namespace UrHmi.Domain.Robot;

public sealed record RtdeFrame(
    DateTimeOffset ReceivedUtc,
    IReadOnlyDictionary<string, object?> Values);
