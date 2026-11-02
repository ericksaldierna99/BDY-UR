namespace UrHmi.Domain.Plc;

public sealed record PendingConfirmation(
    string CorrelationId,
    DateTimeOffset RequestedUtc,
    string? Status);
