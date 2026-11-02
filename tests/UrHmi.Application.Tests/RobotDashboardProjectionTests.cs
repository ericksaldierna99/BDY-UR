using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Tests;

public sealed class RobotDashboardProjectionTests
{
    [Fact]
    public void Connection_is_degraded_when_only_one_channel_is_up()
    {
        var dashboard = new ChannelHealth(ChannelIds.RobotDashboard, LinkState.Connected, DateTimeOffset.UtcNow, TimeSpan.Zero, null);
        var rtde = ChannelHealth.Disconnected(ChannelIds.RobotRtde);

        var (text, kind) = RobotDashboardProjection.ProjectConnection(dashboard, rtde);

        Assert.Equal("Degraded", text);
        Assert.Equal(IndicatorKind.Warning, kind);
    }

    [Fact]
    public void Connection_loss_is_not_a_fault_lamp()
    {
        var disconnected = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);
        var (text, kind) = RobotDashboardProjection.ProjectConnection(disconnected, disconnected);

        Assert.Equal("Disconnected", text);
        Assert.Equal(IndicatorKind.CommsLoss, kind);
    }

    [Fact]
    public void Safety_kinds_stay_distinct()
    {
        Assert.Equal(IndicatorKind.Normal, RobotDashboardProjection.KindForSafety("NORMAL"));
        Assert.Equal(IndicatorKind.Safety, RobotDashboardProjection.KindForSafety("PROTECTIVE_STOP"));
        Assert.Equal(IndicatorKind.Fault, RobotDashboardProjection.KindForSafety("FAULT"));
        Assert.Equal(IndicatorKind.Safety, RobotDashboardProjection.KindForSafety("ROBOT_EMERGENCY_STOP"));
    }

    [Fact]
    public void Empty_live_fault_is_an_em_dash_not_ok()
    {
        var empty = new TimedValue<string>(null, DateTimeOffset.UtcNow, Freshness.Live);
        var unavailable = TimedValue<string>.Unavailable();

        Assert.Equal("—", RobotDashboardProjection.FormatTimed(empty.Value, empty.Freshness));
        Assert.Equal("—", RobotDashboardProjection.FormatTimed(unavailable.Value, unavailable.Freshness));
        Assert.Equal(IndicatorKind.Normal, RobotDashboardProjection.KindForActiveFault(empty.Value));
    }
}
