namespace UrHmi.Domain.Plc;

public sealed record PlcWriteRequest(
    string CorrelationId,
    IReadOnlyDictionary<string, object?> Tags);
