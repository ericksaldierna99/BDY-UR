using UrHmi.Infrastructure.Robot;

namespace UrHmi.Infrastructure.Tests;

public sealed class DashboardProtocolTests
{
    [Fact]
    public void Parses_robotmode_and_safetystatus_payloads()
    {
        var mode = DashboardProtocol.ParseNamed("Robotmode: RUNNING", "Robotmode");
        var safety = DashboardProtocol.ParseNamed("Safetystatus: PROTECTIVE_STOP", "Safetystatus");

        Assert.True(mode.Success);
        Assert.Equal("RUNNING", mode.Value);
        Assert.True(safety.Success);
        Assert.Equal("PROTECTIVE_STOP", safety.Value);
    }

    [Fact]
    public void Parses_program_state_without_inventing_stopped_on_error()
    {
        var playing = DashboardProtocol.ParseProgramState("PLAYING default.urp");
        var error = DashboardProtocol.ParseProgramState("Error: Unknown command");

        Assert.Equal("PLAYING", playing.Value);
        Assert.False(error.Success);
        Assert.DoesNotContain("STOPPED", error.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parses_running_flag()
    {
        var running = DashboardProtocol.ParseRunning("Program running: true");
        var idle = DashboardProtocol.ParseRunning("Program running: false");

        Assert.Equal("Program running", running.Value);
        Assert.Equal("Program not running", idle.Value);
    }
}
