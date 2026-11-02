using UrHmi.Domain;

namespace UrHmi.Tools.Screwdriver;

public sealed class SpinBridgeOpcUaClient : ISpinBridgeClient
{
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan PortProbeTimeout = TimeSpan.FromMilliseconds(400);

    private readonly ScrewdriverConfiguration _configuration;
    private readonly IOpcUaChannel _channel;
    private readonly ISpinHttpApi? _http;
    private readonly IOpcPortProbe? _portProbe;
    private readonly bool _ownsChannel;
    private readonly bool _ownsHttp;
    private readonly SpinBridgeReadingAggregator _aggregator = new();
    private readonly ReadingBroadcaster _readings = new(SpinBridgeReading.Unavailable());
    private readonly TimeSpan _staleTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly object _lifecycle = new();
    private CancellationTokenSource? _runCts;
    private Task? _runLoop;
    private Task? _watchdogLoop;
    private string? _latestResultId;
    private string? _resolveError;
    private string? _lastError;
    private string? _lastOpcError;
    private int? _connectedPort;
    private bool _hadOpcSession;
    private int _started;
    private int _disposed;

    public SpinBridgeOpcUaClient(
        ScrewdriverConfiguration configuration,
        IOpcUaChannel? channel = null,
        ISpinHttpApi? http = null,
        IOpcPortProbe? portProbe = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _ownsChannel = channel is null;
        _channel = channel ?? new OpcUaChannel();
        _ownsHttp = http is null && _ownsChannel;
        _http = http ?? (_ownsChannel ? new SpinDashboardHttpClient() : null);
        _portProbe = portProbe ?? (_ownsChannel ? new TcpOpcPortProbe() : null);
        _staleTimeout = TimeSpan.FromMilliseconds(Math.Max(1, configuration.StaleTimeoutMs));
        _pollInterval = TimeSpan.FromMilliseconds(Math.Clamp(configuration.StaleTimeoutMs / 4.0, 250, 1000));
    }

    public SpinBridgeReading Current => _readings.Current;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        lock (_lifecycle)
        {
            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;
            _watchdogLoop = Task.Run(() => WatchdogAsync(ct), CancellationToken.None);
            _runLoop = Task.Run(() => RunAsync(ct), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopLoops();
        if (_ownsChannel)
        {
            _channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_ownsHttp && _http is IDisposable disposableHttp)
        {
            disposableHttp.Dispose();
        }
    }

    public IDisposable Subscribe(IObserver<SpinBridgeReading> observer) => _readings.Subscribe(observer);

    private bool IpConfigured => !string.IsNullOrWhiteSpace(_configuration.SpinBridgeIp);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!IpConfigured)
        {
            _lastError = "spinBridge.ip is not set; OPC UA was not connected.";
            _aggregator.SetChannel(ChannelHealth.Disconnected(
                SpinBridgeReading.ChannelId,
                _lastError));
            Publish();
            return;
        }

        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!_channel.IsConnected)
                {
                    if (_hadOpcSession)
                    {
                        _hadOpcSession = false;
                        _connectedPort = null;
                        _lastOpcError ??= "OPC UA session dropped.";
                        _lastError = _lastOpcError;
                        _aggregator.SetChannel(
                            ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, _lastError));
                        Publish();
                        await PollHttpAsync(cancellationToken).ConfigureAwait(false);
                        var dropDelay = SpinBridgeReconnectBackoff.Delay(attempt++);
                        await Task.Delay(dropDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    _latestResultId = null;
                    _resolveError = null;
                    _connectedPort = null;
                    _aggregator.SetChannel(new ChannelHealth(
                        SpinBridgeReading.ChannelId,
                        attempt > 0 ? LinkState.Reconnecting : LinkState.Connecting,
                        null,
                        null,
                        _lastError));
                    Publish();

                    await TryConnectOpcAsync(cancellationToken).ConfigureAwait(false);
                    if (_channel.IsConnected)
                    {
                        _hadOpcSession = true;
                    }
                }

                var hasOpcValues = false;
                if (_channel.IsConnected)
                {
                    attempt = 0;
                    await PollAsync(cancellationToken).ConfigureAwait(false);
                    hasOpcValues = HasLiveValues();
                }

                if (!hasOpcValues)
                {
                    await PollHttpAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!cancellationToken.IsCancellationRequested && !_channel.IsConnected && !HasLiveValues())
                {
                    var delay = SpinBridgeReconnectBackoff.Delay(attempt++);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastOpcError = ex.Message;
                _lastError = ex.Message;
                _aggregator.SetChannel(
                    ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, _lastError));
                Publish();
                try
                {
                    await _channel.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort close before reconnect.
                }

                try
                {
                    await PollHttpAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // HTTP is a fallback; OPC error stays in LastError.
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var delay = SpinBridgeReconnectBackoff.Delay(attempt++);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task TryConnectOpcAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var ports = _connectedPort is { } last
            ? _configuration.OpcUaPortsToProbe.Prepend(last).Distinct()
            : _configuration.OpcUaPortsToProbe;

        if (_portProbe is null)
        {
            ports = [_configuration.OpcUaPort];
        }

        foreach (var port in ports)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var endpoint = OpcEndpointUrls.Canonical(_configuration.SpinBridgeIp, port);
            if (_portProbe is not null)
            {
                var open = await _portProbe
                    .IsOpenAsync(_configuration.SpinBridgeIp, port, PortProbeTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (!open)
                {
                    errors.Add($"{endpoint} closed");
                    continue;
                }
            }

            try
            {
                await _channel.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (_channel.IsConnected)
                {
                    _connectedPort = port;
                    _lastOpcError = null;
                    _lastError = null;
                    _aggregator.SetChannel(new ChannelHealth(
                        SpinBridgeReading.ChannelId,
                        LinkState.Connected,
                        DateTimeOffset.UtcNow,
                        TimeSpan.Zero,
                        null));
                    Publish();
                    return;
                }

                errors.Add($"{endpoint} session not connected");
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{endpoint}: {ex.Message}");
                try
                {
                    await _channel.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }

        _lastOpcError = errors.Count == 0
            ? $"OPC UA {_configuration.OpcUaEndpoint} did not connect."
            : "OPC UA " + string.Join("; ", errors);
        _lastError = _lastOpcError;
        _aggregator.SetChannel(
            ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, _lastError));
        Publish();
    }

    private async Task PollHttpAsync(CancellationToken cancellationToken)
    {
        if (_http is null)
        {
            return;
        }

        SpinHttpRead read;
        try
        {
            read = await _http.ReadLatestAsync(_configuration.HttpBase, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            _lastError = CombineErrors(_lastOpcError, ex.Message);
            if (!_channel.IsConnected)
            {
                _aggregator.SetChannel(
                    ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, _lastError));
            }

            Publish();
            return;
        }

        if (read.TorqueNm is not null || read.AngleDeg is not null)
        {
            var viaHttp = string.IsNullOrWhiteSpace(read.Path)
                ? _configuration.HttpBase
                : _configuration.HttpBase + read.Path;
            _lastError = string.IsNullOrWhiteSpace(_lastOpcError)
                ? null
                : $"{_lastOpcError}. Using HTTP {viaHttp}.";
            _aggregator.ApplyPoll(DateTimeOffset.UtcNow, read.TorqueNm, read.AngleDeg, _lastError);
            Publish();
            return;
        }

        _lastError = CombineErrors(_lastOpcError, read.Error);
        if (_channel.IsConnected)
        {
            _aggregator.ApplyPoll(DateTimeOffset.UtcNow, null, null, _lastError);
        }
        else
        {
            _aggregator.SetChannel(
                ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, _lastError));
        }

        Publish();
    }

    private bool HasLiveValues()
    {
        var current = Current;
        return current.TorqueNm.Freshness == Freshness.Live
            || current.AngleDeg.Freshness == Freshness.Live;
    }

    private static string? CombineErrors(string? opc, string? http)
    {
        if (string.IsNullOrWhiteSpace(opc))
        {
            return http;
        }

        if (string.IsNullOrWhiteSpace(http))
        {
            return opc;
        }

        return $"{opc}. {http}";
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_staleTimeout);
        var ct = timeout.Token;

        _latestResultId ??= await ResolveLatestResultAsync(ct).ConfigureAwait(false);
        if (_latestResultId is null)
        {
            _lastError = _resolveError
                ?? "LatestResult was not found under TighteningSystem/ResultManagement/Results.";
            _lastOpcError = _lastError;
            _aggregator.ApplyPoll(DateTimeOffset.UtcNow, null, null, _lastError);
            Publish();
            return;
        }

        var (pair, latest) = await ReadLatestAsync(_latestResultId, ct).ConfigureAwait(false);
        var decodeError = SpinBridgeResultDecoder.DescribeFailure(latest, pair);
        _lastError = decodeError;
        _lastOpcError = decodeError;
        _aggregator.ApplyPoll(DateTimeOffset.UtcNow, pair.TorqueNm, pair.AngleDeg, decodeError);
        Publish();
    }

    private async Task<string?> ResolveLatestResultAsync(CancellationToken cancellationToken)
    {
        (string Start, string[] Path, string Label)[] candidates =
        [
            (
                SpinBridgeNodes.TighteningSystemAbsolute,
                [SpinBridgeNodes.ResultManagementName, SpinBridgeNodes.ResultsName, SpinBridgeNodes.LatestResultName],
                "TighteningSystem (nsu)"
            ),
            (
                SpinBridgeNodes.TighteningSystem,
                [SpinBridgeNodes.ResultManagementName, SpinBridgeNodes.ResultsName, SpinBridgeNodes.LatestResultName],
                "TighteningSystem ns=1;i=5003"
            ),
            (
                SpinBridgeNodes.ResultManagementAbsolute,
                [SpinBridgeNodes.ResultsName, SpinBridgeNodes.LatestResultName],
                "ResultManagement (nsu)"
            ),
            (
                SpinBridgeNodes.ResultManagement,
                [SpinBridgeNodes.ResultsName, SpinBridgeNodes.LatestResultName],
                "ResultManagement ns=1;i=5011"
            ),
            (
                SpinBridgeNodes.ObjectsFolder,
                [
                    SpinBridgeNodes.TighteningSystemName,
                    SpinBridgeNodes.ResultManagementName,
                    SpinBridgeNodes.ResultsName,
                    SpinBridgeNodes.LatestResultName
                ],
                "Objects i=85"
            )
        ];

        string? lastMiss = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var id = await WalkAsync(candidate.Start, candidate.Path, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _resolveError = null;
                    return id;
                }

                lastMiss = $"{candidate.Label} did not contain {string.Join('/', candidate.Path)}.";
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastMiss = $"{candidate.Label}: {ex.Message}";
            }
        }

        _resolveError = lastMiss
            ?? "LatestResult was not found under TighteningSystem/ResultManagement/Results.";
        return null;
    }

    private async Task<string?> WalkAsync(
        string startNodeId,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken)
    {
        var current = startNodeId;
        foreach (var name in path)
        {
            var children = await _channel.BrowseChildrenAsync(current, cancellationToken).ConfigureAwait(false);
            var next = children.FirstOrDefault(child => NamesMatch(child, name));
            if (next is null)
            {
                return null;
            }

            current = next.NodeId;
        }

        return current;
    }

    private async Task<(SpinBridgeMeasuredPair Pair, object? Latest)> ReadLatestAsync(
        string latestResultId,
        CancellationToken cancellationToken)
    {
        // LatestResult is a Variable of JoiningResultDataType, not a folder. Always read it.
        var latest = await _channel.ReadValueAsync(latestResultId, cancellationToken).ConfigureAwait(false);

        Dictionary<string, object?>? fields = null;
        object? overall = null;
        try
        {
            var children = await _channel.BrowseChildrenAsync(latestResultId, cancellationToken)
                .ConfigureAwait(false);
            if (children.Count > 0)
            {
                fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in children)
                {
                    object? value;
                    try
                    {
                        value = await _channel.ReadValueAsync(child.NodeId, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        continue;
                    }

                    if (SpinBridgeResultDecoder.IsOverallName(child.BrowseName)
                        || SpinBridgeResultDecoder.IsOverallName(child.DisplayName))
                    {
                        overall = value;
                        continue;
                    }

                    fields[child.BrowseName] = value;
                    if (!string.Equals(child.DisplayName, child.BrowseName, StringComparison.Ordinal))
                    {
                        fields[child.DisplayName] = value;
                    }
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Child browse is optional; the Variable value is the JoiningResult payload.
        }

        return (SpinBridgeResultDecoder.Decode(fields, overall, latest), latest);
    }

    private async Task WatchdogAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Publish();
                await Task.Delay(WatchdogPeriod, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void Publish() => _readings.Publish(_aggregator.Build(DateTimeOffset.UtcNow, _staleTimeout));

    private void StopLoops()
    {
        CancellationTokenSource? cts;
        Task[] tasks;
        lock (_lifecycle)
        {
            cts = _runCts;
            _runCts = null;
            tasks = new[] { _runLoop, _watchdogLoop }.OfType<Task>().ToArray();
            _runLoop = null;
            _watchdogLoop = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            if (tasks.Length > 0)
            {
                Task.WaitAll(tasks, TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception)
        {
            // Loop errors are reflected in ChannelHealth.
        }

        cts?.Dispose();
        try
        {
            _channel.DisconnectAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        _aggregator.SetChannel(ChannelHealth.Disconnected(SpinBridgeReading.ChannelId));
        Publish();
    }

    private static bool NamesMatch(OpcBrowseNode node, string expected) =>
        string.Equals(node.BrowseName, expected, StringComparison.OrdinalIgnoreCase)
        || string.Equals(node.DisplayName, expected, StringComparison.OrdinalIgnoreCase);

    private sealed class ReadingBroadcaster : IObservable<SpinBridgeReading>
    {
        private readonly object _gate = new();
        private readonly List<IObserver<SpinBridgeReading>> _observers = [];
        private SpinBridgeReading _current;

        public ReadingBroadcaster(SpinBridgeReading initial) => _current = initial;

        public SpinBridgeReading Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public void Publish(SpinBridgeReading value)
        {
            IObserver<SpinBridgeReading>[] observers;
            lock (_gate)
            {
                _current = value;
                observers = _observers.ToArray();
            }

            foreach (var observer in observers)
            {
                observer.OnNext(value);
            }
        }

        public IDisposable Subscribe(IObserver<SpinBridgeReading> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            SpinBridgeReading current;
            lock (_gate)
            {
                _observers.Add(observer);
                current = _current;
            }

            observer.OnNext(current);
            return new Subscription(this, observer);
        }

        private void Unsubscribe(IObserver<SpinBridgeReading> observer)
        {
            lock (_gate)
            {
                _observers.Remove(observer);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private ReadingBroadcaster? _owner;
            private readonly IObserver<SpinBridgeReading> _observer;

            public Subscription(ReadingBroadcaster owner, IObserver<SpinBridgeReading> observer)
            {
                _owner = owner;
                _observer = observer;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.Unsubscribe(_observer);
            }
        }
    }
}
