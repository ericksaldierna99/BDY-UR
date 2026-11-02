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

    bool IsFreedriveActive { get; }

    Task<JogResult> EnableFreedriveAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Close the held program socket immediately, then send <c>end_freedrive_mode()</c> in the background when linked.
    /// Does not require permission. Does not wait for the send.
    /// </summary>
    Task<JogResult> EndFreedriveAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>Close the held URScript program socket without sending.</summary>
    void StopFreedrive();

    Task<JogResult> DeleteAsync(int slot, CancellationToken cancellationToken = default);
}
