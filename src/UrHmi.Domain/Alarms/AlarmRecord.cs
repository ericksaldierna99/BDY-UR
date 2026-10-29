namespace UrHmi.Domain.Alarms;

public sealed record AlarmRecord(
    string Id,
    DateTimeOffset RaisedUtc,
    DateTimeOffset? ClearedUtc,
    AlarmOrigin Origin,
    string Category,
    string Description,
    bool IsActive,
    TimeSpan? Duration);
