namespace UrHmi.Domain.Robot;

public sealed record DashboardCommandResult(
    bool Success,
    string? Value,
    string? Raw,
    string? Error)
{
    public static DashboardCommandResult Failed(string error, string? raw = null) =>
        new(false, null, raw, error);

    public static DashboardCommandResult Ok(string value, string raw) =>
        new(true, value, raw, null);
}
