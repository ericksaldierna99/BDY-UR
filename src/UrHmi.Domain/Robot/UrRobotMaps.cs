namespace UrHmi.Domain.Robot;

/// <summary>
/// PolyScope 5.25.2 names from the RTDE interface and Dashboard Server.
/// Unknown numeric codes are shown as <c>{field}={n}</c>; they are not invented labels.
/// </summary>
public static class UrRobotMaps
{
    public static string RobotModeName(int code) => code switch
    {
        -1 => "NO_CONTROLLER",
        0 => "DISCONNECTED",
        1 => "CONFIRM_SAFETY",
        2 => "BOOTING",
        3 => "POWER_OFF",
        4 => "POWER_ON",
        5 => "IDLE",
        6 => "BACKDRIVE",
        7 => "RUNNING",
        8 => "UPDATING_FIRMWARE",
        9 => "WAITING_POWER_BUTTON",
        _ => $"robot_mode={code}"
    };

    public static string SafetyStatusName(int code) => code switch
    {
        1 => "NORMAL",
        2 => "REDUCED",
        3 => "PROTECTIVE_STOP",
        4 => "RECOVERY",
        5 => "SAFEGUARD_STOP",
        6 => "SYSTEM_EMERGENCY_STOP",
        7 => "ROBOT_EMERGENCY_STOP",
        8 => "VIOLATION",
        9 => "FAULT",
        10 => "VALIDATE_JOINT_ID",
        11 => "UNDEFINED_SAFETY_MODE",
        12 => "AUTOMATIC_MODE_SAFEGUARD_STOP",
        13 => "SYSTEM_THREE_POSITION_ENABLING_STOP",
        _ => $"safety_status={code}"
    };

    public static string RuntimeStateName(uint code) => code switch
    {
        0 => "STOPPING",
        1 => "STOPPED",
        2 => "PLAYING",
        3 => "PAUSING",
        4 => "PAUSED",
        5 => "RESUMING",
        _ => $"runtime_state={code}"
    };

    public static string RobotStatusFromBits(uint bits)
    {
        var powerOn = (bits & 1u) != 0;
        var programRunning = (bits & 2u) != 0;
        var teach = (bits & 4u) != 0;
        var powerButton = (bits & 8u) != 0;

        var status = powerOn ? "Power on" : "Power off";
        if (programRunning)
        {
            status += ", program running";
        }

        if (teach)
        {
            status += ", teach";
        }

        if (powerButton)
        {
            status += ", power button";
        }

        return status;
    }

    public static bool IsIdentifiedFault(string? safetyStatus)
    {
        if (string.IsNullOrWhiteSpace(safetyStatus))
        {
            return false;
        }

        return safetyStatus.Equals("FAULT", StringComparison.OrdinalIgnoreCase)
            || safetyStatus.Equals("VIOLATION", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SafetyBitsIndicateFault(uint bits) =>
        (bits & (1u << 8)) != 0 || (bits & (1u << 9)) != 0;

    public static string? ActiveFaultFrom(string? safetyStatus, uint? safetyStatusBits)
    {
        if (IsIdentifiedFault(safetyStatus))
        {
            return safetyStatus!.ToUpperInvariant();
        }

        if (safetyStatusBits is { } bits)
        {
            if ((bits & (1u << 9)) != 0)
            {
                return "FAULT";
            }

            if ((bits & (1u << 8)) != 0)
            {
                return "VIOLATION";
            }
        }

        return null;
    }
}
