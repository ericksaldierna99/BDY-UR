using System.Net;
using UrHmi.Domain.Configuration;

namespace UrHmi.Application.Configuration;

public sealed class HmiConfigurationValidator
{
    private static readonly HashSet<string> AllowedTransports = new(StringComparer.OrdinalIgnoreCase)
    {
        "simulator",
        "ethernetip",
        "modbus"
    };

    public IReadOnlyList<string> Validate(HmiConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();

        ValidateOptionalIp(configuration.Robot.Ip, "robot.ip", errors);
        ValidatePort(configuration.Robot.Dashboard.Port, "robot.dashboard.port", errors);
        ValidatePositive(configuration.Robot.Dashboard.StaleTimeoutMs, "robot.dashboard.staleTimeoutMs", errors);
        ValidatePort(configuration.Robot.Rtde.Port, "robot.rtde.port", errors);
        ValidatePositive(configuration.Robot.Rtde.StaleTimeoutMs, "robot.rtde.staleTimeoutMs", errors);
        ValidatePort(configuration.Robot.UrScript.Port, "robot.urscript.port", errors);
        ValidatePort(configuration.Robot.UrScript.FallbackPort, "robot.urscript.fallbackPort", errors);
        if (string.IsNullOrWhiteSpace(configuration.Robot.Rtde.RecipeId))
        {
            errors.Add("robot.rtde.recipeId is required.");
        }

        ValidateOptionalIp(configuration.Plc.Ip, "plc.ip", errors);
        ValidatePositive(configuration.Plc.StaleTimeoutMs, "plc.staleTimeoutMs", errors);
        if (!AllowedTransports.Contains(configuration.Plc.Transport))
        {
            errors.Add("plc.transport must be simulator, ethernetip, or modbus.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Ui.Theme))
        {
            errors.Add("ui.theme is required.");
        }

        return errors;
    }

    private static void ValidateOptionalIp(string ip, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return;
        }

        if (!IPAddress.TryParse(ip, out _))
        {
            errors.Add($"{path} is not a valid IP address.");
        }
    }

    private static void ValidatePort(int port, string path, List<string> errors)
    {
        if (port is < 1 or > 65535)
        {
            errors.Add($"{path} must be between 1 and 65535.");
        }
    }

    private static void ValidatePositive(int value, string path, List<string> errors)
    {
        if (value <= 0)
        {
            errors.Add($"{path} must be greater than 0.");
        }
    }
}
