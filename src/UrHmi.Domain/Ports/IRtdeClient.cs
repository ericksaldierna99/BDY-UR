using UrHmi.Domain.Robot;

namespace UrHmi.Domain.Ports;

/// <summary>
/// RTDE cyclic data channel. Phase 0 is a placeholder; no recipe negotiation yet.
/// </summary>
public interface IRtdeClient : IAsyncDisposable
{
    ChannelHealth Health { get; }

    IObservable<RtdeFrame> Frames { get; }

    Task ConnectAsync(RtdeRecipe recipe, CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
