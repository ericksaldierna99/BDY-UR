using CommunityToolkit.Mvvm.ComponentModel;
using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Ports;

namespace UrHmi.Presentation.Dashboard;

/// <summary>
/// Dashboard workspace ViewModel. Public so WPF <c>x:Type</c> DataTemplates can resolve it.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    public DashboardViewModel(IRobotTelemetry robot, IPlcTelemetry plc, IAlarmStore alarms)
    {
        var snapshot = robot.Current;
        var plcSnapshot = plc.Current;
        var activeAlarms = alarms.CountActiveAsync().GetAwaiter().GetResult();

        RobotConnection = ProjectRobotConnection(snapshot.Dashboard, snapshot.Rtde);
        RobotMode = IndicatorViewModel.Unavailable("Robot Mode");
        SafetyStatus = IndicatorViewModel.Unavailable("Safety Status");
        ActiveFault = IndicatorViewModel.Unavailable("Active Fault");
        PlcConnection = IndicatorViewModel.Connection("PLC Connection", plcSnapshot.Channel);
        ActiveAlarms = new IndicatorViewModel(
            "Active Alarms",
            activeAlarms.ToString(),
            IndicatorKind.Normal,
            Freshness.Live,
            string.Empty);
        TcpPosition = IndicatorViewModel.Unavailable("TCP Position");
        TcpSpeed = IndicatorViewModel.Unavailable("TCP Speed");
        Indicators =
        [
            RobotConnection,
            RobotMode,
            SafetyStatus,
            ActiveFault,
            PlcConnection,
            ActiveAlarms
        ];

        StationStatusText = "—";
        StationStatusHint = "Stop · Idle · Running · Starving";
        StationStatusCaption = "sin dato";
        UphText = "—";
        CurrentCycleTimeText = "—";
        PreviousCycleTimeText = "—";
    }

    public IndicatorViewModel RobotConnection { get; }

    public IndicatorViewModel RobotMode { get; }

    public IndicatorViewModel SafetyStatus { get; }

    public IndicatorViewModel ActiveFault { get; }

    public IndicatorViewModel PlcConnection { get; }

    public IndicatorViewModel ActiveAlarms { get; }

    public IndicatorViewModel TcpPosition { get; }

    public IndicatorViewModel TcpSpeed { get; }

    public IReadOnlyList<IndicatorViewModel> Indicators { get; }

    public string StationStatusText { get; }

    public string StationStatusHint { get; }

    public string StationStatusCaption { get; }

    public string UphText { get; }

    public string CurrentCycleTimeText { get; }

    public string PreviousCycleTimeText { get; }

    private static IndicatorViewModel ProjectRobotConnection(ChannelHealth dashboard, ChannelHealth rtde)
    {
        var dashboardUp = dashboard.Link == LinkState.Connected;
        var rtdeUp = rtde.Link == LinkState.Connected;
        if (dashboardUp && rtdeUp)
        {
            return IndicatorViewModel.Connection("Robot Connection", dashboard);
        }

        if (dashboardUp || rtdeUp)
        {
            return IndicatorViewModel.DegradedConnection("Robot Connection");
        }

        return IndicatorViewModel.Connection("Robot Connection", dashboard);
    }
}
