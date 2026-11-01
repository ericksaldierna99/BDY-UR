using UrHmi.Application.Alarms;
using UrHmi.Domain.Alarms;

namespace UrHmi.Application.Tests;

public sealed class ActiveAlarmListTests
{
    [Fact]
    public void Empty_store_projects_an_empty_list()
    {
        var rows = ActiveAlarmList.Project([]);

        Assert.Empty(rows);
    }

    [Fact]
    public void Lists_only_active_alarms_with_origin_time_and_description()
    {
        var older = new AlarmRecord(
            "comms.robot.dashboard.link",
            DateTimeOffset.Parse("2026-10-29T12:00:00Z"),
            null,
            AlarmOrigin.RobotCommunication,
            "comunicación",
            "Dashboard Server channel is not connected.",
            IsActive: true,
            Duration: null);
        var newer = new AlarmRecord(
            "comms.robot.rtde.link",
            DateTimeOffset.Parse("2026-10-29T12:01:00Z"),
            null,
            AlarmOrigin.RobotCommunication,
            "comunicación",
            "RTDE channel is not connected.",
            IsActive: true,
            Duration: null);
        var recovered = new AlarmRecord(
            "old",
            DateTimeOffset.Parse("2026-10-29T11:00:00Z"),
            DateTimeOffset.Parse("2026-10-29T11:05:00Z"),
            AlarmOrigin.HmiApplication,
            "aplicación",
            "Should not appear.",
            IsActive: false,
            Duration: TimeSpan.FromMinutes(5));

        var rows = ActiveAlarmList.Project([recovered, older, newer]);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Robot communication", rows[0].Origin);
        Assert.Equal("RTDE channel is not connected.", rows[0].Description);
        Assert.DoesNotContain(rows, row => row.Description.Contains("Should not appear", StringComparison.Ordinal));
        Assert.Matches(@"^\d{2}:\d{2}:\d{2}$", rows[0].Time);
        Assert.DoesNotContain("UTC", rows[0].Time, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-", rows[0].Time, StringComparison.Ordinal);
    }

    [Fact]
    public void Formats_computer_local_time_of_day_only()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("lab", TimeSpan.FromHours(-6), "lab", "lab");
        var raised = DateTimeOffset.Parse("2026-10-29T18:01:09Z");

        var text = ActiveAlarmList.FormatTime(raised, zone);

        Assert.Equal("12:01:09", text);
    }

    [Theory]
    [InlineData(AlarmOrigin.RobotUr12e, "Robot UR12e")]
    [InlineData(AlarmOrigin.PlcMicro820, "PLC Micro820")]
    [InlineData(AlarmOrigin.RobotCommunication, "Robot communication")]
    [InlineData(AlarmOrigin.PlcCommunication, "PLC communication")]
    [InlineData(AlarmOrigin.Tooling, "Tooling")]
    [InlineData(AlarmOrigin.HmiApplication, "HMI application")]
    public void Origin_labels_match_specified_sources(AlarmOrigin origin, string expected)
    {
        Assert.Equal(expected, ActiveAlarmList.OriginLabel(origin));
    }
}
