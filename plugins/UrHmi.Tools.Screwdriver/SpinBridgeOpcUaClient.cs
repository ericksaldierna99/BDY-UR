using UrHmi.Domain;

namespace UrHmi.Tools.Screwdriver;

public sealed class SpinBridgeOpcUaClient : ISpinBridgeClient
{
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(100);

    private readonly ScrewdriverConfiguration _configuration;
    private readonly IOpcUaChannel _channel;
    private readonly bool _ownsChannel;
    private readonly SpinBridgeReadingAggregator _aggregator = new();
    private readonly ReadingBroadcaster _readings = new(SpinBridgeReading.Unavailable());
    private readonly TimeSpan _staleTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly object _lifecycle = new();
    private CancellationTokenSource? _runCts;
    private Task? _runLoop;
    private Task? _watchdogLoop;
    private string? _latestResultId;
    private int _started;
    private int _disposed;

    public SpinBridgeOpcUaClient(ScrewdriverConfiguration configuration, IOpcUaChannel? channel = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _ownsChannel = channel is null;
        _channel = channel ?? new OpcUaChannel();
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
    }

    public IDisposable Subscribe(IObserver<SpinBridgeReading> observer) => _readings.Subscribe(observer);

    private bool IpConfigured => !string.IsNullOrWhiteSpace(_configuration.SpinBridgeIp);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!IpConfigured)
        {
            _aggregator.SetChannel(ChannelHealth.Disconnected(
                SpinBridgeReading.ChannelId,
                "spinBridge.ip is not set; OPC UA was not connected."));
            Publish();
            return;
        }

        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _latestResultId = null;
                _aggregator.SetChannel(new ChannelHealth(
                    SpinBridgeReading.ChannelId,
                    attempt > 0 ? LinkState.Reconnecting : LinkState.Connecting,
                    null,
                    null,
                    null));
                Publish();

                await _channel.ConnectAsync(_configuration.OpcUaEndpoint, cancellationToken).ConfigureAwait(false);
                attempt = 0;
                _aggregator.SetChannel(new ChannelHealth(
                    SpinBridgeReading.ChannelId,
                    LinkState.Connected,
                    DateTimeOffset.UtcNow,
                    TimeSpan.Zero,
                    null));
                Publish();

                while (!cancellationToken.IsCancellationRequested && _channel.IsConnected)
                {
                    await PollAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }

                if (!cancellationToken.IsCancellationRequested && !_channel.IsConnected)
                {
                    _aggregator.SetChannel(
                        ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, "OPC UA session dropped."));
                    Publish();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _aggregator.SetChannel(
                    ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, ex.Message));
                Publish();
                try
                {
                    await _channel.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort close before reconnect.
                }
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

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_staleTimeout);
        var ct = timeout.Token;

        _latestResultId ??= await ResolveLatestResultAsync(ct).ConfigureAwait(false);
        if (_latestResultId is null)
        {
            _aggregator.ApplyPoll(DateTimeOffset.UtcNow, null, null);
            Publish();
            return;
        }

        var pair = await ReadLatestAsync(_latestResultId, ct).ConfigureAwait(false);
        _aggregator.ApplyPoll(DateTimeOffset.UtcNow, pair.TorqueNm, pair.AngleDeg);
        Publish();
    }

    private async Task<string?> ResolveLatestResultAsync(CancellationToken cancellationToken)
    {
        string?[] candidates =
        [
            await WalkAsync(
                SpinBridgeNodes.TighteningSystem,
                [SpinBridgeNodes.ResultManagementName, SpinBridgeNodes.ResultsName, SpinBridgeNodes.LatestResultName],
                cancellationToken).ConfigureAwait(false),
            await WalkAsync(
                SpinBridgeNodes.ResultManagement,
                [SpinBridgeNodes.ResultsName, SpinBridgeNodes.LatestResultName],
                cancellationToken).ConfigureAwait(false),
            await WalkAsync(
                SpinBridgeNodes.ObjectsFolder,
                [
                    SpinBridgeNodes.TighteningSystemName,
                    SpinBridgeNodes.ResultManagementName,
                    SpinBridgeNodes.ResultsName,
                    SpinBridgeNodes.LatestResultName
                ],
                cancellationToken).ConfigureAwait(false)
        ];

        return candidates.FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
    }

    private async Task<string?> WalkAsync(
        string startNodeId,
        IReadOnlyList<string> path,
        CancellationToken cancellationToken)
    {
        var current = startNodeId;
        try
        {
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<SpinBridgeMeasuredPair> ReadLatestAsync(string latestResultId, CancellationToken cancellationToken)
    {
        var children = await _channel.BrowseChildrenAsync(latestResultId, cancellationToken).ConfigureAwait(false);
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        object? overall = null;
        foreach (var child in children)
        {
            object? value;
            try
            {
                value = await _channel.ReadValueAsync(child.NodeId, cancellationToken).ConfigureAwait(false);
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

        object? latest = null;
        if (fields.Count == 0 && overall is null)
        {
            try
            {
                latest = await _channel.ReadValueAsync(latestResultId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                latest = null;
            }
        }

        return SpinBridgeResultDecoder.Decode(fields, overall, latest);
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
