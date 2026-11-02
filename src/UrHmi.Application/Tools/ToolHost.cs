using UrHmi.Application.Authorization;
using UrHmi.Application.Telemetry;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Application.Tools;

public sealed class ToolHost : IToolHost
{
    private readonly IPlcChannel _channel;
    private readonly IAuthorizationService _authorization;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ToolHost(
        IPlcTelemetry plc,
        IRobotTelemetry robot,
        IAlarmSink alarms,
        IPlcChannel channel,
        IAuthorizationService authorization)
    {
        ArgumentNullException.ThrowIfNull(plc);
        ArgumentNullException.ThrowIfNull(robot);
        ArgumentNullException.ThrowIfNull(alarms);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(authorization);

        Plc = plc.Snapshots;
        Robot = robot.Snapshots;
        Alarms = alarms;
        _channel = channel;
        _authorization = authorization;
    }

    public IObservable<PlcSnapshot> Plc { get; }

    public IObservable<RobotSnapshot> Robot { get; }

    public IAlarmSink Alarms { get; }

    public async Task<WriteResult> WritePlcAsync(
        PlcWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_authorization.HasProcessChangePermission())
        {
            return new WriteResult(
                WriteOutcome.Rejected,
                request.CorrelationId,
                "Process changes require permission.");
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _channel.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
