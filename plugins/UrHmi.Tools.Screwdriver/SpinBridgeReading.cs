using UrHmi.Domain;

namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Latest tightening values from SpinBridge OPC UA. Torque and angle are not
/// taken from the UR controller or from PLC tags.
/// </summary>
public sealed record SpinBridgeReading(
    ChannelHealth Channel,
    TimedValue<double> TorqueNm,
    TimedValue<double> AngleDeg)
{
    public const string ChannelId = "spinbridge.opcua";

    public static SpinBridgeReading Unavailable() =>
        new(
            ChannelHealth.Disconnected(ChannelId),
            TimedValue<double>.Unavailable(),
            TimedValue<double>.Unavailable());
}
