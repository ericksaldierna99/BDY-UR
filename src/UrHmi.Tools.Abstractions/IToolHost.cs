using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Tools.Abstractions;

public interface IToolHost
{
    IObservable<PlcSnapshot> Plc { get; }

    IObservable<RobotSnapshot> Robot { get; }

    IAlarmSink Alarms { get; }

    Task<WriteResult> WritePlcAsync(PlcWriteRequest request, CancellationToken cancellationToken = default);
}
