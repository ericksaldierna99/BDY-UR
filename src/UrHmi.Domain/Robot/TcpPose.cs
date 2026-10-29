namespace UrHmi.Domain.Robot;

public sealed record TcpPose(
    double X,
    double Y,
    double Z,
    double Rx,
    double Ry,
    double Rz);
