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
    }

    [Fact]
    public void Live_tags_map_status_result_and_process_values()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = ScrewdriverProjection.Project(LivePlc(now, new Dictionary<string, object?>
        {
            [ScrewdriverTags.Status] = "running",
            [ScrewdriverTags.Result] = "ok",
            [ScrewdriverTags.TorqueNm] = 12.5,
            [ScrewdriverTags.AngleDeg] = 90,
            [ScrewdriverTags.CycleCount] = 4,
            [ScrewdriverTags.ProgramId] = "P1",
            [ScrewdriverTags.FaultCode] = null,
            [ScrewdriverTags.Enabled] = true
        }));

        Assert.Equal("running", snapshot.Status.Text);
        Assert.Equal(IndicatorKind.Normal, snapshot.Status.Kind);
        Assert.Equal("ok", snapshot.Result.Text);
        Assert.Equal("12.5 Nm", snapshot.Torque.Text);
        Assert.Equal("90°", snapshot.Angle.Text);
        Assert.Equal("4", snapshot.CycleCount.Text);
        Assert.Equal("P1", snapshot.Program.Text);
        Assert.Equal("Enabled", snapshot.Enabled.Text);
        Assert.Equal(Freshness.Live, snapshot.Torque.Freshness);
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
