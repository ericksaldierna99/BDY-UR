namespace UrHmi.Domain.Ports;

/// <summary>
/// Dashboard Server channel. Phase 0 is a placeholder; no motion or program control.
/// </summary>
public interface IRobotDashboardClient : IAsyncDisposable
{
    ChannelHealth Health { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
