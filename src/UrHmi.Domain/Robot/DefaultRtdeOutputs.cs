namespace UrHmi.Domain.Robot;

/// <summary>
/// RTDE output keys requested for UR12e PolyScope 5.25.2. Missing keys stay Unavailable.
/// Frequency 10 Hz is an HMI decision; the controller may stream faster internally.
/// </summary>
public static class DefaultRtdeOutputs
{
    public const string Timestamp = "timestamp";
    public const string ActualTcpPose = "actual_TCP_pose";
    public const string ActualQ = "actual_q";
    public const string ActualTcpSpeed = "actual_TCP_speed";
    public const string RobotMode = "robot_mode";
    public const string SafetyStatus = "safety_status";
    public const string RuntimeState = "runtime_state";
    public const string RobotStatusBits = "robot_status_bits";
    public const string SafetyStatusBits = "safety_status_bits";

    public const double FrequencyHz = 10.0;

    public static readonly IReadOnlyList<string> Keys =
    [
        Timestamp,
        ActualTcpPose,
        ActualQ,
        ActualTcpSpeed,
        RobotMode,
        SafetyStatus,
        RuntimeState,
        RobotStatusBits,
        SafetyStatusBits
    ];

    public static RtdeRecipe CreateRecipe(string recipeId) =>
        new(string.IsNullOrWhiteSpace(recipeId) ? "default" : recipeId, Keys, FrequencyHz);
}
