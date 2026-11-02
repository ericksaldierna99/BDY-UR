namespace UrHmi.Domain;

public sealed record TimedValue<T>(
    T? Value,
    DateTimeOffset? LastValidUtc,
    Freshness Freshness)
{
    public static TimedValue<T> Unavailable() =>
        new(default, null, Freshness.Unavailable);

    public static TimedValue<T> Live(T value, DateTimeOffset lastValidUtc) =>
        new(value, lastValidUtc, Freshness.Live);

    public TimedValue<T> WithFreshness(DateTimeOffset now, TimeSpan staleTimeout, bool linkLive)
    {
        if (LastValidUtc is null)
        {
            return Unavailable();
        }

        if (!linkLive || now - LastValidUtc.Value > staleTimeout)
        {
            return this with { Freshness = Freshness.Stale };
        }

        return this with { Freshness = Freshness.Live };
    }
}
