using UrHmi.Domain.Robot;

namespace UrHmi.Domain.Tests;

public sealed class UrRobotMapsTests
{
    [Theory]
    [InlineData(7, "RUNNING")]
    [InlineData(5, "IDLE")]
    [InlineData(0, "DISCONNECTED")]
    [InlineData(42, "robot_mode=42")]
    public void Robot_mode_uses_polyscope_names_and_does_not_invent_unknown_codes(int code, string expected)
    {
        Assert.Equal(expected, UrRobotMaps.RobotModeName(code));
    }

    [Theory]
    [InlineData(1, "NORMAL")]
    [InlineData(3, "PROTECTIVE_STOP")]
    [InlineData(9, "FAULT")]
    [InlineData(8, "VIOLATION")]
    public void Safety_status_uses_documented_codes(int code, string expected)
    {
        Assert.Equal(expected, UrRobotMaps.SafetyStatusName(code));
    }

    [Fact]
    public void Protective_stop_is_not_an_identified_fault()
    {
        Assert.False(UrRobotMaps.IsIdentifiedFault("PROTECTIVE_STOP"));
        Assert.Null(UrRobotMaps.ActiveFaultFrom("PROTECTIVE_STOP", safetyStatusBits: 1u << 2));
    }

    [Fact]
    public void Fault_and_violation_bits_are_identified_faults()
    {
        Assert.Equal("FAULT", UrRobotMaps.ActiveFaultFrom("NORMAL", safetyStatusBits: 1u << 9));
        Assert.Equal("VIOLATION", UrRobotMaps.ActiveFaultFrom(null, safetyStatusBits: 1u << 8));
        Assert.Equal("FAULT", UrRobotMaps.ActiveFaultFrom("FAULT", safetyStatusBits: 0));
    }

    [Fact]
    public void Robot_status_bits_do_not_say_stopped_when_power_is_on()
    {
        var text = UrRobotMaps.RobotStatusFromBits(0b0011);

        Assert.Contains("Power on", text, StringComparison.Ordinal);
        Assert.Contains("program running", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stopped", text, StringComparison.OrdinalIgnoreCase);
    }
}
