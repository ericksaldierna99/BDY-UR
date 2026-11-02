using System.Globalization;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Jog;

public static class UrScriptCommands
{
    public static string MoveJ(JointPose joints, double acceleration = 1.0, double speed = 0.25)
    {
        ArgumentNullException.ThrowIfNull(joints);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"movej({FormatList(joints.ToRadians())}, a={acceleration:0.###}, v={speed:0.###})\n");
    }

    public static string MoveL(TcpPose pose, double acceleration = 0.5, double speed = 0.15)
    {
        ArgumentNullException.ThrowIfNull(pose);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"movel(p{FormatList(pose.ToVector())}, a={acceleration:0.###}, v={speed:0.###})\n");
    }

    /// <summary>
    /// Complete program so <c>freedrive_mode</c> stays active. A one-line call ends with the script and exits freedrive.
    /// The function must be invoked; a <c>def</c> without a call never enters freedrive.
    /// </summary>
    public static string EnableFreedrive() =>
        "def start_freedrive():\n" +
        "  freedrive_mode()\n" +
        "  while True:\n" +
        "    sync()\n" +
        "  end\n" +
        "end\n" +
        "start_freedrive()\n";

    public static string DisableFreedrive() => "end_freedrive_mode()\n";

    public static string FormatStored(SavedJogPose pose)
    {
        ArgumentNullException.ThrowIfNull(pose);
        if (!pose.HasPose)
        {
            return "No coordinates saved.";
        }

        var lines = new List<string>();
        if (pose.Tcp is { } tcp)
        {
            lines.Add("TCP");
            lines.Add(tcp.ToJogDisplayString());
        }

        if (pose.Joints is { } joints)
        {
            lines.Add("Joints");
            lines.Add(joints.ToDisplayString());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatList(IReadOnlyList<double> values) =>
        "[" + string.Join(", ", values.Select(value => value.ToString("0.######", CultureInfo.InvariantCulture))) + "]";
}
