using System.Globalization;
using UrHmi.Domain;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Telemetry;

public static class RobotDashboardProjection
{
    public static (string Text, IndicatorKind Kind) ProjectConnection(ChannelHealth dashboard, ChannelHealth rtde)
    {
        var dashboardUp = dashboard.Link == LinkState.Connected;
        var rtdeUp = rtde.Link == LinkState.Connected;
        if (dashboardUp && rtdeUp)
        {
            return ("Connected", IndicatorKind.Normal);
        }

        if (dashboardUp || rtdeUp)
        {
            return ("Degraded", IndicatorKind.Warning);
        }

        if (dashboard.Link == LinkState.Connecting || rtde.Link == LinkState.Connecting)
        {
            return ("Connecting", IndicatorKind.Warning);
        }

        if (dashboard.Link == LinkState.Reconnecting || rtde.Link == LinkState.Reconnecting)
        {
            return ("Reconnecting", IndicatorKind.Warning);
        }

        if (dashboard.Link == LinkState.Faulted || rtde.Link == LinkState.Faulted)
        {
            return ("Faulted", IndicatorKind.Fault);
        }

        return ("Disconnected", IndicatorKind.CommsLoss);
    }

    public static IndicatorKind KindForRobotMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return IndicatorKind.Normal;
        }

        return mode.ToUpperInvariant() switch
        {
            "RUNNING" or "IDLE" or "POWER_ON" => IndicatorKind.Normal,
            "POWER_OFF" or "DISCONNECTED" or "NO_CONTROLLER" => IndicatorKind.Warning,
            "BOOTING" or "CONFIRM_SAFETY" or "BACKDRIVE" or "UPDATING_FIRMWARE" or "WAITING_POWER_BUTTON" => IndicatorKind.Warning,
            _ => IndicatorKind.Warning
        };
    }

    public static IndicatorKind KindForSafety(string? safety)
    {
        if (string.IsNullOrWhiteSpace(safety))
        {
            return IndicatorKind.Normal;
        }

        return safety.ToUpperInvariant() switch
        {
            "NORMAL" => IndicatorKind.Normal,
            "REDUCED" => IndicatorKind.Warning,
            "FAULT" or "VIOLATION" => IndicatorKind.Fault,
            "PROTECTIVE_STOP" or "SAFEGUARD_STOP" or "RECOVERY"
                or "SYSTEM_EMERGENCY_STOP" or "ROBOT_EMERGENCY_STOP"
                or "AUTOMATIC_MODE_SAFEGUARD_STOP" or "SYSTEM_THREE_POSITION_ENABLING_STOP" => IndicatorKind.Safety,
            _ => IndicatorKind.Warning
        };
    }

    public static IndicatorKind KindForActiveFault(string? fault)
    {
        return string.IsNullOrWhiteSpace(fault) ? IndicatorKind.Normal : IndicatorKind.Fault;
    }

    public static string FormatTimed(string? value, Freshness freshness)
    {
        if (freshness == Freshness.Unavailable || string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        return value;
    }

    public static string FormatTcpPosition(TimedValue<TcpPose> pose)
    {
        if (pose.Freshness == Freshness.Unavailable || pose.Value is null)
        {
            return "—";
        }

        return pose.Value.ToDisplayString();
    }

    public static string FormatTcpSpeed(TimedValue<double> speed)
    {
        if (speed.Freshness == Freshness.Unavailable)
        {
            return "—";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{speed.Value:0.000} m/s");
    }

    public static string FormatAge(TimeSpan? age)
    {
        if (age is null)
        {
            return "—";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{age.Value.TotalSeconds:0.0} s");
    }
}
