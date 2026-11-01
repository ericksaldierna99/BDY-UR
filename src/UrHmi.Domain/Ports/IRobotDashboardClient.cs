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

    Task<DashboardCommandResult> BrakeReleaseAsync(CancellationToken cancellationToken = default);
}
