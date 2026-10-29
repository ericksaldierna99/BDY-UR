using System.Globalization;

namespace UrHmi.Domain.Robot;

public sealed record TcpPose(
    double X,
    double Y,
    double Z,
    double Rx,
    double Ry,
    double Rz)
{
    public static TcpPose? FromVector6(IReadOnlyList<double> values)
    {
        if (values.Count < 6)
        {
            return null;
        }

        return new TcpPose(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    public string ToDisplayString() =>
        string.Create(CultureInfo.InvariantCulture, $"{X:0.000}, {Y:0.000}, {Z:0.000} m");
}
