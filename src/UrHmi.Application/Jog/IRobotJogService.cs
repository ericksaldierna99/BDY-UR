using UrHmi.Domain.Robot;

namespace UrHmi.Application.Jog;

public interface IRobotJogService
{
    IReadOnlyList<SavedJogPose> Slots { get; }

    Task ReloadAsync(CancellationToken cancellationToken = default);

    Task<JogResult> SaveCurrentAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<JogResult> GoToAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<JogResult> InitializeAsync(
        RobotSnapshot snapshot,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<JogResult> EnableFreedriveAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send <c>end_freedrive_mode()</c> when linked, then close the held URScript program socket.
    /// Always closes the socket. Does not require permission.
    /// </summary>
    Task<JogResult> EndFreedriveAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Close the held URScript program socket without sending.</summary>
    void StopFreedrive();

    Task<JogResult> DeleteAsync(int slot, CancellationToken cancellationToken = default);
}
