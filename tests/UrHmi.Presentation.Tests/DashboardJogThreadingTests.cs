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
    public async Task Initialize_robot_marshals_busy_and_status_back_to_the_dispatcher()
    {
        var dispatcher = new RecordingUiDispatcher();
        var jog = new ThreadPoolJogService();
        using var sut = CreateViewModel(dispatcher, jog);

        Assert.True(sut.IsRobotConnected);
        await sut.InitializeRobotCommand.ExecuteAsync(null);

        Assert.False(sut.IsJogBusy);
        Assert.Equal("Robot initialized.", sut.JogStatusText);
        Assert.True(dispatcher.InvokeAsyncCalls >= 3);
        Assert.Equal(1, jog.InitCalls);
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
        Assert.True(sut.JogSlots[0].IsSaved);
    }

    [Fact]
    public async Task Delete_hold_cancelled_before_duration_does_not_delete()
    {
        var dispatcher = new RecordingUiDispatcher { CheckAccessResult = true };
        var jog = new ThreadPoolJogService();
        using var sut = CreateViewModel(dispatcher, jog);
        sut.DeleteHoldDuration = TimeSpan.FromSeconds(30);
        await sut.SaveCurrentPositionCommand.ExecuteAsync(null);
        sut.OpenSavePositionCommand.Execute(1);

        var hold = sut.BeginDeleteHoldCommand.ExecuteAsync(null);
        await Task.Delay(40);
        sut.CancelDeleteHoldCommand.Execute(null);
        await hold;

        Assert.True(jog.Slots[0].HasPose);
        Assert.False(sut.IsDeleteHolding);
        Assert.Equal(0, sut.DeleteHoldProgress);
    }

    [Fact]
    public async Task Delete_hold_for_full_duration_clears_the_saved_pose()
    {
        var dispatcher = new RecordingUiDispatcher { CheckAccessResult = true };
        var jog = new ThreadPoolJogService();
        using var sut = CreateViewModel(dispatcher, jog);
        sut.DeleteHoldDuration = TimeSpan.FromMilliseconds(50);
        await sut.SaveCurrentPositionCommand.ExecuteAsync(null);
        sut.OpenSavePositionCommand.Execute(1);

        await sut.BeginDeleteHoldCommand.ExecuteAsync(null);

        Assert.False(jog.Slots[0].HasPose);
        Assert.False(sut.JogSlots[0].IsSaved);
        Assert.Equal("No coordinates saved.", sut.StoredCoordinatesText);
        Assert.Contains("deleted", sut.JogStatusText, StringComparison.OrdinalIgnoreCase);
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

        public bool CheckAccessResult { get; set; }

        public bool CheckAccess() => CheckAccessResult;

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

        public int InitCalls { get; private set; }

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

        public Task<JogResult> InitializeAsync(
            RobotSnapshot snapshot,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InitCalls++;
            progress?.Invoke("Power on…");
            return Task.Run(() => JogResult.Ok("Robot initialized."), cancellationToken);
        }

        public Task<JogResult> EnableFreedriveAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.Run(() => JogResult.Ok("Freedrive enabled."), cancellationToken);

        public Task<JogResult> DeleteAsync(int slot, CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                var copy = _slots.ToArray();
                copy[slot - 1] = new SavedJogPose(slot, null, null, null);
                _slots = copy;
                return JogResult.Ok($"Position {slot} deleted.");
            }, cancellationToken);
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
