namespace UrHmi.Domain.Configuration;

public sealed record HmiConfiguration(
    RobotConfiguration Robot,
    PlcConfiguration Plc,
    ToolsConfiguration Tools,
    UiConfiguration Ui,
    bool SafeMode = false)
{
    public static HmiConfiguration CreateDefault(bool safeMode = false) =>
        new(
            new RobotConfiguration(
                Ip: string.Empty,
                Dashboard: new DashboardChannelConfiguration(Port: 29999, StaleTimeoutMs: 2000),
                Rtde: new RtdeChannelConfiguration(Port: 30004, StaleTimeoutMs: 200, RecipeId: "default")),
            new PlcConfiguration(
                Ip: string.Empty,
                Transport: "simulator",
                StaleTimeoutMs: 1000),
            new ToolsConfiguration(SelectedId: null, Enabled: Array.Empty<string>()),
            new UiConfiguration(Theme: "industrial"),
            SafeMode: safeMode);
}

public sealed record RobotConfiguration(
    string Ip,
    DashboardChannelConfiguration Dashboard,
    RtdeChannelConfiguration Rtde);

public sealed record DashboardChannelConfiguration(int Port, int StaleTimeoutMs);

public sealed record RtdeChannelConfiguration(int Port, int StaleTimeoutMs, string RecipeId);

public sealed record PlcConfiguration(string Ip, string Transport, int StaleTimeoutMs);

public sealed record ToolsConfiguration(string? SelectedId, IReadOnlyList<string> Enabled);

public sealed record UiConfiguration(string Theme);
