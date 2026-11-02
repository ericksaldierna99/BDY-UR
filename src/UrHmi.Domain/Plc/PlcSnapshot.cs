namespace UrHmi.Domain.Plc;

public sealed record PlcSnapshot(
    DateTimeOffset ProducedUtc,
    ChannelHealth Channel,
    TimedValue<IReadOnlyDictionary<string, PlcValue>> Tags,
    PendingConfirmation? LastWrite)
{
    public static PlcSnapshot Empty(DateTimeOffset producedUtc) =>
        new(
            producedUtc,
            ChannelHealth.Disconnected(ChannelIds.Plc),
            TimedValue<IReadOnlyDictionary<string, PlcValue>>.Unavailable(),
            LastWrite: null);
}
