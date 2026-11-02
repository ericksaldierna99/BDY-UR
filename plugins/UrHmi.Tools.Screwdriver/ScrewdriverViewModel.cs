using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Plc;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Tools.Screwdriver;

public sealed partial class ScrewdriverViewModel : ObservableObject, IDisposable
{
    public const string FaultAlarmId = "tool.screwdriver.fault";

    private readonly IToolHost _host;
    private readonly SynchronizationContext? _ui;
    private readonly IDisposable _plcSubscription;
    private PlcSnapshot _plc = PlcSnapshot.Empty(DateTimeOffset.UtcNow);
    private SpinBridgeReading _spinBridge = SpinBridgeReading.Unavailable();
    private int _disposed;
    private bool _faultAlarmRaised;

    public ScrewdriverViewModel(IToolHost host, ScrewdriverConfiguration? configuration = null)
    {
        _host = host;
        _ui = SynchronizationContext.Current;
        Configuration = configuration ?? ScrewdriverConfiguration.CreateDefault();
        Apply(ScrewdriverProjection.Project(_plc, _spinBridge));
        _plcSubscription = host.Plc.Subscribe(new PlcObserver(this));
    }

    public ScrewdriverConfiguration Configuration { get; }

    public string Title => "Atornillado";

    public string ToolModel => Configuration.ToolModel;

    public string SpinBridgeIp => Configuration.SpinBridgeIp;

    public string OpcUaEndpoint => Configuration.OpcUaEndpoint;

    public string MapNotice =>
        $"Torque and angle come from SpinBridge OPC UA at {Configuration.OpcUaEndpoint} (SDV35), not from the UR or PLC tags. Remaining I/O waits on the Micro820 map. Core projects do not reference this assembly.";

    [ObservableProperty]
    private string _statusText = "—";

    [ObservableProperty]
    private string _statusCaption = "sin dato";

    [ObservableProperty]
    private IndicatorKind _statusKind = IndicatorKind.CommsLoss;

    [ObservableProperty]
    private string _resultText = "—";

    [ObservableProperty]
    private string _resultCaption = "sin dato";

    [ObservableProperty]
    private IndicatorKind _resultKind = IndicatorKind.Normal;

    [ObservableProperty]
    private string _torqueText = "—";

    [ObservableProperty]
    private string _torqueCaption = "sin dato";

    [ObservableProperty]
    private string _angleText = "—";

    [ObservableProperty]
    private string _angleCaption = "sin dato";

    [ObservableProperty]
    private string _cycleCountText = "—";

    [ObservableProperty]
    private string _cycleCountCaption = "sin dato";

    [ObservableProperty]
    private string _programText = "—";

    [ObservableProperty]
    private string _programCaption = "sin dato";

    [ObservableProperty]
    private string _faultText = "—";

    [ObservableProperty]
    private string _faultCaption = "sin dato";

    [ObservableProperty]
    private IndicatorKind _faultKind = IndicatorKind.Normal;

    [ObservableProperty]
    private string _enabledText = "—";

    [ObservableProperty]
    private string _enabledCaption = "sin dato";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetFaultCommand))]
    private bool _actuationAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetFaultCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _commandStatus = "PLC writes wait for a live channel and process permission.";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _plcSubscription.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanActuate))]
    private Task EnableAsync() => WriteAsync(ScrewdriverTags.CommandEnable, true);

    [RelayCommand(CanExecute = nameof(CanActuate))]
    private Task DisableAsync() => WriteAsync(ScrewdriverTags.CommandDisable, true);

    [RelayCommand(CanExecute = nameof(CanActuate))]
    private Task ResetFaultAsync() => WriteAsync(ScrewdriverTags.CommandReset, true);

    private bool CanActuate() => ActuationAvailable && !IsBusy;

    private async Task WriteAsync(string tag, object value)
    {
        IsBusy = true;
        try
        {
            var request = new PlcWriteRequest(
                Guid.NewGuid().ToString("N"),
                new Dictionary<string, object?>(StringComparer.Ordinal) { [tag] = value });
            var result = await _host.WritePlcAsync(request).ConfigureAwait(false);
            Post(() => CommandStatus = FormatWrite(tag, result));
        }
        catch (Exception ex)
        {
            Post(() => CommandStatus = ex.Message);
        }
        finally
        {
            Post(() => IsBusy = false);
        }
    }

    internal void ApplyPlc(PlcSnapshot snapshot)
    {
        Post(() =>
        {
            _plc = snapshot;
            var projected = ScrewdriverProjection.Project(_plc, _spinBridge);
            Apply(projected);
            SyncFaultAlarm(projected.Fault);
        });
    }

    internal void ApplySpinBridge(SpinBridgeReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Post(() =>
        {
            _spinBridge = reading;
            Apply(ScrewdriverProjection.Project(_plc, _spinBridge));
        });
    }

    private void Apply(ScrewdriverSnapshot snapshot)
    {
        StatusText = snapshot.Status.Text;
        StatusCaption = snapshot.Status.Caption;
        StatusKind = snapshot.Status.Kind;
        ResultText = snapshot.Result.Text;
        ResultCaption = snapshot.Result.Caption;
        ResultKind = snapshot.Result.Kind;
        TorqueText = snapshot.Torque.Text;
        TorqueCaption = snapshot.Torque.Caption;
        AngleText = snapshot.Angle.Text;
        AngleCaption = snapshot.Angle.Caption;
        CycleCountText = snapshot.CycleCount.Text;
        CycleCountCaption = snapshot.CycleCount.Caption;
        ProgramText = snapshot.Program.Text;
        ProgramCaption = snapshot.Program.Caption;
        FaultText = snapshot.Fault.Text;
        FaultCaption = snapshot.Fault.Caption;
        FaultKind = snapshot.Fault.Kind;
        EnabledText = snapshot.Enabled.Text;
        EnabledCaption = snapshot.Enabled.Caption;
        ActuationAvailable = snapshot.ActuationAvailable;
    }

    private void SyncFaultAlarm(ScrewdriverField fault)
    {
        if (fault.Freshness == Freshness.Unavailable)
        {
            return;
        }

        var hasFault = fault.Kind == IndicatorKind.Fault;
        if (hasFault && !_faultAlarmRaised)
        {
            _host.Alarms.Raise(new AlarmRecord(
                FaultAlarmId,
                DateTimeOffset.UtcNow,
                null,
                AlarmOrigin.Tooling,
                "atornillado",
                $"Screwdriver fault: {fault.Text}",
                IsActive: true,
                Duration: null));
            _faultAlarmRaised = true;
            return;
        }

        if (!hasFault && _faultAlarmRaised)
        {
            _host.Alarms.Clear(FaultAlarmId, DateTimeOffset.UtcNow);
            _faultAlarmRaised = false;
        }
    }

    private static string FormatWrite(string tag, WriteResult result) =>
        $"{tag}: {result.Outcome}" +
        (string.IsNullOrWhiteSpace(result.Message) ? string.Empty : $" — {result.Message}");

    private void Post(Action action)
    {
        if (_ui is null || ReferenceEquals(SynchronizationContext.Current, _ui))
        {
            action();
            return;
        }

        _ui.Post(_ => action(), null);
    }

    private sealed class PlcObserver : IObserver<PlcSnapshot>
    {
        private readonly ScrewdriverViewModel _owner;

        public PlcObserver(ScrewdriverViewModel owner) => _owner = owner;

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(PlcSnapshot value) => _owner.ApplyPlc(value);
    }
}
