using UrHmi.Application.Authorization;
using UrHmi.Application.Telemetry;
using UrHmi.Application.Tools;
using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Presentation.Shell;
using UrHmi.Presentation.Tools;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Presentation.Tests;

public sealed class ToolsViewModelTests
{
    [Fact]
    public void Empty_catalog_shows_plugin_shell_message()
    {
        using var sut = CreateViewModel(new ToolCatalog([], new ToolsConfiguration(null, [])));

        Assert.Empty(sut.Modules);
        Assert.Contains("plugins directory", sut.EmptyMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sut.ActiveToolViewModel);
    }

    [Fact]
    public void Selecting_an_enabled_module_creates_its_view_model()
    {
        var tool = new FakeTool("cell-tool", "Cell tool");
        var catalog = new ToolCatalog(
            [tool],
            new ToolsConfiguration("cell-tool", ["cell-tool"]));

        using var sut = CreateViewModel(catalog);

        Assert.Equal("cell-tool", sut.SelectedModule?.Id);
        Assert.Equal("created", sut.ActiveToolViewModel);
        Assert.Equal("Cell tool", sut.FrameName);
        Assert.Equal("Enabled", sut.EnablementText);
        Assert.Equal(1, tool.CreateCalls);
        Assert.Equal(string.Empty, sut.EmptyMessage);
    }

    [Fact]
    public void Plc_frame_updates_from_telemetry()
    {
        var plc = new PublishingPlcTelemetry();
        using var sut = CreateViewModel(
            new ToolCatalog([], new ToolsConfiguration(null, [])),
            plc);
        var now = DateTimeOffset.UtcNow;

        plc.Publish(new PlcSnapshot(
            now,
            new ChannelHealth(ChannelIds.Plc, LinkState.Connected, now, TimeSpan.Zero, null),
            TimedValue<IReadOnlyDictionary<string, PlcValue>>.Unavailable(),
            null));

        Assert.Equal(nameof(LinkState.Connected), sut.PlcLinkText);
    }

    private static ToolsViewModel CreateViewModel(IToolCatalog catalog, PublishingPlcTelemetry? plc = null)
    {
        plc ??= new PublishingPlcTelemetry();
        return new ToolsViewModel(
            catalog,
            new StubHost(),
            plc,
            new MemoryConfigStore(),
            new AllowAuthorization(),
            new ImmediateDispatcher());
    }

    private sealed class FakeTool : IToolModule
    {
        public FakeTool(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public IReadOnlyList<string> RequiredPlcTags => [];

        public int CreateCalls { get; private set; }

        public object CreateViewModel(IToolHost host)
        {
            CreateCalls++;
            return "created";
        }
    }

    private sealed class StubHost : IToolHost
    {
        public IObservable<PlcSnapshot> Plc { get; } = NeverPlc.Instance;

        public IObservable<RobotSnapshot> Robot { get; } = NeverRobot.Instance;

        public IAlarmSink Alarms { get; } = new NullAlarms();

        public Task<WriteResult> WritePlcAsync(
            PlcWriteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WriteResult(WriteOutcome.Rejected, request.CorrelationId, "test"));
    }

    private sealed class PublishingPlcTelemetry : IPlcTelemetry
    {
        private readonly List<IObserver<PlcSnapshot>> _observers = [];

        public PlcSnapshot Current { get; private set; } = PlcSnapshot.Empty(DateTimeOffset.UtcNow);

        public IObservable<PlcSnapshot> Snapshots => new Source(this);

        public void Publish(PlcSnapshot snapshot)
        {
            Current = snapshot;
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(snapshot);
            }
        }

        private sealed class Source : IObservable<PlcSnapshot>
        {
            private readonly PublishingPlcTelemetry _owner;

            public Source(PublishingPlcTelemetry owner) => _owner = owner;

            public IDisposable Subscribe(IObserver<PlcSnapshot> observer)
            {
                _owner._observers.Add(observer);
                observer.OnNext(_owner.Current);
                return new Unsub(_owner, observer);
            }
        }

        private sealed class Unsub : IDisposable
        {
            private readonly PublishingPlcTelemetry _owner;
            private readonly IObserver<PlcSnapshot> _observer;

            public Unsub(PublishingPlcTelemetry owner, IObserver<PlcSnapshot> observer)
            {
                _owner = owner;
                _observer = observer;
            }

            public void Dispose() => _owner._observers.Remove(_observer);
        }
    }

    private sealed class MemoryConfigStore : IConfigStore
    {
        private HmiConfiguration _current = HmiConfiguration.CreateDefault();

        public Task<HmiConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_current);

        public Task SaveAsync(HmiConfiguration configuration, CancellationToken cancellationToken = default)
        {
            _current = configuration;
            return Task.CompletedTask;
        }
    }

    private sealed class AllowAuthorization : IAuthorizationService
    {
        public bool HasProcessChangePermission() => true;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();

        public T Invoke<T>(Func<T> func) => func();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class NullAlarms : IAlarmSink
    {
        public void Raise(AlarmRecord alarm)
        {
        }

        public void Clear(string alarmId, DateTimeOffset clearedUtc)
        {
        }
    }

    private sealed class NeverPlc : IObservable<PlcSnapshot>
    {
        public static readonly NeverPlc Instance = new();

        public IDisposable Subscribe(IObserver<PlcSnapshot> observer) => Nop.Instance;
    }

    private sealed class NeverRobot : IObservable<RobotSnapshot>
    {
        public static readonly NeverRobot Instance = new();

        public IDisposable Subscribe(IObserver<RobotSnapshot> observer) => Nop.Instance;
    }

    private sealed class Nop : IDisposable
    {
        public static readonly Nop Instance = new();

        public void Dispose()
        {
        }
    }
}
