using UrHmi.Domain;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class SpinBridgeReadingAggregatorTests
{
    private static readonly TimeSpan Stale = TimeSpan.FromMilliseconds(2000);

    [Fact]
    public void Successful_read_is_live_even_if_the_tightening_is_old()
    {
        var aggregator = new SpinBridgeReadingAggregator();
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        aggregator.ApplyPoll(now, 2.4, 30);

        var reading = aggregator.Build(now.AddMilliseconds(500), Stale);

        Assert.Equal(LinkState.Connected, reading.Channel.Link);
        Assert.Equal(Freshness.Live, reading.TorqueNm.Freshness);
        Assert.Equal(Freshness.Live, reading.AngleDeg.Freshness);
        Assert.Equal(2.4, reading.TorqueNm.Value);
        Assert.Equal(30, reading.AngleDeg.Value);
    }

    [Fact]
    public void Empty_latest_result_is_unavailable_while_connected()
    {
        var aggregator = new SpinBridgeReadingAggregator();
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        aggregator.ApplyPoll(now, 2.4, 30);
        aggregator.ApplyPoll(now.AddSeconds(1), null, null);

        var reading = aggregator.Build(now.AddSeconds(1), Stale);

        Assert.Equal(LinkState.Connected, reading.Channel.Link);
        Assert.Equal(Freshness.Unavailable, reading.TorqueNm.Freshness);
        Assert.Equal(Freshness.Unavailable, reading.AngleDeg.Freshness);
        Assert.Null(reading.TorqueNm.LastValidUtc);
    }

    [Fact]
    public void Disconnect_keeps_last_values_and_marks_them_stale()
    {
        var aggregator = new SpinBridgeReadingAggregator();
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        aggregator.ApplyPoll(now, 1.5, 20);
        aggregator.SetChannel(ChannelHealth.Disconnected(SpinBridgeReading.ChannelId, "socket closed"));

        var reading = aggregator.Build(now.AddSeconds(1), Stale);

        Assert.Equal(LinkState.Disconnected, reading.Channel.Link);
        Assert.Equal(1.5, reading.TorqueNm.Value);
        Assert.Equal(20, reading.AngleDeg.Value);
        Assert.Equal(Freshness.Stale, reading.TorqueNm.Freshness);
        Assert.Equal(Freshness.Stale, reading.AngleDeg.Freshness);
    }

    [Fact]
    public void Late_sample_on_an_open_session_is_stale_not_disconnected()
    {
        var aggregator = new SpinBridgeReadingAggregator();
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z");
        aggregator.ApplyPoll(now, 0.8, 10);

        var reading = aggregator.Build(now.AddSeconds(3), Stale);

        Assert.Equal(LinkState.Connected, reading.Channel.Link);
        Assert.Equal(Freshness.Stale, reading.TorqueNm.Freshness);
        Assert.Equal(0.8, reading.TorqueNm.Value);
    }
}
