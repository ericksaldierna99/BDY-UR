using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrHmi.Application.Observables;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Telemetry;

/// <summary>
/// Phase 0 stub: publishes an empty snapshot. Does not connect Dashboard or RTDE
/// and does not copy link state onto <see cref="RobotSnapshot.RobotStatus"/>.
/// </summary>
public sealed class RobotTelemetryService : IHostedService, IRobotTelemetry
{
    private readonly IRobotDashboardClient _dashboard;
    private readonly IRtdeClient _rtde;
    private readonly ILogger<RobotTelemetryService> _logger;
    private readonly SnapshotBroadcaster<RobotSnapshot> _snapshots =
        new(RobotSnapshot.Empty(DateTimeOffset.UtcNow));

    public RobotTelemetryService(
        IRobotDashboardClient dashboard,
        IRtdeClient rtde,
        ILogger<RobotTelemetryService> logger)
    {
        _dashboard = dashboard;
        _rtde = rtde;
        _logger = logger;
    }

    public RobotSnapshot Current => _snapshots.Current;

    public IObservable<RobotSnapshot> Snapshots => _snapshots;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Robot telemetry placeholder started. Dashboard link={DashboardLink}, RTDE link={RtdeLink}. Protocol clients are not connected in Phase 0.",
            _dashboard.Health.Link,
            _rtde.Health.Link);

        _snapshots.Publish(RobotSnapshot.Empty(DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
