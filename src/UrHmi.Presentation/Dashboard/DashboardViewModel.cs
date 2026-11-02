using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UrHmi.Application.Alarms;
using UrHmi.Application.Jog;
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

    private readonly IRobotTelemetry _robot;
    private readonly IPlcTelemetry _plc;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAlarmStore _alarms;
    private readonly IRobotJogService _jog;
    private readonly IUserPrompt _prompt;
    private readonly IDisposable _robotSubscription;
    private readonly IDisposable _plcSubscription;
    private CancellationTokenSource? _deleteHoldCts;
    private int _disposed;

    public DashboardViewModel(
        IRobotTelemetry robot,
        IPlcTelemetry plc,
        IAlarmStore alarms,
        IRobotJogService jog,
        IUserPrompt prompt,
        IUiDispatcher dispatcher)
    {
        _robot = robot;
        _plc = plc;
        _dispatcher = dispatcher;
        _alarms = alarms;
        _jog = jog;
        _prompt = prompt;

        RobotConnection = IndicatorViewModel.Unavailable("Robot Connection");
        RobotMode = IndicatorViewModel.Unavailable("Robot Mode");
        SafetyStatus = IndicatorViewModel.Unavailable("Safety Status");
        ActiveFault = IndicatorViewModel.Unavailable("Active Fault");
        PlcConnection = IndicatorViewModel.Unavailable("PLC Connection");
        ActiveAlarms = IndicatorViewModel.Unavailable("Active Alarms");
        TcpPosition = IndicatorViewModel.Unavailable("TCP Position");
        TcpSpeed = IndicatorViewModel.Unavailable("TCP Speed");
        JogSlots = Enumerable.Range(1, SavedJogPose.SlotCount).Select(slot => new JogSlotItem(slot)).ToArray();
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

        ActiveAlarms.Command = OpenActiveAlarmsCommand;
        RobotConnection.Command = OpenRobotJogCommand;

        _jog.ReloadAsync().GetAwaiter().GetResult();
        RefreshJogCaptions();
        Apply(robot.Current, plc.Current, alarms.CountActiveAsync().GetAwaiter().GetResult());
        _robotSubscription = robot.Snapshots.Subscribe(new DelegateObserver<RobotSnapshot>(snapshot =>
            OnUi(() => Apply(snapshot, _plc.Current, _alarms.CountActiveAsync().GetAwaiter().GetResult()))));
        _plcSubscription = plc.Snapshots.Subscribe(new DelegateObserver<PlcSnapshot>(snapshot =>
            OnUi(() => Apply(_robot.Current, snapshot, _alarms.CountActiveAsync().GetAwaiter().GetResult()))));
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

    public IReadOnlyList<JogSlotItem> JogSlots { get; }

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenRobotJogCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenSavePositionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCurrentPositionCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToSavedPositionCommand))]
    [NotifyCanExecuteChangedFor(nameof(InitializeRobotCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableFreedriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginDeleteHoldCommand))]
    private bool _isRobotConnected;

    [ObservableProperty]
    private bool _isRobotJogOpen;

    [ObservableProperty]
    private bool _isSavePositionOpen;

    [ObservableProperty]
    private int _selectedJogSlot = 1;

    [ObservableProperty]
    private string _selectedJogTitle = "Position 1";

    [ObservableProperty]
    private string _storedCoordinatesText = "No coordinates saved.";

    [ObservableProperty]
    private string _jogStatusText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCurrentPositionCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToSavedPositionCommand))]
    [NotifyCanExecuteChangedFor(nameof(InitializeRobotCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableFreedriveCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginDeleteHoldCommand))]
    private bool _isJogBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginDeleteHoldCommand))]
    private bool _hasSelectedSavedPose;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginDeleteHoldCommand))]
    private bool _isDeleteHolding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FreedriveButtonText))]
    private bool _isFreedriveActive;

    [ObservableProperty]
    private double _deleteHoldProgress;

    public string FreedriveButtonText => IsFreedriveActive ? "Disable freedrive" : "Enable freedrive";

    public TimeSpan DeleteHoldDuration { get; set; } = TimeSpan.FromSeconds(4);

    public bool HasNoActiveAlarms => ActiveAlarmItems.Count == 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancelDeleteHold();
        _jog.StopFreedrive();
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

    [RelayCommand(CanExecute = nameof(IsRobotConnected))]
    private void OpenRobotJog()
    {
        if (!IsRobotConnected)
        {
            return;
        }

        _jog.ReloadAsync().GetAwaiter().GetResult();
        RefreshJogCaptions();
        JogStatusText = string.Empty;
        IsRobotJogOpen = true;
    }

    [RelayCommand]
    private void CloseRobotJog()
    {
        CancelDeleteHold();
        IsSavePositionOpen = false;
        IsFreedriveActive = false;
        IsRobotJogOpen = false;
        _ = _jog.EndFreedriveAsync(_robot.Current);
    }

    [RelayCommand(CanExecute = nameof(IsRobotConnected))]
    private void OpenSavePosition(int slot)
    {
        if (!IsRobotConnected || slot is < 1 or > SavedJogPose.SlotCount)
        {
            return;
        }

        CancelDeleteHold();
        SelectedJogSlot = slot;
        SelectedJogTitle = $"Save position — Position {slot}";
        StoredCoordinatesText = UrScriptCommands.FormatStored(_jog.Slots[slot - 1]);
        JogStatusText = string.Empty;
        IsSavePositionOpen = true;
    }

    [RelayCommand]
    private void CloseSavePosition()
    {
        CancelDeleteHold();
        IsSavePositionOpen = false;
    }

    [RelayCommand]
    private void CloseTopDialog()
    {
        if (IsSavePositionOpen)
        {
            CancelDeleteHold();
            IsSavePositionOpen = false;
            return;
        }

        if (IsRobotJogOpen)
        {
            CloseRobotJog();
            return;
        }

        if (IsActiveAlarmsDialogOpen)
        {
            IsActiveAlarmsDialogOpen = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunJogAction))]
    private async Task SaveCurrentPositionAsync()
    {
        var slot = SelectedJogSlot;
        var existing = _jog.Slots[slot - 1];
        if (existing.HasPose
            && !_prompt.Confirm("Save position", $"Replace saved Position {slot} with the current TCP and joints?"))
        {
            return;
        }

        await RunJogAsync(() => _jog.SaveCurrentAsync(slot, _robot.Current)).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() =>
        {
            StoredCoordinatesText = UrScriptCommands.FormatStored(_jog.Slots[slot - 1]);
            RefreshJogCaptions();
        }).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanRunJogAction))]
    private async Task GoToSavedPositionAsync()
    {
        var slot = SelectedJogSlot;
        if (!_jog.Slots[slot - 1].HasPose)
        {
            _prompt.Inform("Go to saved pose", $"Position {slot} has no saved coordinates.");
            return;
        }

        if (!_prompt.Confirm("Go to saved pose", $"Move the robot to Position {slot}?"))
        {
            return;
        }

        await RunJogAsync(() => _jog.GoToAsync(slot, _robot.Current)).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanRunJogAction))]
    private async Task InitializeRobotAsync()
    {
        if (!_prompt.Confirm(
                "Initialize robot",
                "Power on, wait until booting completes, release the brakes, and wait until the robot is active in normal mode?"))
        {
            return;
        }

        await RunJogAsync(() => _jog.InitializeAsync(
            _robot.Current,
            status => _dispatcher.Post(() => JogStatusText = status))).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanRunJogAction))]
    private async Task EnableFreedriveAsync()
    {
        if (IsFreedriveActive)
        {
            if (!_prompt.Confirm("Disable freedrive", "Exit freedrive_mode?"))
            {
                return;
            }

            await RunJogAsync(() => _jog.EndFreedriveAsync(_robot.Current)).ConfigureAwait(false);
        }
        else
        {
            if (!_prompt.Confirm(
                    "Enable freedrive",
                    "Enter freedrive_mode? The robot must be Remote and powered on with brakes released. It can then be moved by hand."))
            {
                return;
            }

            await RunJogAsync(() => _jog.EnableFreedriveAsync(_robot.Current)).ConfigureAwait(false);
        }

        await _dispatcher.InvokeAsync(() => IsFreedriveActive = _jog.IsFreedriveActive).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanBeginDeleteHold))]
    private async Task BeginDeleteHoldAsync()
    {
        if (!CanBeginDeleteHold())
        {
            return;
        }

        CancelDeleteHoldToken();
        var cts = new CancellationTokenSource();
        _deleteHoldCts = cts;
        await _dispatcher.InvokeAsync(() =>
        {
            IsDeleteHolding = true;
            DeleteHoldProgress = 0;
        }).ConfigureAwait(false);

        var started = Environment.TickCount64;
        var durationMs = Math.Max(1, DeleteHoldDuration.TotalMilliseconds);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var elapsed = Environment.TickCount64 - started;
                if (elapsed >= durationMs)
                {
                    break;
                }

                var progress = elapsed / durationMs;
                await _dispatcher.InvokeAsync(() => DeleteHoldProgress = progress).ConfigureAwait(false);
                var remaining = (int)Math.Min(50, durationMs - elapsed);
                await Task.Delay(Math.Max(1, remaining), cts.Token).ConfigureAwait(false);
            }

            cts.Token.ThrowIfCancellationRequested();
            var slot = SelectedJogSlot;
            await RunJogAsync(() => _jog.DeleteAsync(slot)).ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                StoredCoordinatesText = UrScriptCommands.FormatStored(_jog.Slots[slot - 1]);
                RefreshJogCaptions();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_deleteHoldCts, cts))
            {
                _deleteHoldCts = null;
            }

            cts.Dispose();
            await _dispatcher.InvokeAsync(() =>
            {
                IsDeleteHolding = false;
                DeleteHoldProgress = 0;
            }).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void CancelDeleteHold()
    {
        CancelDeleteHoldToken();
        IsDeleteHolding = false;
        DeleteHoldProgress = 0;
    }

    private bool CanRunJogAction() => IsRobotConnected && !IsJogBusy;

    private bool CanBeginDeleteHold() =>
        IsRobotConnected && !IsJogBusy && HasSelectedSavedPose && !IsDeleteHolding;

    private async Task RunJogAsync(Func<Task<JogResult>> action)
    {
        await _dispatcher.InvokeAsync(() => IsJogBusy = true).ConfigureAwait(false);
        try
        {
            var result = await action().ConfigureAwait(false);
            await _dispatcher.InvokeAsync(() =>
            {
                JogStatusText = result.Message;
                if (!result.Succeeded)
                {
                    _prompt.Inform("Robot JOG", result.Message);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                JogStatusText = ex.Message;
                _prompt.Inform("Robot JOG", ex.Message);
            }).ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsJogBusy = false).ConfigureAwait(false);
        }
    }

    private void Apply(RobotSnapshot robot, PlcSnapshot plc, int activeAlarms)
    {
        var connection = RobotDashboardProjection.ProjectConnection(robot.Dashboard, robot.Rtde);
        RobotConnection.Apply(connection.Text, connection.Kind, Freshness.Live);
        IsRobotConnected = RobotJogService.IsLinked(robot);
        if (!IsRobotConnected && (IsRobotJogOpen || IsSavePositionOpen))
        {
            CancelDeleteHold();
            _jog.StopFreedrive();
            IsFreedriveActive = false;
            IsSavePositionOpen = false;
            IsRobotJogOpen = false;
            JogStatusText = "Robot is disconnected.";
        }

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

    private void RefreshJogCaptions()
    {
        foreach (var item in JogSlots)
        {
            var pose = _jog.Slots[item.Slot - 1];
            item.Caption = pose.HasPose ? "saved" : "empty";
            item.IsSaved = pose.HasPose;
        }

        HasSelectedSavedPose = SelectedJogSlot is >= 1 and <= SavedJogPose.SlotCount
            && _jog.Slots[SelectedJogSlot - 1].HasPose;
    }

    partial void OnSelectedJogSlotChanged(int value) => RefreshJogCaptions();

    private void CancelDeleteHoldToken() => _deleteHoldCts?.Cancel();

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
