namespace UrHmi.Domain.Plc;

public enum WriteOutcome
{
    Accepted,
    Rejected,
    Timeout,
    Unconfirmed
}

public sealed record WriteResult(
    WriteOutcome Outcome,
    string CorrelationId,
    string? Message);
