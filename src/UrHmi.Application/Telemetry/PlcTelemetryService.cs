using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrHmi.Application.Observables;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;

namespace UrHmi.Application.Telemetry;

/// <summary>
/// Phase 0 stub: publishes a disconnected PLC snapshot. No simulator tags and no industrial protocol.
/// </summary>
public sealed class PlcTelemetryService : IHostedService, IPlcTelemetry
{
    private readonly IPlcChannel _channel;
    private readonly ILogger<PlcTelemetryService> _logger;
    private readonly SnapshotBroadcaster<PlcSnapshot> _snapshots =
        new(PlcSnapshot.Empty(DateTimeOffset.UtcNow));

    public PlcTelemetryService(IPlcChannel channel, ILogger<PlcTelemetryService> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public PlcSnapshot Current => _snapshots.Current;

    public IObservable<PlcSnapshot> Snapshots => _snapshots;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "PLC telemetry placeholder started. Channel link={Link}. IPlcChannel is not connected in Phase 0.",
            _channel.Health.Link);

        _snapshots.Publish(PlcSnapshot.Empty(DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
