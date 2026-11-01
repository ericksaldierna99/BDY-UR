namespace UrHmi.Domain.Robot;

public sealed record SavedJogPose(
    int Slot,
    TcpPose? Tcp,
    JointPose? Joints,
    DateTimeOffset? SavedUtc)
{
    public const int SlotCount = 10;

    public bool HasPose => Tcp is not null || Joints is not null;

    public static IReadOnlyList<SavedJogPose> EmptySlots() =>
        Enumerable.Range(1, SlotCount).Select(slot => new SavedJogPose(slot, null, null, null)).ToArray();
}
