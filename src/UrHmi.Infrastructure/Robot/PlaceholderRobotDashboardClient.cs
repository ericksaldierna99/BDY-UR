using Microsoft.Extensions.Logging;
using UrHmi.Domain;
using UrHmi.Domain.Ports;

namespace UrHmi.Infrastructure.Robot;

public sealed class PlaceholderRobotDashboardClient : IRobotDashboardClient
{
    private readonly ILogger<PlaceholderRobotDashboardClient> _logger;

    public PlaceholderRobotDashboardClient(ILogger<PlaceholderRobotDashboardClient> logger)
    {
        _logger = logger;
        Health = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);
    }

    public ChannelHealth Health { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Dashboard Server placeholder: TCP protocol is not implemented in Phase 0.");
        Health = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Health = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
