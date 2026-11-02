namespace UrHmi.Domain;

public sealed record ChannelHealth(
    string ChannelId,
    LinkState Link,
    DateTimeOffset? LastRxUtc,
    TimeSpan? Age,
    string? LastError)
{
    public static ChannelHealth Disconnected(string channelId, string? lastError = null) =>
        new(channelId, LinkState.Disconnected, null, null, lastError);

    public ChannelHealth WithAge(DateTimeOffset now) =>
        this with { Age = LastRxUtc is { } rx ? now - rx : null };

    public ChannelHealth WithLink(LinkState link, string? lastError = null) =>
        this with { Link = link, LastError = lastError ?? LastError };

    public ChannelHealth WithRx(DateTimeOffset now, LinkState link) =>
        new(ChannelId, link, now, TimeSpan.Zero, LastError: null);
}
