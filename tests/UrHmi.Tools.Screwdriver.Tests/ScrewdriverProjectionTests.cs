using UrHmi.Domain;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Plc;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class ScrewdriverProjectionTests
{
    [Fact]
    public void Missing_tags_are_unavailable_not_a_tool_fault()
    {
        var snapshot = ScrewdriverProjection.Project(PlcSnapshot.Empty(DateTimeOffset.UtcNow));

        Assert.Equal(Freshness.Unavailable, snapshot.Status.Freshness);
        Assert.Equal("—", snapshot.Status.Text);
        Assert.Equal("sin dato", snapshot.Status.Caption);
        Assert.Equal(IndicatorKind.CommsLoss, snapshot.Status.Kind);
        Assert.False(snapshot.ActuationAvailable);
        Assert.NotEqual(IndicatorKind.Fault, snapshot.Result.Kind);
        Assert.NotEqual(IndicatorKind.Safety, snapshot.Status.Kind);
        Assert.Equal(Freshness.Unavailable, snapshot.Torque.Freshness);
        Assert.Equal(IndicatorKind.CommsLoss, snapshot.Torque.Kind);
    }

    [Fact]
    public void Live_tags_map_status_result_and_process_values()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = ScrewdriverProjection.Project(LivePlc(now, new Dictionary<string, object?>
        {
            [ScrewdriverTags.Status] = "running",
            [ScrewdriverTags.Result] = "ok",
            [ScrewdriverTags.CycleCount] = 4,
            [ScrewdriverTags.ProgramId] = "P1",
            [ScrewdriverTags.FaultCode] = null,
            [ScrewdriverTags.Enabled] = true
        }));

        Assert.Equal("running", snapshot.Status.Text);
        Assert.Equal(IndicatorKind.Normal, snapshot.Status.Kind);
        Assert.Equal("ok", snapshot.Result.Text);
        Assert.Equal("—", snapshot.Torque.Text);
        Assert.Equal(Freshness.Unavailable, snapshot.Torque.Freshness);
        Assert.Equal("4", snapshot.CycleCount.Text);
        Assert.Equal("P1", snapshot.Program.Text);
        Assert.Equal("Enabled", snapshot.Enabled.Text);
        Assert.True(snapshot.ActuationAvailable);
    }

    [Fact]
    public void Nok_result_is_fault_not_safety()
    {
        var snapshot = ScrewdriverProjection.Project(LivePlc(DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            [ScrewdriverTags.Status] = "idle",
            [ScrewdriverTags.Result] = "nok"
        }));

        Assert.Equal(IndicatorKind.Fault, snapshot.Result.Kind);
        Assert.Equal(IndicatorKind.Normal, snapshot.Status.Kind);
        Assert.NotEqual(IndicatorKind.Safety, snapshot.Result.Kind);
    }

    [Fact]
    public void Stale_map_does_not_present_values_as_live()
    {
        var now = DateTimeOffset.UtcNow;
        var tags = new Dictionary<string, PlcValue>
        {
            [ScrewdriverTags.Status] = new(ScrewdriverTags.Status, "ready", now, Freshness.Live)
        };
        var plc = new PlcSnapshot(
            now,
            new ChannelHealth(ChannelIds.Plc, LinkState.Connected, now, TimeSpan.Zero, null),
            new TimedValue<IReadOnlyDictionary<string, PlcValue>>(tags, now, Freshness.Stale),
            null);

        var snapshot = ScrewdriverProjection.Project(plc);

        Assert.Equal(Freshness.Stale, snapshot.Status.Freshness);
        Assert.Equal("dato no actualizado", snapshot.Status.Caption);
        Assert.Equal("ready", snapshot.Status.Text);
    }

    [Fact]
    public void Torque_angle_and_depth_come_from_spinbridge_opcua_not_plc_tags()
    {
        var now = DateTimeOffset.UtcNow;
        var plc = LivePlc(now, new Dictionary<string, object?>
        {
            ["screwdriver.torque_nm"] = 99,
            ["screwdriver.angle_deg"] = 180,
            [ScrewdriverTags.Status] = "idle"
        });
        var spin = new SpinBridgeReading(
            new ChannelHealth(SpinBridgeReading.ChannelId, LinkState.Connected, now, TimeSpan.Zero, null),
            TimedValue<double>.Live(1.25, now),
            TimedValue<double>.Live(45, now),
            TimedValue<double>.Live(4.2, now));

        var snapshot = ScrewdriverProjection.Project(plc, spin);

        Assert.Equal("1.25 Nm", snapshot.Torque.Text);
        Assert.Equal("45°", snapshot.Angle.Text);
        Assert.Equal("4.2 mm", snapshot.Depth.Text);
        Assert.Equal(Freshness.Live, snapshot.Torque.Freshness);
        Assert.Equal(Freshness.Live, snapshot.Depth.Freshness);
        Assert.Equal(IndicatorKind.Normal, snapshot.Torque.Kind);
        Assert.DoesNotContain("99", snapshot.Torque.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Connected_empty_latest_result_is_unavailable_not_comms_loss()
    {
        var now = DateTimeOffset.UtcNow;
        var spin = new SpinBridgeReading(
            new ChannelHealth(SpinBridgeReading.ChannelId, LinkState.Connected, now, TimeSpan.Zero, null),
            TimedValue<double>.Unavailable(),
            TimedValue<double>.Unavailable(),
            TimedValue<double>.Unavailable());

        var snapshot = ScrewdriverProjection.Project(PlcSnapshot.Empty(now), spin);

        Assert.Equal(LinkState.Connected, spin.Channel.Link);
        Assert.Equal(Freshness.Unavailable, snapshot.Torque.Freshness);
        Assert.Equal(Freshness.Unavailable, snapshot.Angle.Freshness);
        Assert.Equal(Freshness.Unavailable, snapshot.Depth.Freshness);
        Assert.Equal("—", snapshot.Torque.Text);
        Assert.Equal("—", snapshot.Depth.Text);
        Assert.Equal(IndicatorKind.Normal, snapshot.Torque.Kind);
        Assert.Equal(IndicatorKind.Normal, snapshot.Angle.Kind);
        Assert.NotEqual(IndicatorKind.CommsLoss, snapshot.Torque.Kind);
        Assert.NotEqual(IndicatorKind.Fault, snapshot.Torque.Kind);
    }

    [Fact]
    public void Disconnect_keeps_last_torque_as_stale_not_a_fault()
    {
        var now = DateTimeOffset.UtcNow;
        var spin = new SpinBridgeReading(
            ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, "socket closed"),
            new TimedValue<double>(1.5, now, Freshness.Stale),
            new TimedValue<double>(20, now, Freshness.Stale),
            new TimedValue<double>(0, now, Freshness.Stale));

        var snapshot = ScrewdriverProjection.Project(PlcSnapshot.Empty(now), spin);

        Assert.Equal("1.5 Nm", snapshot.Torque.Text);
        Assert.Equal("20°", snapshot.Angle.Text);
        Assert.Equal("0 mm", snapshot.Depth.Text);
        Assert.Equal(Freshness.Stale, snapshot.Torque.Freshness);
        Assert.Equal(Freshness.Stale, snapshot.Depth.Freshness);
        Assert.Equal("dato no actualizado", snapshot.Torque.Caption);
        Assert.Equal(IndicatorKind.Normal, snapshot.Torque.Kind);
        Assert.NotEqual(IndicatorKind.Fault, snapshot.Torque.Kind);
        Assert.NotEqual(IndicatorKind.Safety, snapshot.Torque.Kind);
    }

    private static PlcSnapshot LivePlc(DateTimeOffset now, IReadOnlyDictionary<string, object?> values)
    {
        var tags = values.ToDictionary(
            pair => pair.Key,
            pair => new PlcValue(pair.Key, pair.Value, now, Freshness.Live),
            StringComparer.Ordinal);
        return new PlcSnapshot(
            now,
            new ChannelHealth(ChannelIds.Plc, LinkState.Connected, now, TimeSpan.Zero, null),
            TimedValue<IReadOnlyDictionary<string, PlcValue>>.Live(tags, now),
            null);
    }
}
