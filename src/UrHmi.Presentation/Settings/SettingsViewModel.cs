using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UrHmi.Application.Observables;
using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Robot;
using UrHmi.Presentation.Shell;

namespace UrHmi.Presentation.Settings;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IRobotChannelControl _robotControl;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDisposable _robotSubscription;
    private int _disposed;

    public SettingsViewModel(
        HmiConfiguration configuration,
        IRobotTelemetry robot,
        IRobotChannelControl robotControl,
        IUiDispatcher dispatcher)
    {
        Configuration = configuration;
        _robotControl = robotControl;
        _dispatcher = dispatcher;
        SafeModeMessage = configuration.SafeMode
            ? "Started in safe mode: configuration was missing, invalid, or contained sensitive keys."
            : null;
        ApplySnapshot(robot.Current);
        ProbeSummary = configuration.SafeMode || string.IsNullOrWhiteSpace(configuration.Robot.Ip)
            ? "robot.ip is not set; Test connection will not open sockets."
            : "Use Test connection to sample Dashboard Server and RTDE, or Reconnect to restart both channels.";
        _robotSubscription = robot.Snapshots.Subscribe(new DelegateObserver<RobotSnapshot>(snapshot =>
            OnUi(() => ApplySnapshot(snapshot))));
    }

    public string Title => "Settings";

    public HmiConfiguration Configuration { get; }

    public string? SafeModeMessage { get; }

    public string RobotIp => string.IsNullOrWhiteSpace(Configuration.Robot.Ip)
        ? "(not set)"
        : Configuration.Robot.Ip;

    public int DashboardPort => Configuration.Robot.Dashboard.Port;

    public int RtdePort => Configuration.Robot.Rtde.Port;

    public string PlcIp => string.IsNullOrWhiteSpace(Configuration.Plc.Ip)
        ? "(not set)"
        : Configuration.Plc.Ip;

    public string PlcTransport => Configuration.Plc.Transport;

    public string Notice =>
        "Connection diagnostics are available. Applying IP/interval edits, permissions, and secret storage are not in this phase. Secrets are not stored in JSON.";

    [ObservableProperty]
    private string _dashboardLinkText = LinkState.Disconnected.ToString();

    [ObservableProperty]
    private string _dashboardAgeText = "—";

    [ObservableProperty]
    private string _dashboardErrorText = "—";

    [ObservableProperty]
    private string _rtdeLinkText = LinkState.Disconnected.ToString();

    [ObservableProperty]
    private string _rtdeAgeText = "—";

    [ObservableProperty]
    private string _rtdeErrorText = "—";

    [ObservableProperty]
    private string _polyscopeVersionText = "—";

    [ObservableProperty]
    private string _rtdeKeysText = "—";

    [ObservableProperty]
    private string _probeSummary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool _isBusy;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _robotSubscription.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanRunDiagnostics))]
    private async Task TestConnectionAsync()
    {
        await RunDiagnosticsAsync(() => _robotControl.ProbeAsync()).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRunDiagnostics))]
    private async Task ReconnectAsync()
    {
        await RunDiagnosticsAsync(() => _robotControl.ReconnectAsync()).ConfigureAwait(true);
    }

    private bool CanRunDiagnostics() => !IsBusy;

    private async Task RunDiagnosticsAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action().ConfigureAwait(false);
            var probe = await _robotControl.ProbeAsync().ConfigureAwait(false);
            OnUi(() => ApplyProbe(probe));
        }
        catch (Exception ex)
        {
            OnUi(() => ProbeSummary = ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(RobotSnapshot snapshot)
    {
        DashboardLinkText = snapshot.Dashboard.Link.ToString();
        DashboardAgeText = RobotDashboardProjection.FormatAge(snapshot.Dashboard.Age);
        DashboardErrorText = string.IsNullOrWhiteSpace(snapshot.Dashboard.LastError)
            ? "—"
            : snapshot.Dashboard.LastError;
        RtdeLinkText = snapshot.Rtde.Link.ToString();
        RtdeAgeText = RobotDashboardProjection.FormatAge(snapshot.Rtde.Age);
        RtdeErrorText = string.IsNullOrWhiteSpace(snapshot.Rtde.LastError)
            ? "—"
            : snapshot.Rtde.LastError;
        PolyscopeVersionText = string.IsNullOrWhiteSpace(_robotControl.PolyscopeVersion)
            ? "—"
            : _robotControl.PolyscopeVersion;
    }

    private void ApplyProbe(RobotChannelProbe probe)
    {
        DashboardLinkText = probe.Dashboard.Link.ToString();
        DashboardAgeText = RobotDashboardProjection.FormatAge(probe.Dashboard.Age);
        DashboardErrorText = string.IsNullOrWhiteSpace(probe.Dashboard.LastError) ? "—" : probe.Dashboard.LastError;
        RtdeLinkText = probe.Rtde.Link.ToString();
        RtdeAgeText = RobotDashboardProjection.FormatAge(probe.Rtde.Age);
        RtdeErrorText = string.IsNullOrWhiteSpace(probe.Rtde.LastError) ? "—" : probe.Rtde.LastError;
        PolyscopeVersionText = string.IsNullOrWhiteSpace(probe.PolyscopeVersion) ? "—" : probe.PolyscopeVersion;
        RtdeKeysText = probe.RtdeNegotiatedKeys.Count == 0 && probe.RtdeRejectedKeys.Count == 0
            ? "—"
            : $"ok: {string.Join(", ", probe.RtdeNegotiatedKeys)}; unavailable: {string.Join(", ", probe.RtdeRejectedKeys)}";
        ProbeSummary = probe.Summary;
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }
}
