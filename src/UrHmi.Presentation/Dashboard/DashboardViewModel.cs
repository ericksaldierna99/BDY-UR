using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UrHmi.Application.Alarms;
using UrHmi.Application.Observables;
using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Presentation.Shell;

namespace UrHmi.Presentation.Dashboard;

/// <summary>
/// Dashboard workspace ViewModel. Public so WPF <c>x:Type</c> DataTemplates can resolve it.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly AlarmQuery ActiveQuery = new(null, null, null, null, null, IsActive: true);

    private readonly IUiDispatcher _dispatcher;
    private readonly IAlarmStore _alarms;
    private readonly IDisposable _robotSubscription;
    private readonly IDisposable _plcSubscription;
    private int _disposed;

    public DashboardViewModel(IRobotTelemetry robot, IPlcTelemetry plc, IAlarmStore alarms, IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _alarms = alarms;

        RobotConnection = IndicatorViewModel.Unavailable("Robot Connection");
        RobotMode = IndicatorViewModel.Unavailable("Robot Mode");
        SafetyStatus = IndicatorViewModel.Unavailable("Safety Status");
        ActiveFault = IndicatorViewModel.Unavailable("Active Fault");
        PlcConnection = IndicatorViewModel.Unavailable("PLC Connection");
        ActiveAlarms = IndicatorViewModel.Unavailable("Active Alarms");
        ActiveAlarms.Command = OpenActiveAlarmsCommand;
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

        Apply(robot.Current, plc.Current, alarms.CountActiveAsync().GetAwaiter().GetResult());
        _robotSubscription = robot.Snapshots.Subscribe(new DelegateObserver<RobotSnapshot>(snapshot =>
            OnUi(() => Apply(snapshot, plc.Current, _alarms.CountActiveAsync().GetAwaiter().GetResult()))));
        _plcSubscription = plc.Snapshots.Subscribe(new DelegateObserver<PlcSnapshot>(snapshot =>
            OnUi(() => Apply(robot.Current, snapshot, _alarms.CountActiveAsync().GetAwaiter().GetResult()))));
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

    public string ActiveAlarmsEmptyMessage => "No active alarms.";

    public ObservableCollection<ActiveAlarmRow> ActiveAlarmItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoActiveAlarms))]
    private bool _isActiveAlarmsDialogOpen;

    public bool HasNoActiveAlarms => ActiveAlarmItems.Count == 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _robotSubscription.Dispose();
        _plcSubscription.Dispose();
    }

    [RelayCommand]
    private void OpenActiveAlarms()
    {
        RefreshActiveAlarms();
        IsActiveAlarmsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseActiveAlarms()
    {
        IsActiveAlarmsDialogOpen = false;
    }

    private void Apply(RobotSnapshot robot, PlcSnapshot plc, int activeAlarms)
    {
        var connection = RobotDashboardProjection.ProjectConnection(robot.Dashboard, robot.Rtde);
        RobotConnection.Apply(connection.Text, connection.Kind, Freshness.Live);

        RobotMode.Apply(
            RobotDashboardProjection.FormatTimed(robot.RobotMode.Value, robot.RobotMode.Freshness),
            robot.RobotMode.Freshness == Freshness.Unavailable
                ? IndicatorKind.Normal
                : RobotDashboardProjection.KindForRobotMode(robot.RobotMode.Value),
            robot.RobotMode.Freshness);

        SafetyStatus.Apply(
            RobotDashboardProjection.FormatTimed(robot.SafetyStatus.Value, robot.SafetyStatus.Freshness),
            robot.SafetyStatus.Freshness == Freshness.Unavailable
                ? IndicatorKind.Normal
                : RobotDashboardProjection.KindForSafety(robot.SafetyStatus.Value),
            robot.SafetyStatus.Freshness);

        ActiveFault.Apply(
            RobotDashboardProjection.FormatTimed(robot.ActiveFault.Value, robot.ActiveFault.Freshness),
            robot.ActiveFault.Freshness == Freshness.Unavailable
                ? IndicatorKind.Normal
                : RobotDashboardProjection.KindForActiveFault(robot.ActiveFault.Value),
            robot.ActiveFault.Freshness);

        PlcConnection.ApplyFromConnection(plc.Channel);
        var alarmKind = activeAlarms > 0 ? IndicatorKind.Warning : IndicatorKind.Normal;
        ActiveAlarms.Apply(activeAlarms.ToString(), alarmKind, Freshness.Live);
        TcpPosition.Apply(
            RobotDashboardProjection.FormatTcpPosition(robot.TcpPosition),
            IndicatorKind.Normal,
            robot.TcpPosition.Freshness);
        TcpSpeed.Apply(
            RobotDashboardProjection.FormatTcpSpeed(robot.TcpSpeed),
            IndicatorKind.Normal,
            robot.TcpSpeed.Freshness);

        if (IsActiveAlarmsDialogOpen)
        {
            RefreshActiveAlarms();
        }
    }

    private void RefreshActiveAlarms()
    {
        var records = _alarms.QueryAsync(ActiveQuery).GetAwaiter().GetResult();
        var rows = ActiveAlarmList.Project(records);
        ActiveAlarmItems.Clear();
        foreach (var row in rows)
        {
            ActiveAlarmItems.Add(row);
        }

        OnPropertyChanged(nameof(HasNoActiveAlarms));
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
