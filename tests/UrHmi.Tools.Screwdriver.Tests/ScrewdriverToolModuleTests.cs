using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class ScrewdriverToolModuleTests
{
    [Fact]
    public void Module_identity_and_tags_stay_in_the_plugin()
    {
        var module = new ScrewdriverToolModule();

        Assert.Equal("screwdriver", module.Id);
        Assert.Equal("Atornillado", module.DisplayName);
        Assert.DoesNotContain("screwdriver.torque_nm", module.RequiredPlcTags);
        Assert.DoesNotContain("screwdriver.angle_deg", module.RequiredPlcTags);
        Assert.IsAssignableFrom<IToolModule>(module);
        Assert.False(typeof(IToolViewFactory).IsAssignableFrom(typeof(ScrewdriverToolModule)));
    }

    [Fact]
    public void CreateViewModel_returns_the_plugin_view_model()
    {
        var module = new ScrewdriverToolModule(_ => NullSpinBridgeClient.Instance);

        using var vm = Assert.IsType<ScrewdriverViewModel>(module.CreateViewModel(new FakeHost()));

        Assert.Equal("Atornillado", vm.Title);
        Assert.Equal(ScrewdriverConfiguration.DefaultSpinBridgeIp, vm.SpinBridgeIp);
        Assert.Equal("opc.tcp://10.42.0.1:4840", vm.OpcUaEndpoint);
        Assert.Equal("—", vm.StatusText);
        Assert.Equal("sin dato", vm.StatusCaption);
        Assert.Equal("—", vm.DepthText);
        Assert.Equal("sin dato", vm.DepthCaption);
        Assert.Contains("OPC UA", vm.MapNotice, StringComparison.Ordinal);
        Assert.Contains("insertion depth", vm.MapNotice, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class FakeHost : IToolHost, IAlarmSink
{
    public List<PlcWriteRequest> Writes { get; } = [];

    public List<AlarmRecord> Raised { get; } = [];

    public List<string> Cleared { get; } = [];

    public WriteResult NextResult { get; set; } =
        new(WriteOutcome.Rejected, "test", "PLC writes are not available in this phase.");

    public SnapshotFeed<PlcSnapshot> PlcFeed { get; } = new(PlcSnapshot.Empty(DateTimeOffset.UtcNow));

    public IObservable<PlcSnapshot> Plc => PlcFeed;

    public IObservable<RobotSnapshot> Robot { get; } = new SnapshotFeed<RobotSnapshot>(RobotSnapshot.Empty(DateTimeOffset.UtcNow));

    public IAlarmSink Alarms => this;

    public Task<WriteResult> WritePlcAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
    {
        Writes.Add(request);
        return Task.FromResult(NextResult with { CorrelationId = request.CorrelationId });
    }

    void IAlarmSink.Raise(AlarmRecord alarm) => Raised.Add(alarm);

    void IAlarmSink.Clear(string alarmId, DateTimeOffset clearedUtc) => Cleared.Add(alarmId);
}

internal sealed class FeedSpinBridgeClient : ISpinBridgeClient
{
    private readonly SnapshotFeed<SpinBridgeReading> _feed;

    public FeedSpinBridgeClient(SnapshotFeed<SpinBridgeReading> feed) => _feed = feed;

    public SpinBridgeReading Current => _feed.Current;

    public void Start()
    {
    }

    public void Dispose()
    {
    }

    public IDisposable Subscribe(IObserver<SpinBridgeReading> observer) => _feed.Subscribe(observer);
}

internal sealed class SnapshotFeed<T> : IObservable<T>
{
    private readonly List<IObserver<T>> _observers = [];

    public SnapshotFeed(T current) => Current = current;

    public T Current { get; private set; }

    public void Publish(T value)
    {
        Current = value;
        foreach (var observer in _observers.ToArray())
        {
            observer.OnNext(value);
        }
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        _observers.Add(observer);
        observer.OnNext(Current);
        return new Unsub(this, observer);
    }

    private sealed class Unsub : IDisposable
    {
        private readonly SnapshotFeed<T> _owner;
        private readonly IObserver<T> _observer;

        public Unsub(SnapshotFeed<T> owner, IObserver<T> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose() => _owner._observers.Remove(_observer);
    }
}
