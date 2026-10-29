using UrHmi.Domain.Robot;

namespace UrHmi.Infrastructure.Robot;

internal static class DashboardProtocol
{
    public static bool IsWelcome(string line) =>
        line.StartsWith("Connected", StringComparison.OrdinalIgnoreCase);

    public static bool IsError(string line) =>
        line.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);

    public static DashboardCommandResult ParseNamed(string raw, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var trimmed = raw.Trim();
        if (IsError(trimmed))
        {
            return DashboardCommandResult.Failed(trimmed, trimmed);
        }

        if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = trimmed[prefix.Length..].Trim().TrimStart(':').Trim();
            return string.IsNullOrWhiteSpace(value)
                ? DashboardCommandResult.Failed("Empty Dashboard Server response.", trimmed)
                : DashboardCommandResult.Ok(value, trimmed);
        }

        return DashboardCommandResult.Ok(trimmed, trimmed);
    }

    public static DashboardCommandResult ParseProgramState(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);
        var trimmed = raw.Trim();
        if (IsError(trimmed))
        {
            return DashboardCommandResult.Failed(trimmed, trimmed);
        }

        var first = trimmed.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        return first.Length == 0
            ? DashboardCommandResult.Failed("Empty programState response.", trimmed)
            : DashboardCommandResult.Ok(first[0], trimmed);
    }

    public static DashboardCommandResult ParseRunning(string raw)
    {
        var named = ParseNamed(raw, "Program running");
        if (!named.Success)
        {
            return named;
        }

        if (bool.TryParse(named.Value, out var running))
        {
            return DashboardCommandResult.Ok(running ? "Program running" : "Program not running", named.Raw!);
        }

        return named;
    }
}
