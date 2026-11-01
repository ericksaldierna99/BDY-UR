namespace UrHmi.Domain.Robot;

public sealed record RobotSnapshot(
    DateTimeOffset ProducedUtc,
    ChannelHealth Dashboard,
    ChannelHealth Rtde,
    TimedValue<string> RobotMode,
    TimedValue<string> RobotStatus,
    TimedValue<string> ProgramStatus,
    TimedValue<string> SafetyStatus,
    TimedValue<string> ActiveFault,
    TimedValue<TcpPose> TcpPosition,
    TimedValue<JointPose> JointPositions,
    TimedValue<double> TcpSpeed,
    DateTimeOffset? LastValidUpdateUtc)
{
    public static RobotSnapshot Empty(DateTimeOffset producedUtc) =>
        new(
            producedUtc,
            ChannelHealth.Disconnected(ChannelIds.RobotDashboard),
            ChannelHealth.Disconnected(ChannelIds.RobotRtde),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<string>.Unavailable(),
            TimedValue<TcpPose>.Unavailable(),
            TimedValue<JointPose>.Unavailable(),
            TimedValue<double>.Unavailable(),
            LastValidUpdateUtc: null);
}
