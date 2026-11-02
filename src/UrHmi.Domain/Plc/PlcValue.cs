namespace UrHmi.Domain.Plc;

public sealed record PlcValue(
    string Key,
    object? Value,
    DateTimeOffset? LastValidUtc,
    Freshness Freshness);
