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
        RobotStatus = IndicatorViewModel.Unavailable("Robot Status");
        ProgramStatus = IndicatorViewModel.Unavailable("Program Status");
        SafetyStatus = IndicatorViewModel.Unavailable("Safety Status");
        ActiveFault = IndicatorViewModel.Unavailable("Active Fault");
        TcpPosition = IndicatorViewModel.Unavailable("TCP Position");
        TcpSpeed = IndicatorViewModel.Unavailable("TCP Speed");
        PlcConnection = IndicatorViewModel.Connection("PLC Connection", plcSnapshot.Channel);
        ToolStatus = IndicatorViewModel.Unavailable("Tool Status");
        ActiveAlarms = new IndicatorViewModel(
            "Active Alarms",
            activeAlarms.ToString(),
            IndicatorKind.Normal,
            Freshness.Live,
            string.Empty);
        LastUpdate = IndicatorViewModel.Unavailable("Last Update");
        Indicators =
        [
            RobotConnection,
            RobotMode,
            RobotStatus,
            ProgramStatus,
            SafetyStatus,
            ActiveFault,
            TcpPosition,
            TcpSpeed,
            PlcConnection,
            ToolStatus,
            ActiveAlarms,
            LastUpdate
        ];
    }

    public IndicatorViewModel RobotConnection { get; }

    public IndicatorViewModel RobotMode { get; }

    public IndicatorViewModel RobotStatus { get; }

    public IndicatorViewModel ProgramStatus { get; }

    public IndicatorViewModel SafetyStatus { get; }

    public IndicatorViewModel ActiveFault { get; }

    public IndicatorViewModel TcpPosition { get; }

    public IndicatorViewModel TcpSpeed { get; }

    public IndicatorViewModel PlcConnection { get; }

    public IndicatorViewModel ToolStatus { get; }

    public IndicatorViewModel ActiveAlarms { get; }

    public IndicatorViewModel LastUpdate { get; }

    public IReadOnlyList<IndicatorViewModel> Indicators { get; }

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
