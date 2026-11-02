using UrHmi.Domain.Robot;

namespace UrHmi.Domain.Ports;

/// <summary>
/// Dashboard Server channel (TCP 29999, ASCII). Queries plus explicit commands the HMI requires.
/// </summary>
public interface IRobotDashboardClient : IAsyncDisposable
{
    ChannelHealth Health { get; }

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    Task DisconnectAsync();

    Task<DashboardCommandResult> GetRobotModeAsync(CancellationToken cancellationToken = default);

    Task<DashboardCommandResult> GetSafetyStatusAsync(CancellationToken cancellationToken = default);

    Task<DashboardCommandResult> GetProgramStateAsync(CancellationToken cancellationToken = default);

    Task<DashboardCommandResult> GetRunningAsync(CancellationToken cancellationToken = default);

    Task<DashboardCommandResult> GetPolyscopeVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>PolyScope 5.x Dashboard <c>power on</c> (space-separated).</summary>
    Task<DashboardCommandResult> PowerOnAsync(CancellationToken cancellationToken = default);

    /// <summary>PolyScope 5.x Dashboard <c>brake release</c> (space-separated).</summary>
    Task<DashboardCommandResult> BrakeReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>PolyScope 5.x Dashboard <c>unlock protective stop</c> (space-separated).</summary>
    Task<DashboardCommandResult> UnlockProtectiveStopAsync(CancellationToken cancellationToken = default);

    /// <summary>PolyScope 5.6+ Dashboard <c>is in remote control</c>.</summary>
    Task<DashboardCommandResult> IsInRemoteControlAsync(CancellationToken cancellationToken = default);
}
