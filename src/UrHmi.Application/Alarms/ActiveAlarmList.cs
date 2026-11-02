using System.Globalization;
using UrHmi.Domain.Alarms;

namespace UrHmi.Application.Alarms;

public sealed record ActiveAlarmRow(string Origin, string Time, string Description);

public static class ActiveAlarmList
{
    public static IReadOnlyList<ActiveAlarmRow> Project(IEnumerable<AlarmRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .Where(record => record.IsActive)
            .OrderByDescending(record => record.RaisedUtc)
            .Select(record => new ActiveAlarmRow(
                OriginLabel(record.Origin),
                FormatTime(record.RaisedUtc),
                record.Description))
            .ToArray();
    }

    public static string OriginLabel(AlarmOrigin origin) => origin switch
    {
        AlarmOrigin.RobotUr12e => "Robot UR12e",
        AlarmOrigin.PlcMicro820 => "PLC Micro820",
        AlarmOrigin.RobotCommunication => "Robot communication",
        AlarmOrigin.PlcCommunication => "PLC communication",
        AlarmOrigin.Tooling => "Tooling",
        AlarmOrigin.HmiApplication => "HMI application",
        _ => origin.ToString()
    };

    public static string FormatTime(DateTimeOffset raisedUtc) =>
        FormatTime(raisedUtc, TimeZoneInfo.Local);

    public static string FormatTime(DateTimeOffset raisedUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = TimeZoneInfo.ConvertTime(raisedUtc, timeZone);
        return local.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
