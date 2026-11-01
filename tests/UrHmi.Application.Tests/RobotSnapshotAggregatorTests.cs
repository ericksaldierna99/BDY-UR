using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Tests;

public sealed class RobotSnapshotAggregatorTests
{
    private static readonly TimeSpan DashboardStale = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RtdeStale = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void Disconnect_keeps_last_process_values_and_marks_them_stale()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.RobotMode, DefaultRtdeOutputs.ActualTcpPose, DefaultRtdeOutputs.RuntimeState]);
        aggregator.SetRtdeHealth(Connected(ChannelIds.RobotRtde, now));
        aggregator.ApplyRtdeFrame(Frame(now, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.RobotMode] = 7,
            [DefaultRtdeOutputs.RuntimeState] = 2u,
            [DefaultRtdeOutputs.ActualTcpPose] = new[] { 0.1, 0.2, 0.3, 0.0, 0.0, 0.0 }
        }));

        var live = aggregator.Build(now, DashboardStale, RtdeStale);
        Assert.Equal("RUNNING", live.RobotMode.Value);
        Assert.Equal("PLAYING", live.ProgramStatus.Value);
        Assert.Equal(Freshness.Live, live.RobotMode.Freshness);
        Assert.Equal(0.1, live.TcpPosition.Value?.X);

        aggregator.SetRtdeHealth(ChannelHealth.Disconnected(ChannelIds.RobotRtde, "socket closed"));
        var after = aggregator.Build(now.AddSeconds(1), DashboardStale, RtdeStale);

        Assert.Equal(LinkState.Disconnected, after.Rtde.Link);
        Assert.Equal("RUNNING", after.RobotMode.Value);
        Assert.Equal("PLAYING", after.ProgramStatus.Value);
        Assert.Equal(Freshness.Stale, after.RobotMode.Freshness);
        Assert.Equal(Freshness.Stale, after.ProgramStatus.Freshness);
        Assert.Equal(0.1, after.TcpPosition.Value?.X);
        Assert.NotEqual("STOPPED", after.RobotMode.Value, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual("stopped", after.ProgramStatus.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_socket_with_late_sample_is_stale_not_disconnected()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.ActualTcpSpeed]);
        aggregator.SetRtdeHealth(Connected(ChannelIds.RobotRtde, now));
        aggregator.ApplyRtdeFrame(Frame(now, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.ActualTcpSpeed] = new[] { 0.3, 0.4, 0.0, 0.0, 0.0, 0.0 }
        }));

        var stale = aggregator.Build(now.AddMilliseconds(250), DashboardStale, RtdeStale);

        Assert.Equal(LinkState.Connected, stale.Rtde.Link);
        Assert.Equal(Freshness.Stale, stale.TcpSpeed.Freshness);
        Assert.Equal(0.5, stale.TcpSpeed.Value);
    }

    [Fact]
    public void Rejected_pose_key_stays_unavailable_and_is_not_filled_with_zeros()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.UtcNow;
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.RobotMode]);
        aggregator.SetRtdeHealth(Connected(ChannelIds.RobotRtde, now));
        aggregator.ApplyRtdeFrame(Frame(now, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.RobotMode] = 5
        }));

        var snapshot = aggregator.Build(now, DashboardStale, RtdeStale);

        Assert.Equal("IDLE", snapshot.RobotMode.Value);
        Assert.Equal(Freshness.Unavailable, snapshot.TcpPosition.Freshness);
        Assert.Null(snapshot.TcpPosition.Value);
        Assert.Equal(Freshness.Unavailable, snapshot.TcpSpeed.Freshness);
    }

    [Fact]
    public void Protective_stop_is_safety_data_not_active_fault()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.UtcNow;
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.SafetyStatus, DefaultRtdeOutputs.SafetyStatusBits]);
        aggregator.SetRtdeHealth(Connected(ChannelIds.RobotRtde, now));
        aggregator.ApplyRtdeFrame(Frame(now, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.SafetyStatus] = 3,
            [DefaultRtdeOutputs.SafetyStatusBits] = 1u << 2
        }));

        var snapshot = aggregator.Build(now, DashboardStale, RtdeStale);

        Assert.Equal("PROTECTIVE_STOP", snapshot.SafetyStatus.Value);
        Assert.Null(snapshot.ActiveFault.Value);
        Assert.Equal(Freshness.Live, snapshot.ActiveFault.Freshness);
    }

    [Fact]
    public void Identified_fault_fills_active_fault()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.UtcNow;
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.SafetyStatus]);
        aggregator.SetRtdeHealth(Connected(ChannelIds.RobotRtde, now));
        aggregator.ApplyRtdeFrame(Frame(now, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.SafetyStatus] = 9
        }));

        var snapshot = aggregator.Build(now, DashboardStale, RtdeStale);

        Assert.Equal("FAULT", snapshot.SafetyStatus.Value);
        Assert.Equal("FAULT", snapshot.ActiveFault.Value);
    }

    [Fact]
    public void Dashboard_fills_mode_when_rtde_did_not_negotiate_it()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.UtcNow;
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.ActualTcpPose]);
        aggregator.ApplyDashboard("RUNNING", "NORMAL", "PLAYING", "Program running", now);

        var snapshot = aggregator.Build(now, DashboardStale, RtdeStale);

        Assert.Equal("RUNNING", snapshot.RobotMode.Value);
        Assert.Equal("NORMAL", snapshot.SafetyStatus.Value);
        Assert.Equal("PLAYING", snapshot.ProgramStatus.Value);
    }

    [Fact]
    public void Actual_q_fills_live_joint_positions()
    {
        var aggregator = new RobotSnapshotAggregator();
        var now = DateTimeOffset.UtcNow;
        aggregator.SetRtdeKeys([DefaultRtdeOutputs.ActualQ]);
        aggregator.SetRtdeHealth(Connected(ChannelIds.RobotRtde, now));
        aggregator.ApplyRtdeFrame(Frame(now, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.ActualQ] = new[] { 0.1, -0.2, 0.3, 0.4, 0.5, 0.6 }
        }));

        var snapshot = aggregator.Build(now, DashboardStale, RtdeStale);

        Assert.Equal(Freshness.Live, snapshot.JointPositions.Freshness);
        Assert.Equal(0.1, snapshot.JointPositions.Value?.J1);
        Assert.Equal(0.6, snapshot.JointPositions.Value?.J6);
    }

    private static ChannelHealth Connected(string channelId, DateTimeOffset now) =>
        new(channelId, LinkState.Connected, now, TimeSpan.Zero, null);

    private static RtdeFrame Frame(DateTimeOffset now, Dictionary<string, object?> values) =>
        new(now, values);
}
