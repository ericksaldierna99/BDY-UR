using UrHmi.Domain;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Telemetry;

public sealed class RobotSnapshotAggregator
{
    private enum FieldSource
    {
        None,
        Dashboard,
        Rtde
    }

    private ChannelHealth _dashboard = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);
    private ChannelHealth _rtde = ChannelHealth.Disconnected(ChannelIds.RobotRtde);
    private TimedValue<string> _robotMode = TimedValue<string>.Unavailable();
    private TimedValue<string> _robotStatus = TimedValue<string>.Unavailable();
    private TimedValue<string> _programStatus = TimedValue<string>.Unavailable();
    private TimedValue<string> _safetyStatus = TimedValue<string>.Unavailable();
    private TimedValue<string> _activeFault = TimedValue<string>.Unavailable();
    private TimedValue<TcpPose> _tcpPosition = TimedValue<TcpPose>.Unavailable();
    private TimedValue<JointPose> _jointPositions = TimedValue<JointPose>.Unavailable();
    private TimedValue<double> _tcpSpeed = TimedValue<double>.Unavailable();
    private FieldSource _robotModeSource;
    private FieldSource _robotStatusSource;
    private FieldSource _programStatusSource;
    private FieldSource _safetyStatusSource;
    private uint? _safetyStatusBits;
    private DateTimeOffset? _safetyBitsUtc;
    private HashSet<string> _rtdeKeys = new(StringComparer.Ordinal);

    public void SetDashboardHealth(ChannelHealth health) => _dashboard = health;

    public void SetRtdeHealth(ChannelHealth health) => _rtde = health;

    public void SetRtdeKeys(IEnumerable<string> negotiatedKeys)
    {
        _rtdeKeys = new HashSet<string>(negotiatedKeys, StringComparer.Ordinal);
    }

    public void ApplyDashboard(
        string? robotMode,
        string? safetyStatus,
        string? programState,
        string? running,
        DateTimeOffset now)
    {
        if (robotMode is not null && !RtdeOwns(DefaultRtdeOutputs.RobotMode))
        {
            _robotMode = TimedValue<string>.Live(robotMode, now);
            _robotModeSource = FieldSource.Dashboard;
        }

        if (safetyStatus is not null && !RtdeOwns(DefaultRtdeOutputs.SafetyStatus))
        {
            _safetyStatus = TimedValue<string>.Live(safetyStatus, now);
            _safetyStatusSource = FieldSource.Dashboard;
            RefreshActiveFault(now);
        }

        if (programState is not null && !RtdeOwns(DefaultRtdeOutputs.RuntimeState))
        {
            _programStatus = TimedValue<string>.Live(programState, now);
            _programStatusSource = FieldSource.Dashboard;
        }

        if (running is not null && !RtdeOwns(DefaultRtdeOutputs.RobotStatusBits))
        {
            _robotStatus = TimedValue<string>.Live(running, now);
            _robotStatusSource = FieldSource.Dashboard;
        }
    }

    public void ApplyRtdeFrame(RtdeFrame frame)
    {
        var now = frame.ReceivedUtc;
        if (TryGetInt(frame, DefaultRtdeOutputs.RobotMode, out var robotMode))
        {
            _robotMode = TimedValue<string>.Live(UrRobotMaps.RobotModeName(robotMode), now);
            _robotModeSource = FieldSource.Rtde;
        }

        if (TryGetInt(frame, DefaultRtdeOutputs.SafetyStatus, out var safety))
        {
            _safetyStatus = TimedValue<string>.Live(UrRobotMaps.SafetyStatusName(safety), now);
            _safetyStatusSource = FieldSource.Rtde;
        }

        if (TryGetUInt(frame, DefaultRtdeOutputs.RuntimeState, out var runtime))
        {
            _programStatus = TimedValue<string>.Live(UrRobotMaps.RuntimeStateName(runtime), now);
            _programStatusSource = FieldSource.Rtde;
        }

        if (TryGetUInt(frame, DefaultRtdeOutputs.RobotStatusBits, out var statusBits))
        {
            _robotStatus = TimedValue<string>.Live(UrRobotMaps.RobotStatusFromBits(statusBits), now);
            _robotStatusSource = FieldSource.Rtde;
        }

        if (TryGetUInt(frame, DefaultRtdeOutputs.SafetyStatusBits, out var safetyBits))
        {
            _safetyStatusBits = safetyBits;
            _safetyBitsUtc = now;
        }

        if (frame.Values.TryGetValue(DefaultRtdeOutputs.ActualTcpPose, out var poseRaw) && poseRaw is double[] poseVector)
        {
            var pose = TcpPose.FromVector6(poseVector);
            if (pose is not null)
            {
                _tcpPosition = TimedValue<TcpPose>.Live(pose, now);
            }
        }

        if (frame.Values.TryGetValue(DefaultRtdeOutputs.ActualQ, out var jointsRaw) && jointsRaw is double[] jointVector)
        {
            var joints = JointPose.FromVector6(jointVector);
            if (joints is not null)
            {
                _jointPositions = TimedValue<JointPose>.Live(joints, now);
            }
        }

        if (frame.Values.TryGetValue(DefaultRtdeOutputs.ActualTcpSpeed, out var speedRaw) && speedRaw is double[] speedVector
            && speedVector.Length >= 3)
        {
            var magnitude = Math.Sqrt(
                (speedVector[0] * speedVector[0])
                + (speedVector[1] * speedVector[1])
                + (speedVector[2] * speedVector[2]));
            _tcpSpeed = TimedValue<double>.Live(magnitude, now);
        }

        RefreshActiveFault(now);
    }

    public RobotSnapshot Build(DateTimeOffset now, TimeSpan dashboardStale, TimeSpan rtdeStale)
    {
        var dashboardLinkLive = _dashboard.Link == LinkState.Connected;
        var rtdeLinkLive = _rtde.Link == LinkState.Connected;

        var robotMode = Age(_robotMode, _robotModeSource, now, dashboardStale, rtdeStale, dashboardLinkLive, rtdeLinkLive);
        var robotStatus = Age(_robotStatus, _robotStatusSource, now, dashboardStale, rtdeStale, dashboardLinkLive, rtdeLinkLive);
        var programStatus = Age(_programStatus, _programStatusSource, now, dashboardStale, rtdeStale, dashboardLinkLive, rtdeLinkLive);
        var safetyStatus = Age(_safetyStatus, _safetyStatusSource, now, dashboardStale, rtdeStale, dashboardLinkLive, rtdeLinkLive);
        var tcpPosition = _tcpPosition.WithFreshness(now, rtdeStale, rtdeLinkLive);
        var jointPositions = _jointPositions.WithFreshness(now, rtdeStale, rtdeLinkLive);
        var tcpSpeed = _tcpSpeed.WithFreshness(now, rtdeStale, rtdeLinkLive);

        var faultSourceLive = _safetyStatusSource == FieldSource.Rtde ? rtdeLinkLive : dashboardLinkLive;
        var faultTimeout = _safetyStatusSource == FieldSource.Rtde ? rtdeStale : dashboardStale;
        var activeFault = _activeFault.WithFreshness(now, faultTimeout, faultSourceLive);
        if (_activeFault.LastValidUtc is not null && _activeFault.Value is null)
        {
            activeFault = new TimedValue<string>(null, _activeFault.LastValidUtc, activeFault.Freshness);
        }

        DateTimeOffset? lastValid = Max(
            robotMode.LastValidUtc,
            robotStatus.LastValidUtc,
            programStatus.LastValidUtc,
            safetyStatus.LastValidUtc,
            activeFault.LastValidUtc,
            tcpPosition.LastValidUtc,
            jointPositions.LastValidUtc,
            tcpSpeed.LastValidUtc);

        return new RobotSnapshot(
            now,
            _dashboard.WithAge(now),
            _rtde.WithAge(now),
            robotMode,
            robotStatus,
            programStatus,
            safetyStatus,
            activeFault,
            tcpPosition,
            jointPositions,
            tcpSpeed,
            lastValid);
    }

    private void RefreshActiveFault(DateTimeOffset now)
    {
        if (_safetyStatus.LastValidUtc is null && _safetyBitsUtc is null)
        {
            _activeFault = TimedValue<string>.Unavailable();
            return;
        }

        var fault = UrRobotMaps.ActiveFaultFrom(_safetyStatus.Value, _safetyStatusBits);
        var stamp = Max(_safetyStatus.LastValidUtc, _safetyBitsUtc) ?? now;
        _activeFault = new TimedValue<string>(fault, stamp, Freshness.Live);
    }

    private bool RtdeOwns(string key) => _rtdeKeys.Contains(key);

    private static TimedValue<string> Age(
        TimedValue<string> value,
        FieldSource source,
        DateTimeOffset now,
        TimeSpan dashboardStale,
        TimeSpan rtdeStale,
        bool dashboardLinkLive,
        bool rtdeLinkLive)
    {
        var (timeout, live) = source switch
        {
            FieldSource.Rtde => (rtdeStale, rtdeLinkLive),
            FieldSource.Dashboard => (dashboardStale, dashboardLinkLive),
            _ => (dashboardStale, false)
        };

        return value.WithFreshness(now, timeout, live);
    }

    private static bool TryGetInt(RtdeFrame frame, string key, out int value)
    {
        value = default;
        if (!frame.Values.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case uint u:
                value = (int)u;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetUInt(RtdeFrame frame, string key, out uint value)
    {
        value = default;
        if (!frame.Values.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case uint u:
                value = u;
                return true;
            case int i when i >= 0:
                value = (uint)i;
                return true;
            default:
                return false;
        }
    }

    private static DateTimeOffset? Max(params DateTimeOffset?[] values)
    {
        DateTimeOffset? max = null;
        foreach (var value in values)
        {
            if (value is { } current && (max is null || current > max))
            {
                max = current;
            }
        }

        return max;
    }
}
