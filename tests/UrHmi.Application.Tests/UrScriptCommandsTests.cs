using UrHmi.Application.Jog;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Tests;

public sealed class UrScriptCommandsTests
{
    [Fact]
    public void MoveJ_formats_radians_and_trailing_newline()
    {
        var joints = new JointPose(0.1, -0.2, 0.3, -0.4, 1.570796, 0);

        var script = UrScriptCommands.MoveJ(joints);

        Assert.Equal("movej([0.1, -0.2, 0.3, -0.4, 1.570796, 0], a=1, v=0.25)\n", script);
    }

    [Fact]
    public void MoveL_formats_pose_vector()
    {
        var pose = new TcpPose(0.4, -0.15, 0.25, 0, 3.141593, 0);

        var script = UrScriptCommands.MoveL(pose);

        Assert.Equal("movel(p[0.4, -0.15, 0.25, 0, 3.141593, 0], a=0.5, v=0.15)\n", script);
    }

    [Fact]
    public void FormatStored_empty_slot_has_no_coordinates()
    {
        var text = UrScriptCommands.FormatStored(new SavedJogPose(3, null, null, null));

        Assert.Equal("No coordinates saved.", text);
    }

    [Fact]
    public void FormatStored_includes_tcp_and_joints()
    {
        var pose = new SavedJogPose(
            1,
            new TcpPose(0.1, 0.2, 0.3, 0, 0, 0),
            new JointPose(0, 0, 0, 0, 0, 0),
            DateTimeOffset.UtcNow);

        var text = UrScriptCommands.FormatStored(pose);

        Assert.Contains("TCP", text, StringComparison.Ordinal);
        Assert.Contains("X=0.100", text, StringComparison.Ordinal);
        Assert.Contains("Joints", text, StringComparison.Ordinal);
        Assert.Contains(pose.Joints!.ToDisplayString(), text, StringComparison.Ordinal);
    }
}
