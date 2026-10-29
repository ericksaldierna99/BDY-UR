namespace UrHmi.Domain;

public sealed record ChannelHealth(
    string ChannelId,
    LinkState Link,
    DateTimeOffset? LastRxUtc,
    TimeSpan? Age,
    string? LastError)
{
    public static ChannelHealth Disconnected(string channelId) =>
        new(channelId, LinkState.Disconnected, null, null, null);
}
