using UrHmi.Domain.Robot;

namespace UrHmi.Domain.Ports;

public interface IJogPoseStore
{
    Task<IReadOnlyList<SavedJogPose>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<SavedJogPose> poses, CancellationToken cancellationToken = default);
}
