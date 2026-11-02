using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrHmi.Application.Observables;
using UrHmi.Domain;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Telemetry;

public sealed class RobotTelemetryService : IHostedService, IRobotTelemetry, IRobotChannelControl, IAsyncDisposable
{
    private static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ProbeWait = TimeSpan.FromSeconds(5);

    private readonly IRobotDashboardClient _dashboard;
    private readonly IRtdeClient _rtde;
    private readonly IAlarmSink _alarms;
    private readonly HmiConfiguration _configuration;
    private readonly ILogger<RobotTelemetryService> _logger;
    private readonly SnapshotBroadcaster<RobotSnapshot> _snapshots;
    private readonly RobotSnapshotAggregator _aggregator = new();
    private readonly object _lifecycle = new();
    private readonly TimeSpan _dashboardStale;
    private readonly TimeSpan _rtdeStale;
    private readonly TimeSpan _dashboardPoll;

    private CancellationTokenSource? _runCts;
    private Task? _dashboardLoop;
    private Task? _rtdeLoop;
    private Task? _watchdogLoop;
    private IDisposable? _frameSubscription;
    private bool _dashboardLinkAlarm;
    private bool _dashboardStaleAlarm;
    private bool _rtdeLinkAlarm;
    private bool _rtdeStaleAlarm;
    private bool _dashboardRetrying;
    private bool _rtdeRetrying;
    private string? _polyscopeVersion;

    public RobotTelemetryService(
        IRobotDashboardClient dashboard,
        IRtdeClient rtde,
        IAlarmSink alarms,
        HmiConfiguration configuration,
        ILogger<RobotTelemetryService> logger)
    {
        _dashboard = dashboard;
        _rtde = rtde;
        _alarms = alarms;
        _configuration = configuration;
        _logger = logger;
        _dashboardStale = TimeSpan.FromMilliseconds(configuration.Robot.Dashboard.StaleTimeoutMs);
        _rtdeStale = TimeSpan.FromMilliseconds(configuration.Robot.Rtde.StaleTimeoutMs);
        _dashboardPoll = TimeSpan.FromMilliseconds(Math.Clamp(configuration.Robot.Dashboard.StaleTimeoutMs / 4.0, 250, 1000));
        _snapshots = new SnapshotBroadcaster<RobotSnapshot>(RobotSnapshot.Empty(DateTimeOffset.UtcNow));
    }

    public RobotSnapshot Current => _snapshots.Current;

    public IObservable<RobotSnapshot> Snapshots => _snapshots;

    public string? PolyscopeVersion => _polyscopeVersion;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _frameSubscription = _rtde.Frames.Subscribe(new DelegateObserver<RtdeFrame>(OnRtdeFrame));
        StartLoops();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await StopLoopsAsync().ConfigureAwait(false);
        _frameSubscription?.Dispose();
        _frameSubscription = null;
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Robot channel reconnect requested.");
        await StopLoopsAsync().ConfigureAwait(false);
        StartLoops();
    }

    public async Task<RobotChannelProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!IpConfigured)
        {
            Publish(DateTimeOffset.UtcNow);
            return BuildProbe("robot.ip is not set; Dashboard Server and RTDE are not connected.");
        }

        var deadline = DateTimeOffset.UtcNow + ProbeWait;
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var snapshot = Current;
            if (snapshot.Dashboard.Link == LinkState.Connected || snapshot.Rtde.Link == LinkState.Connected)
            {
                break;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        if (_dashboard.Health.Link == LinkState.Connected)
        {
            var version = await _dashboard.GetPolyscopeVersionAsync(cancellationToken).ConfigureAwait(false);
            if (version.Success)
            {
                _polyscopeVersion = version.Value;
            }

            await QueryDashboardAsync(cancellationToken).ConfigureAwait(false);
        }

        Publish(DateTimeOffset.UtcNow);
        var current = Current;
        var summary = $"Dashboard {current.Dashboard.Link}; RTDE {current.Rtde.Link}.";
        if (current.Dashboard.LastError is { } dashError)
        {
            summary += $" Dashboard error: {dashError}.";
        }

        if (current.Rtde.LastError is { } rtdeError)
        {
            summary += $" RTDE error: {rtdeError}.";
        }

        if (_rtde.RejectedKeys.Count > 0)
        {
            summary += $" RTDE unavailable keys: {string.Join(", ", _rtde.RejectedKeys)}.";
        }

        return BuildProbe(summary);
    }

    public async ValueTask DisposeAsync()
    {
        await StopLoopsAsync().ConfigureAwait(false);
        _frameSubscription?.Dispose();
    }

    private bool IpConfigured =>
        !_configuration.SafeMode && !string.IsNullOrWhiteSpace(_configuration.Robot.Ip);

    private void StartLoops()
    {
        lock (_lifecycle)
        {
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;
            _dashboardRetrying = false;
            _rtdeRetrying = false;
            _watchdogLoop = Task.Run(() => WatchdogLoopAsync(ct), CancellationToken.None);
            if (!IpConfigured)
            {
                _logger.LogInformation(
                    "Robot telemetry started without connecting. robot.ip is empty or the app is in safe mode.");
                Publish(DateTimeOffset.UtcNow);
                return;
            }

            var host = _configuration.Robot.Ip.Trim();
            _dashboardLoop = Task.Run(() => DashboardLoopAsync(host, ct), CancellationToken.None);
            _rtdeLoop = Task.Run(() => RtdeLoopAsync(host, ct), CancellationToken.None);
        }
    }

    private async Task StopLoopsAsync()
    {
        CancellationTokenSource? cts;
        Task[] tasks;
        lock (_lifecycle)
        {
            cts = _runCts;
            _runCts = null;
            tasks = new[] { _dashboardLoop, _rtdeLoop, _watchdogLoop }
                .OfType<Task>()
                .ToArray();
            _dashboardLoop = null;
            _rtdeLoop = null;
            _watchdogLoop = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            if (tasks.Length > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Loop errors are reflected in ChannelHealth.
        }

        cts?.Dispose();
        await _dashboard.DisconnectAsync().ConfigureAwait(false);
        await _rtde.DisconnectAsync().ConfigureAwait(false);
        _dashboardRetrying = false;
        _rtdeRetrying = false;
        Publish(DateTimeOffset.UtcNow);
    }

    private async Task DashboardLoopAsync(string host, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _dashboardRetrying = attempt > 0;
                _aggregator.SetDashboardHealth(Overlay(
                    _dashboard.Health.WithLink(attempt > 0 ? LinkState.Reconnecting : LinkState.Connecting),
                    _dashboardRetrying));
                Publish(DateTimeOffset.UtcNow);
                await _dashboard.ConnectAsync(host, _configuration.Robot.Dashboard.Port, cancellationToken)
                    .ConfigureAwait(false);
                attempt = 0;
                _dashboardRetrying = false;
                var version = await _dashboard.GetPolyscopeVersionAsync(cancellationToken).ConfigureAwait(false);
                if (version.Success)
                {
                    _polyscopeVersion = version.Value;
                    _logger.LogInformation("PolyScope version: {Version}", version.Value);
                }

                while (!cancellationToken.IsCancellationRequested && _dashboard.Health.Link == LinkState.Connected)
                {
                    await QueryDashboardAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(_dashboardPoll, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard Server channel failed.");
                _aggregator.SetDashboardHealth(_dashboard.Health.WithLink(LinkState.Disconnected, ex.Message));
                Publish(DateTimeOffset.UtcNow);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _dashboardRetrying = true;
            var delay = ReconnectBackoff.Delay(attempt++);
            _logger.LogInformation("Dashboard Server reconnect in {DelaySeconds:0}s.", delay.TotalSeconds);
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

    private async Task RtdeLoopAsync(string host, CancellationToken cancellationToken)
    {
        var attempt = 0;
        var recipe = DefaultRtdeOutputs.CreateRecipe(_configuration.Robot.Rtde.RecipeId);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _rtdeRetrying = attempt > 0;
                _aggregator.SetRtdeHealth(Overlay(
                    _rtde.Health.WithLink(attempt > 0 ? LinkState.Reconnecting : LinkState.Connecting),
                    _rtdeRetrying));
                Publish(DateTimeOffset.UtcNow);
                await _rtde.ConnectAsync(host, _configuration.Robot.Rtde.Port, recipe, cancellationToken)
                    .ConfigureAwait(false);
                attempt = 0;
                _rtdeRetrying = false;
                _aggregator.SetRtdeKeys(_rtde.NegotiatedKeys);
                _aggregator.SetRtdeHealth(_rtde.Health);
                Publish(DateTimeOffset.UtcNow);

                while (!cancellationToken.IsCancellationRequested && _rtde.Health.Link == LinkState.Connected)
                {
                    await Task.Delay(WatchdogPeriod, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RTDE channel failed.");
                _aggregator.SetRtdeHealth(_rtde.Health.WithLink(LinkState.Disconnected, ex.Message));
                Publish(DateTimeOffset.UtcNow);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _rtdeRetrying = true;
            var delay = ReconnectBackoff.Delay(attempt++);
            _logger.LogInformation("RTDE reconnect in {DelaySeconds:0}s.", delay.TotalSeconds);
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

    private async Task WatchdogLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Publish(DateTimeOffset.UtcNow);
                await Task.Delay(WatchdogPeriod, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task QueryDashboardAsync(CancellationToken cancellationToken)
    {
        var mode = await _dashboard.GetRobotModeAsync(cancellationToken).ConfigureAwait(false);
        var safety = await _dashboard.GetSafetyStatusAsync(cancellationToken).ConfigureAwait(false);
        var program = await _dashboard.GetProgramStateAsync(cancellationToken).ConfigureAwait(false);
        var running = await _dashboard.GetRunningAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        _aggregator.ApplyDashboard(
            mode.Success ? mode.Value : null,
            safety.Success ? safety.Value : null,
            program.Success ? program.Value : null,
            running.Success ? running.Value : null,
            now);
        _aggregator.SetDashboardHealth(Overlay(_dashboard.Health, _dashboardRetrying));
        Publish(now);
    }

    private void OnRtdeFrame(RtdeFrame frame)
    {
        _aggregator.ApplyRtdeFrame(frame);
        _aggregator.SetRtdeHealth(Overlay(_rtde.Health, _rtdeRetrying));
        Publish(frame.ReceivedUtc);
    }

    private void Publish(DateTimeOffset now)
    {
        _aggregator.SetDashboardHealth(Overlay(_dashboard.Health, _dashboardRetrying));
        _aggregator.SetRtdeHealth(Overlay(_rtde.Health, _rtdeRetrying));
        var snapshot = _aggregator.Build(now, _dashboardStale, _rtdeStale);
        SyncAlarms(snapshot, now);
        _snapshots.Publish(snapshot);
    }

    private ChannelHealth Overlay(ChannelHealth health, bool retrying)
    {
        if (retrying && health.Link is LinkState.Disconnected or LinkState.Faulted)
        {
            return health.WithLink(LinkState.Reconnecting, health.LastError);
        }

        return health;
    }

    private void SyncAlarms(RobotSnapshot snapshot, DateTimeOffset now)
    {
        if (!IpConfigured)
        {
            return;
        }

        Sync(
            ref _dashboardLinkAlarm,
            snapshot.Dashboard.Link is LinkState.Disconnected or LinkState.Faulted or LinkState.Reconnecting,
            RobotCommsAlarms.DashboardLink,
            "Dashboard Server channel is not connected.",
            now);
        Sync(
            ref _rtdeLinkAlarm,
            snapshot.Rtde.Link is LinkState.Disconnected or LinkState.Faulted or LinkState.Reconnecting,
            RobotCommsAlarms.RtdeLink,
            "RTDE channel is not connected.",
            now);
        Sync(
            ref _dashboardStaleAlarm,
            snapshot.Dashboard.Link == LinkState.Connected
            && snapshot.Dashboard.Age is { } dashboardAge
            && dashboardAge > _dashboardStale,
            RobotCommsAlarms.DashboardStale,
            "Dashboard Server data is stale.",
            now);
        Sync(
            ref _rtdeStaleAlarm,
            snapshot.Rtde.Link == LinkState.Connected
            && snapshot.Rtde.Age is { } rtdeAge
            && rtdeAge > _rtdeStale,
            RobotCommsAlarms.RtdeStale,
            "RTDE data is stale.",
            now);
    }

    private void Sync(ref bool active, bool shouldBeActive, string id, string description, DateTimeOffset now)
    {
        if (active == shouldBeActive)
        {
            return;
        }

        active = shouldBeActive;
        RobotCommsAlarms.SetLink(_alarms, id, description, shouldBeActive, now);
    }

    private RobotChannelProbe BuildProbe(string summary)
    {
        var snapshot = Current;
        return new RobotChannelProbe(
            snapshot.ProducedUtc,
            IpConfigured,
            snapshot.Dashboard,
            snapshot.Rtde,
            _polyscopeVersion,
            _rtde.NegotiatedKeys,
            _rtde.RejectedKeys,
            summary);
    }
}
