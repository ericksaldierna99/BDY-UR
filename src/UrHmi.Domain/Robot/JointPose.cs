using System.Globalization;

namespace UrHmi.Domain.Robot;

public sealed record JointPose(
    double J1,
    double J2,
    double J3,
    double J4,
    double J5,
    double J6)
{
    public static JointPose? FromVector6(IReadOnlyList<double> values)
    {
        if (values.Count < 6)
        {
            return null;
        }

        return new JointPose(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    public IReadOnlyList<double> ToRadians() => [J1, J2, J3, J4, J5, J6];

    public string ToDisplayString()
    {
        static double Deg(double rad) => rad * 180.0 / Math.PI;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Deg(J1):0.1}, {Deg(J2):0.1}, {Deg(J3):0.1}, {Deg(J4):0.1}, {Deg(J5):0.1}, {Deg(J6):0.1} °");
    }
}
