using UrHmi.Domain.Plc;

namespace UrHmi.Application.Telemetry;

public interface IPlcTelemetry
{
    PlcSnapshot Current { get; }

    IObservable<PlcSnapshot> Snapshots { get; }
}
