using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Application.Authorization;
using UrHmi.Application.Jog;
using UrHmi.Domain;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Tests;

public sealed class RobotJogServiceTests
{
    [Fact]
    public async Task Save_refuses_when_disconnected()
    {
        var sut = CreateSut(out _, out var store, out _);
        var snapshot = Snapshot(LinkState.Disconnected, LinkState.Disconnected, tcp: LiveTcp(), joints: LiveJoints());

        var result = await sut.SaveCurrentAsync(1, snapshot);

        Assert.False(result.Succeeded);
        Assert.Contains("disconnected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(store.Poses[0].HasPose);
    }

    [Fact]
    public async Task Save_refuses_stale_pose()
    {
        var sut = CreateSut(out _, out var store, out _);
        var snapshot = Snapshot(
            LinkState.Connected,
            LinkState.Connected,
            new TcpPose(0.1, 0, 0, 0, 0, 0),
            Freshness.Stale,
            new JointPose(0.2, 0, 0, 0, 0, 0),
            Freshness.Stale);

        var result = await sut.SaveCurrentAsync(2, snapshot);

        Assert.False(result.Succeeded);
        Assert.Contains("No live TCP or joint", result.Message, StringComparison.Ordinal);
        Assert.False(store.Poses[1].HasPose);
    }

    [Fact]
    public async Task Save_writes_live_tcp_and_joints()
    {
        var sut = CreateSut(out _, out var store, out _);
        var tcp = new TcpPose(0.4, -0.1, 0.2, 0, 1, 0);
        var joints = new JointPose(0.1, 0.2, 0.3, 0.4, 0.5, 0.6);

        var result = await sut.SaveCurrentAsync(4, Snapshot(LinkState.Connected, LinkState.Disconnected, tcp, Freshness.Live, joints, Freshness.Live));

        Assert.True(result.Succeeded);
        Assert.Equal(tcp, store.Poses[3].Tcp);
        Assert.Equal(joints, store.Poses[3].Joints);
        Assert.Equal(10, store.Poses.Count);
        Assert.Equal(tcp, sut.Slots[3].Tcp);
    }

    [Fact]
    public async Task Save_rejects_slot_outside_1_to_10()
    {
        var sut = CreateSut(out _, out _, out _);

        var result = await sut.SaveCurrentAsync(11, Snapshot(LinkState.Connected, LinkState.Connected, tcp: LiveTcp(), joints: LiveJoints()));

        Assert.False(result.Succeeded);
        Assert.Contains("1 to 10", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Go_refuses_when_disconnected()
    {
        var sut = CreateSut(out var urScript, out var store, out _);
        store.Poses = Occupied(1, LiveTcp(), LiveJoints());
        await sut.ReloadAsync();

        var result = await sut.GoToAsync(1, Snapshot(LinkState.Disconnected, LinkState.Disconnected, tcp: LiveTcp(), joints: LiveJoints()));

        Assert.False(result.Succeeded);
        Assert.Contains("disconnected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(urScript.Sent);
    }

    [Fact]
    public async Task Go_refuses_empty_slot()
    {
        var sut = CreateSut(out var urScript, out _, out _);

        var result = await sut.GoToAsync(5, Snapshot(LinkState.Connected, LinkState.Connected, tcp: LiveTcp(), joints: LiveJoints()));

        Assert.False(result.Succeeded);
        Assert.Contains("no saved coordinates", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(urScript.Sent);
    }

    [Fact]
    public async Task Go_prefers_movej_when_joints_are_saved()
    {
        var sut = CreateSut(out var urScript, out var store, out _);
        store.Poses = Occupied(2, LiveTcp(), new JointPose(0.1, -0.2, 0.3, 0, 0, 0));
        await sut.ReloadAsync();

        var result = await sut.GoToAsync(2, Snapshot(LinkState.Disconnected, LinkState.Connected, tcp: LiveTcp(), joints: LiveJoints()));

        Assert.True(result.Succeeded);
        Assert.Single(urScript.Sent);
        Assert.StartsWith("movej(", urScript.Sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Go_uses_movel_when_only_tcp_is_saved()
    {
        var sut = CreateSut(out var urScript, out var store, out _);
        store.Poses = Occupied(3, new TcpPose(0.5, 0, 0.2, 0, 0, 0), null);
        await sut.ReloadAsync();

        var result = await sut.GoToAsync(3, Snapshot(LinkState.Connected, LinkState.Disconnected, tcp: LiveTcp(), joints: LiveJoints()));

        Assert.True(result.Succeeded);
        Assert.Single(urScript.Sent);
        Assert.StartsWith("movel(p[", urScript.Sent[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Brake_release_is_not_sent_when_disconnected()
    {
        var sut = CreateSut(out _, out _, out var dashboard);

        var result = await sut.ReleaseBrakesAsync(Snapshot(LinkState.Disconnected, LinkState.Disconnected));

        Assert.False(result.Succeeded);
        Assert.Equal(0, dashboard.BrakeCalls);
    }

    [Fact]
    public async Task Brake_release_uses_dashboard_when_linked()
    {
        var sut = CreateSut(out _, out _, out var dashboard);
        dashboard.BrakeResult = DashboardCommandResult.Ok("Brake releasing", "Brake releasing");

        var result = await sut.ReleaseBrakesAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.Equal(1, dashboard.BrakeCalls);
        Assert.Equal("Brake releasing", result.Message);
    }

    [Fact]
    public async Task Motion_and_brake_require_permission()
    {
        var sut = CreateSut(out var urScript, out var store, out var dashboard, permitted: false);
        store.Poses = Occupied(1, LiveTcp(), LiveJoints());
        await sut.ReloadAsync();
        var linked = Snapshot(LinkState.Connected, LinkState.Connected, tcp: LiveTcp(), joints: LiveJoints());

        var go = await sut.GoToAsync(1, linked);
        var brake = await sut.ReleaseBrakesAsync(linked);

        Assert.False(go.Succeeded);
        Assert.False(brake.Succeeded);
        Assert.Empty(urScript.Sent);
        Assert.Equal(0, dashboard.BrakeCalls);
    }

    private static RobotJogService CreateSut(
        out FakeUrScript urScript,
        out MemoryPoseStore store,
        out FakeDashboard dashboard,
        bool permitted = true)
    {
        urScript = new FakeUrScript();
        store = new MemoryPoseStore();
        dashboard = new FakeDashboard();
        return new RobotJogService(
            store,
            urScript,
            dashboard,
            new StubAuthorization(permitted),
            NullLogger<RobotJogService>.Instance);
    }

    private static TcpPose LiveTcp() => new(0.1, 0.2, 0.3, 0, 0, 0);

    private static JointPose LiveJoints() => new(0.1, 0.2, 0.3, 0.4, 0.5, 0.6);

    private static IReadOnlyList<SavedJogPose> Occupied(int slot, TcpPose? tcp, JointPose? joints)
    {
        var slots = SavedJogPose.EmptySlots().ToArray();
        slots[slot - 1] = new SavedJogPose(slot, tcp, joints, DateTimeOffset.UtcNow);
        return slots;
    }

    private static RobotSnapshot Snapshot(
        LinkState dashboard,
        LinkState rtde,
        TcpPose? tcp = null,
        Freshness tcpFreshness = Freshness.Live,
        JointPose? joints = null,
        Freshness jointFreshness = Freshness.Live)
    {
        var now = DateTimeOffset.UtcNow;
        return new RobotSnapshot(
            now,
            new ChannelHealth(ChannelIds.RobotDashboard, dashboard, now, TimeSpan.Zero, null),
            new ChannelHealth(ChannelIds.RobotRtde, rtde, now, TimeSpan.Zero, null),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            tcp is null ? TimedValue<TcpPose>.Unavailable() : new TimedValue<TcpPose>(tcp, now, tcpFreshness),
            joints is null ? TimedValue<JointPose>.Unavailable() : new TimedValue<JointPose>(joints, now, jointFreshness),
            TimedValue<double>.Unavailable(),
            now);
    }

    private sealed class MemoryPoseStore : IJogPoseStore
    {
        public IReadOnlyList<SavedJogPose> Poses { get; set; } = SavedJogPose.EmptySlots();

        public Task<IReadOnlyList<SavedJogPose>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Poses);

        public Task SaveAsync(IReadOnlyList<SavedJogPose> poses, CancellationToken cancellationToken = default)
        {
            Poses = poses;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUrScript : IUrScriptClient
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(string script, CancellationToken cancellationToken = default)
        {
            Sent.Add(script);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDashboard : IRobotDashboardClient
    {
        public int BrakeCalls { get; private set; }

        public DashboardCommandResult BrakeResult { get; set; } = DashboardCommandResult.Ok("Brake releasing", "Brake releasing");

        public ChannelHealth Health { get; } = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<DashboardCommandResult> GetRobotModeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> GetSafetyStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> GetProgramStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> GetRunningAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> GetPolyscopeVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> BrakeReleaseAsync(CancellationToken cancellationToken = default)
        {
            BrakeCalls++;
            return Task.FromResult(BrakeResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubAuthorization : IAuthorizationService
    {
        private readonly bool _permitted;

        public StubAuthorization(bool permitted) => _permitted = permitted;

        public bool HasProcessChangePermission() => _permitted;
    }
}
