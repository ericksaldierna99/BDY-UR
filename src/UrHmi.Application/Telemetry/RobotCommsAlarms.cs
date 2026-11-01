using UrHmi.Domain.Alarms;
using UrHmi.Domain.Ports;

namespace UrHmi.Application.Telemetry;

internal static class RobotCommsAlarms
{
    public const string DashboardLink = "comms.robot.dashboard.link";
    public const string DashboardStale = "comms.robot.dashboard.stale";
    public const string RtdeLink = "comms.robot.rtde.link";
    public const string RtdeStale = "comms.robot.rtde.stale";
    public const string Category = "comunicación";

    public static void SetLink(IAlarmSink sink, string alarmId, string description, bool down, DateTimeOffset now)
    {
        if (down)
        {
            sink.Raise(new AlarmRecord(
                alarmId,
                now,
                null,
                AlarmOrigin.RobotCommunication,
                Category,
                description,
                IsActive: true,
                Duration: null));
        }
        else
        {
            sink.Clear(alarmId, now);
        }
    }
}
