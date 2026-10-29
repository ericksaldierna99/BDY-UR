using UrHmi.Domain.Robot;

namespace UrHmi.Application.Telemetry;

public interface IRobotTelemetry
{
    RobotSnapshot Current { get; }

    IObservable<RobotSnapshot> Snapshots { get; }
}
