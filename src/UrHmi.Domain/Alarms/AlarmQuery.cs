namespace UrHmi.Domain.Alarms;

public sealed record AlarmQuery(
    AlarmOrigin? Origin,
    string? Category,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? Text,
    bool? IsActive);
