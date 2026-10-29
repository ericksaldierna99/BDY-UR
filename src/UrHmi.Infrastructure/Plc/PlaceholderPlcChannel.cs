using Microsoft.Extensions.Logging;
using UrHmi.Domain;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Infrastructure.Observables;

namespace UrHmi.Infrastructure.Plc;

public sealed class PlaceholderPlcChannel : IPlcChannel
{
    private readonly ILogger<PlaceholderPlcChannel> _logger;

    public PlaceholderPlcChannel(ILogger<PlaceholderPlcChannel> logger)
    {
        _logger = logger;
        Health = ChannelHealth.Disconnected(ChannelIds.Plc);
        Snapshots = NeverObservable<PlcSnapshot>.Instance;
    }

    public ChannelHealth Health { get; private set; }

    public IObservable<PlcSnapshot> Snapshots { get; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("PLC channel placeholder: no EtherNet/IP or Modbus transport in Phase 0.");
        Health = ChannelHealth.Disconnected(ChannelIds.Plc);
        return Task.CompletedTask;
    }

    public Task<WriteResult> WriteAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new WriteResult(
            WriteOutcome.Rejected,
            request.CorrelationId,
            "PLC writes are not available in Phase 0."));
    }

    public Task DisconnectAsync()
    {
        Health = ChannelHealth.Disconnected(ChannelIds.Plc);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
