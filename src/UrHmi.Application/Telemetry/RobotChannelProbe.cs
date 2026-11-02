using UrHmi.Domain;

namespace UrHmi.Application.Telemetry;

public sealed record RobotChannelProbe(
    DateTimeOffset ProbedUtc,
    bool IpConfigured,
    ChannelHealth Dashboard,
    ChannelHealth Rtde,
    string? PolyscopeVersion,
    IReadOnlyList<string> RtdeNegotiatedKeys,
    IReadOnlyList<string> RtdeRejectedKeys,
    string Summary);
