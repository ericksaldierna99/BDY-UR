using UrHmi.Domain;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Robot;

namespace UrHmi.Domain.Tests;

public sealed class RobotSnapshotTests
{
    [Fact]
    public void Disconnecting_does_not_rewrite_robot_status_to_stopped()
    {
        var lastValid = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
        var snapshot = new RobotSnapshot(
            DateTimeOffset.UtcNow,
            ChannelHealth.Disconnected(ChannelIds.RobotDashboard),
            ChannelHealth.Disconnected(ChannelIds.RobotRtde),
            TimedValue<string>.Unavailable(),
            new TimedValue<string>("running", lastValid, Freshness.Stale),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<TcpPose>.Unavailable(),
            TimedValue<JointPose>.Unavailable(),
            TimedValue<double>.Unavailable(),
            lastValid);

        Assert.Equal(LinkState.Disconnected, snapshot.Dashboard.Link);
        Assert.Equal("running", snapshot.RobotStatus.Value);
        Assert.Equal(Freshness.Stale, snapshot.RobotStatus.Freshness);
        Assert.NotEqual("stopped", snapshot.RobotStatus.Value, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_snapshot_marks_process_values_unavailable()
    {
        var snapshot = RobotSnapshot.Empty(DateTimeOffset.UtcNow);

        Assert.Equal(LinkState.Disconnected, snapshot.Dashboard.Link);
        Assert.Equal(LinkState.Disconnected, snapshot.Rtde.Link);
        Assert.Equal(Freshness.Unavailable, snapshot.RobotStatus.Freshness);
        Assert.Null(snapshot.RobotStatus.Value);
        Assert.Null(snapshot.LastValidUpdateUtc);
    }

    [Fact]
    public void Timed_value_can_keep_last_value_when_stale()
    {
        var value = new TimedValue<double>(1.25, DateTimeOffset.UtcNow, Freshness.Stale);

        Assert.Equal(1.25, value.Value);
        Assert.Equal(Freshness.Stale, value.Freshness);
    }

    [Fact]
    public void Indicator_kind_has_five_distinct_conditions()
    {
        var kinds = Enum.GetValues<IndicatorKind>();

        Assert.Equal(5, kinds.Length);
        Assert.Contains(IndicatorKind.Normal, kinds);
        Assert.Contains(IndicatorKind.Warning, kinds);
        Assert.Contains(IndicatorKind.Fault, kinds);
        Assert.Contains(IndicatorKind.Safety, kinds);
        Assert.Contains(IndicatorKind.CommsLoss, kinds);
    }
}
