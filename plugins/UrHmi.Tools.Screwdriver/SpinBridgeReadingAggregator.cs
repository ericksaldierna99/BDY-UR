using UrHmi.Domain;

namespace UrHmi.Tools.Screwdriver;

public sealed class SpinBridgeReadingAggregator
{
    private ChannelHealth _channel = ChannelHealth.Disconnected(SpinBridgeReading.ChannelId);
    private TimedValue<double> _torqueNm = TimedValue<double>.Unavailable();
    private TimedValue<double> _angleDeg = TimedValue<double>.Unavailable();

    public void SetChannel(ChannelHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        _channel = health;
    }

    public void ApplyPoll(DateTimeOffset now, double? torqueNm, double? angleDeg, string? lastError = null)
    {
        _channel = _channel.WithRx(now, LinkState.Connected);
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            _channel = _channel.WithLink(LinkState.Connected, lastError);
        }

        _torqueNm = torqueNm is { } torque
            ? TimedValue<double>.Live(torque, now)
            : TimedValue<double>.Unavailable();
        _angleDeg = angleDeg is { } angle
            ? TimedValue<double>.Live(angle, now)
            : TimedValue<double>.Unavailable();
    }

    public SpinBridgeReading Build(DateTimeOffset now, TimeSpan staleTimeout)
    {
        var linkLive = _channel.Link == LinkState.Connected;
        return new SpinBridgeReading(
            _channel.WithAge(now),
            _torqueNm.WithFreshness(now, staleTimeout, linkLive),
            _angleDeg.WithFreshness(now, staleTimeout, linkLive));
    }
}
