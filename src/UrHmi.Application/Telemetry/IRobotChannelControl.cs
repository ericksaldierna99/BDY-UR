namespace UrHmi.Application.Telemetry;

public interface IRobotChannelControl
{
    string? PolyscopeVersion { get; }

    Task<RobotChannelProbe> ProbeAsync(CancellationToken cancellationToken = default);

    Task ReconnectAsync(CancellationToken cancellationToken = default);
}
