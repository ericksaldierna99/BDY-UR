using UrHmi.Domain.Robot;

namespace UrHmi.Domain.Ports;

/// <summary>
/// RTDE cyclic data channel (TCP 30004, binary v2). Recipe is negotiated; rejected keys stay Unavailable.
/// </summary>
public interface IRtdeClient : IAsyncDisposable
{
    ChannelHealth Health { get; }

    IObservable<RtdeFrame> Frames { get; }

    IReadOnlyList<string> NegotiatedKeys { get; }

    IReadOnlyList<string> RejectedKeys { get; }

    Task ConnectAsync(string host, int port, RtdeRecipe recipe, CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
