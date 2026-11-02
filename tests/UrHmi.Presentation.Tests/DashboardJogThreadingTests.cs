using CommunityToolkit.Mvvm.Input;
using UrHmi.Application.Jog;
using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Presentation.Dashboard;
using UrHmi.Presentation.Shell;

namespace UrHmi.Presentation.Tests;

public sealed class DashboardJogThreadingTests
{
    [Fact]
    public async Task Brake_release_marshals_busy_and_status_back_to_the_dispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var jog = new ThreadPoolJogService();
        using var sut = CreateViewModel(dispatcher, jog);

        Assert.True(sut.IsRobotConnected);
        await sut.ReleaseBrakesCommand.ExecuteAsync(null);

        Assert.False(sut.IsJogBusy);
        Assert.Equal("Brake releasing.", sut.JogStatusText);
        Assert.True(dispatcher.InvokeAsyncCalls >= 3);
        Assert.Equal(1, jog.BrakeCalls);
    }

    [Fact]
    public async Task Save_position_marshals_stored_coordinates_after_background_io()
    {
        var dispatcher = new RecordingUiDispatcher();
        var jog = new ThreadPoolJogService();
        using var sut = CreateViewModel(dispatcher, jog);
        sut.OpenSavePositionCommand.Execute(1);

        await sut.SaveCurrentPositionCommand.ExecuteAsync(null);

        Assert.False(sut.IsJogBusy);
        Assert.Equal("Position 1 saved.", sut.JogStatusText);
        Assert.Contains("TCP", sut.StoredCoordinatesText, StringComparison.Ordinal);
        Assert.True(dispatcher.InvokeAsyncCalls >= 4);
    }

    private static DashboardViewModel CreateViewModel(RecordingUiDispatcher dispatcher, ThreadPoolJogService jog)
    {
        var now = DateTimeOffset.UtcNow;
        var robot = new StubRobotTelemetry(Linked(now));
        return new DashboardViewModel(
            robot,
            new StubPlcTelemetry(),
            new StubAlarms(),
            jog,
            new AcceptingPrompt(),
            dispatcher);
    }

    private static RobotSnapshot Linked(DateTimeOffset now) =>
        new(
            now,
            new ChannelHealth(ChannelIds.RobotDashboard, LinkState.Connected, now, TimeSpan.Zero, null),
            ChannelHealth.Disconnected(ChannelIds.RobotRtde),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<TcpPose>.Live(new TcpPose(0.1, 0.2, 0.3, 0, 0, 0), now),
            TimedValue<JointPose>.Live(new JointPose(0.1, 0.2, 0.3, 0.4, 0.5, 0.6), now),
            TimedValue<double>.Unavailable(),
            now);

    private sealed class RecordingUiDispatcher : IUiDispatcher
    {
        public int InvokeAsyncCalls { get; private set; }

        public bool CheckAccess() => false;

        public void Post(Action action) => action();

        public void Invoke(Action action) => action();

        public T Invoke<T>(Func<T> func) => func();

        public Task InvokeAsync(Action action)
        {
            InvokeAsyncCalls++;
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class ThreadPoolJogService : IRobotJogService
    {
        private IReadOnlyList<SavedJogPose> _slots = SavedJogPose.EmptySlots();

        public int BrakeCalls { get; private set; }

        public IReadOnlyList<SavedJogPose> Slots => _slots;

        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<JogResult> SaveCurrentAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                var copy = _slots.ToArray();
                copy[slot - 1] = new SavedJogPose(slot, snapshot.TcpPosition.Value, snapshot.JointPositions.Value, DateTimeOffset.UtcNow);
                _slots = copy;
                return JogResult.Ok($"Position {slot} saved.");
            }, cancellationToken);

        public Task<JogResult> GoToAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.Run(() => JogResult.Ok($"Move command sent to Position {slot}."), cancellationToken);

        public Task<JogResult> ReleaseBrakesAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            BrakeCalls++;
            return Task.Run(() => JogResult.Ok("Brake releasing."), cancellationToken);
        }
    }

    private sealed class AcceptingPrompt : IUserPrompt
    {
        public bool Confirm(string title, string message) => true;

        public void Inform(string title, string message)
        {
        }
    }

    private sealed class StubRobotTelemetry : IRobotTelemetry
    {
        public StubRobotTelemetry(RobotSnapshot current) => Current = current;

        public RobotSnapshot Current { get; }

        public IObservable<RobotSnapshot> Snapshots { get; } = Never.Instance;

        private sealed class Never : IObservable<RobotSnapshot>
        {
            public static readonly Never Instance = new();

            public IDisposable Subscribe(IObserver<RobotSnapshot> observer) => Empty.Instance;
        }
    }

    private sealed class StubPlcTelemetry : IPlcTelemetry
    {
        public PlcSnapshot Current { get; } = PlcSnapshot.Empty(DateTimeOffset.UtcNow);

        public IObservable<PlcSnapshot> Snapshots { get; } = Never.Instance;

        private sealed class Never : IObservable<PlcSnapshot>
        {
            public static readonly Never Instance = new();

            public IDisposable Subscribe(IObserver<PlcSnapshot> observer) => Empty.Instance;
        }
    }

    private sealed class StubAlarms : IAlarmStore
    {
        public Task RaiseAsync(AlarmRecord alarm, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearAsync(string alarmId, DateTimeOffset clearedUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlarmRecord>>([]);

        public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class Empty : IDisposable
    {
        public static readonly Empty Instance = new();

        public void Dispose()
        {
        }
    }
}
