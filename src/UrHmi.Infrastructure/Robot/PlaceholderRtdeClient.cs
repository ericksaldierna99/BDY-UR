using Microsoft.Extensions.Logging;
using UrHmi.Domain;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Infrastructure.Observables;

namespace UrHmi.Infrastructure.Robot;

public sealed class PlaceholderRtdeClient : IRtdeClient
{
    private readonly ILogger<PlaceholderRtdeClient> _logger;

    public PlaceholderRtdeClient(ILogger<PlaceholderRtdeClient> logger)
    {
        _logger = logger;
        Health = ChannelHealth.Disconnected(ChannelIds.RobotRtde);
        Frames = NeverObservable<RtdeFrame>.Instance;
    }

    public ChannelHealth Health { get; private set; }

    public IObservable<RtdeFrame> Frames { get; }

    public Task ConnectAsync(RtdeRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        _logger.LogInformation(
            "RTDE placeholder: recipe {RecipeId} is not negotiated in Phase 0.",
            recipe.RecipeId);
        Health = ChannelHealth.Disconnected(ChannelIds.RobotRtde);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Health = ChannelHealth.Disconnected(ChannelIds.RobotRtde);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
