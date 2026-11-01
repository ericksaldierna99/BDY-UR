using UrHmi.Domain.Robot;

namespace UrHmi.Application.Jog;

public interface IRobotJogService
{
    IReadOnlyList<SavedJogPose> Slots { get; }

    Task ReloadAsync(CancellationToken cancellationToken = default);

    Task<JogResult> SaveCurrentAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<JogResult> GoToAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<JogResult> ReleaseBrakesAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default);
}
