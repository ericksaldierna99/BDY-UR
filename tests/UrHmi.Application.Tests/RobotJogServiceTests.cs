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
    public async Task Initialize_is_not_sent_when_disconnected()
    {
        var sut = CreateSut(out _, out _, out var dashboard);

        var result = await sut.InitializeAsync(Snapshot(LinkState.Disconnected, LinkState.Disconnected));

        Assert.False(result.Succeeded);
        Assert.Empty(dashboard.Commands);
    }

    [Fact]
    public async Task Initialize_powers_on_waits_for_idle_then_releases_brakes()
    {
        var sut = CreateSut(out _, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Safety = "NORMAL";
        dashboard.Modes.Enqueue("POWER_OFF");
        dashboard.Modes.Enqueue("BOOTING");
        dashboard.Modes.Enqueue("POWER_ON");
        dashboard.Modes.Enqueue("IDLE");
        dashboard.Modes.Enqueue("IDLE");
        dashboard.Modes.Enqueue("RUNNING");

        var result = await sut.InitializeAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.Contains("power on", dashboard.Commands);
        Assert.Contains("brake release", dashboard.Commands);
        var powerAt = dashboard.Commands.IndexOf("power on");
        var brakesAt = dashboard.Commands.IndexOf("brake release");
        Assert.True(powerAt >= 0 && brakesAt > powerAt);
        Assert.DoesNotContain("brake release", dashboard.Commands.Take(brakesAt), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Initialize_releases_brakes_when_already_idle()
    {
        var sut = CreateSut(out _, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Safety = "NORMAL";
        dashboard.Modes.Enqueue("IDLE");
        dashboard.Modes.Enqueue("IDLE");
        dashboard.Modes.Enqueue("IDLE");
        dashboard.Modes.Enqueue("RUNNING");

        var result = await sut.InitializeAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("power on", dashboard.Commands);
        Assert.Contains("brake release", dashboard.Commands);
    }

    [Fact]
    public async Task Initialize_does_not_send_power_or_brakes_when_already_running_and_normal()
    {
        var sut = CreateSut(out _, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Safety = "NORMAL";
        dashboard.Modes.Enqueue("RUNNING");

        var result = await sut.InitializeAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("power on", dashboard.Commands);
        Assert.DoesNotContain("brake release", dashboard.Commands);
    }

    [Fact]
    public async Task Initialize_refuses_local_control()
    {
        var sut = CreateSut(out _, out _, out var dashboard);
        dashboard.Remote = "false";

        var result = await sut.InitializeAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.False(result.Succeeded);
        Assert.Contains("Remote", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dashboard.Commands);
    }

    [Fact]
    public async Task Motion_init_and_freedrive_require_permission()
    {
        var sut = CreateSut(out var urScript, out var store, out var dashboard, permitted: false);
        store.Poses = Occupied(1, LiveTcp(), LiveJoints());
        await sut.ReloadAsync();
        var linked = Snapshot(LinkState.Connected, LinkState.Connected, tcp: LiveTcp(), joints: LiveJoints());

        var go = await sut.GoToAsync(1, linked);
        var init = await sut.InitializeAsync(linked);
        var freedrive = await sut.EnableFreedriveAsync(linked);

        var end = await sut.EndFreedriveAsync(linked);

        Assert.False(go.Succeeded);
        Assert.False(init.Succeeded);
        Assert.False(freedrive.Succeeded);
        Assert.True(end.Succeeded);
        Assert.Contains("end_freedrive_mode()", urScript.Sent[0], StringComparison.Ordinal);
        Assert.Equal(1, urScript.CloseCalls);
        Assert.Empty(dashboard.Commands);
    }

    [Fact]
    public async Task Freedrive_is_not_sent_when_disconnected()
    {
        var sut = CreateSut(out var urScript, out _, out _);

        var result = await sut.EnableFreedriveAsync(Snapshot(LinkState.Disconnected, LinkState.Disconnected));

        Assert.False(result.Succeeded);
        Assert.Contains("disconnected", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(urScript.Sent);
    }

    [Fact]
    public async Task Freedrive_sends_looping_program_when_running_and_remote()
    {
        var sut = CreateSut(out var urScript, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Modes.Enqueue("RUNNING");

        var result = await sut.EnableFreedriveAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.True(sut.IsFreedriveActive);
        var script = Assert.Single(urScript.Programs);
        Assert.Contains("freedrive_mode()", script, StringComparison.Ordinal);
        Assert.Contains("while True:", script, StringComparison.Ordinal);
        Assert.Contains("start_freedrive()", script, StringComparison.Ordinal);
        Assert.Empty(urScript.Sent);
    }

    [Fact]
    public async Task Freedrive_refuses_when_not_powered_on()
    {
        var sut = CreateSut(out var urScript, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Modes.Enqueue("IDLE");

        var result = await sut.EnableFreedriveAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.False(result.Succeeded);
        Assert.Contains("Initialize robot", result.Message, StringComparison.Ordinal);
        Assert.Empty(urScript.Programs);
    }

    [Fact]
    public async Task End_freedrive_sends_end_mode_then_closes_the_program_socket()
    {
        var sut = CreateSut(out var urScript, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Modes.Enqueue("RUNNING");
        await sut.EnableFreedriveAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        var result = await sut.EndFreedriveAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.False(sut.IsFreedriveActive);
        Assert.Equal("Freedrive ended.", result.Message);
        Assert.Contains("end_freedrive_mode()", Assert.Single(urScript.Sent), StringComparison.Ordinal);
        Assert.Equal(1, urScript.CloseCalls);
        Assert.Single(urScript.Programs);
    }

    [Fact]
    public async Task End_freedrive_closes_the_socket_immediately_without_waiting_for_the_send()
    {
        var sut = CreateSut(out var urScript, out _, out var dashboard);
        dashboard.Remote = "true";
        dashboard.Modes.Enqueue("RUNNING");
        urScript.SendDelay = TimeSpan.FromSeconds(2);
        await sut.EnableFreedriveAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await sut.EndFreedriveAsync(Snapshot(LinkState.Connected, LinkState.Disconnected));
        started.Stop();

        Assert.True(result.Succeeded);
        Assert.False(sut.IsFreedriveActive);
        Assert.Equal(1, urScript.CloseCalls);
        Assert.Empty(urScript.Sent);
        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(400), $"EndFreedrive blocked for {started.Elapsed}.");
    }

    [Fact]
    public async Task End_freedrive_closes_the_socket_when_disconnected_without_sending()
    {
        var sut = CreateSut(out var urScript, out _, out _);

        var result = await sut.EndFreedriveAsync(Snapshot(LinkState.Disconnected, LinkState.Disconnected));

        Assert.True(result.Succeeded);
        Assert.False(sut.IsFreedriveActive);
        Assert.Empty(urScript.Sent);
        Assert.Equal(1, urScript.CloseCalls);
    }

    [Fact]
    public async Task Delete_clears_a_saved_slot()
    {
        var sut = CreateSut(out _, out var store, out _);
        store.Poses = Occupied(4, LiveTcp(), LiveJoints());
        await sut.ReloadAsync();

        var result = await sut.DeleteAsync(4);

        Assert.True(result.Succeeded);
        Assert.False(store.Poses[3].HasPose);
        Assert.False(sut.Slots[3].HasPose);
        Assert.Equal(10, store.Poses.Count);
    }

    [Fact]
    public async Task Delete_refuses_empty_slot()
    {
        var sut = CreateSut(out _, out var store, out _);

        var result = await sut.DeleteAsync(2);

        Assert.False(result.Succeeded);
        Assert.Contains("no saved coordinates", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(store.Poses, pose => Assert.False(pose.HasPose));
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
        var sut = new RobotJogService(
            store,
            urScript,
            dashboard,
            new StubAuthorization(permitted),
            NullLogger<RobotJogService>.Instance);
        sut.ModePollInterval = TimeSpan.FromMilliseconds(1);
        sut.IdleTimeout = TimeSpan.FromSeconds(2);
        sut.RunningTimeout = TimeSpan.FromSeconds(2);
        sut.SafetyTimeout = TimeSpan.FromSeconds(2);
        return sut;
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

        public List<string> Programs { get; } = [];

        public int CloseCalls { get; private set; }

        public TimeSpan SendDelay { get; set; }

        public Task SendAsync(string script, CancellationToken cancellationToken = default)
        {
            if (SendDelay <= TimeSpan.Zero)
            {
                Sent.Add(script);
                return Task.CompletedTask;
            }

            return SendDelayedAsync(script, cancellationToken);
        }

        private async Task SendDelayedAsync(string script, CancellationToken cancellationToken)
        {
            await Task.Delay(SendDelay, cancellationToken).ConfigureAwait(false);
            Sent.Add(script);
        }

        public Task SendProgramAsync(string script, CancellationToken cancellationToken = default)
        {
            Programs.Add(script);
            return Task.CompletedTask;
        }

        public void CloseProgram() => CloseCalls++;
    }

    private sealed class FakeDashboard : IRobotDashboardClient
    {
        public Queue<string> Modes { get; } = new();

        public List<string> Commands { get; } = [];

        public string Remote { get; set; } = "true";

        public string Safety { get; set; } = "NORMAL";

        public string LastMode { get; private set; } = "POWER_OFF";

        public ChannelHealth Health { get; } = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<DashboardCommandResult> GetRobotModeAsync(CancellationToken cancellationToken = default)
        {
            if (Modes.Count > 0)
            {
                LastMode = Modes.Dequeue();
            }

            return Task.FromResult(DashboardCommandResult.Ok(LastMode, "Robotmode: " + LastMode));
        }

        public Task<DashboardCommandResult> GetSafetyStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Ok(Safety, "Safetystatus: " + Safety));

        public Task<DashboardCommandResult> GetProgramStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> GetRunningAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> GetPolyscopeVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("unused"));

        public Task<DashboardCommandResult> PowerOnAsync(CancellationToken cancellationToken = default)
        {
            Commands.Add("power on");
            return Task.FromResult(DashboardCommandResult.Ok("Powering on", "Powering on"));
        }

        public Task<DashboardCommandResult> BrakeReleaseAsync(CancellationToken cancellationToken = default)
        {
            Commands.Add("brake release");
            return Task.FromResult(DashboardCommandResult.Ok("Brake releasing", "Brake releasing"));
        }

        public Task<DashboardCommandResult> UnlockProtectiveStopAsync(CancellationToken cancellationToken = default)
        {
            Commands.Add("unlock protective stop");
            Safety = "NORMAL";
            return Task.FromResult(DashboardCommandResult.Ok("Protective stop releasing", "Protective stop releasing"));
        }

        public Task<DashboardCommandResult> IsInRemoteControlAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Ok(Remote, Remote));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubAuthorization : IAuthorizationService
    {
        private readonly bool _permitted;

        public StubAuthorization(bool permitted) => _permitted = permitted;

        public bool HasProcessChangePermission() => _permitted;
    }
}
