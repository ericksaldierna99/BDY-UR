using Microsoft.Extensions.Logging;
using UrHmi.Application.Authorization;
using UrHmi.Domain;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Jog;

public sealed class RobotJogService : IRobotJogService
{
    private readonly IJogPoseStore _store;
    private readonly IUrScriptClient _urScript;
    private readonly IRobotDashboardClient _dashboard;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<RobotJogService> _logger;
    private readonly object _gate = new();
    private IReadOnlyList<SavedJogPose> _slots = SavedJogPose.EmptySlots();

    public RobotJogService(
        IJogPoseStore store,
        IUrScriptClient urScript,
        IRobotDashboardClient dashboard,
        IAuthorizationService authorization,
        ILogger<RobotJogService> logger)
    {
        _store = store;
        _urScript = urScript;
        _dashboard = dashboard;
        _authorization = authorization;
        _logger = logger;
    }

    public IReadOnlyList<SavedJogPose> Slots
    {
        get
        {
            lock (_gate)
            {
                return _slots;
            }
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _slots = loaded;
        }
    }

    public async Task<JogResult> SaveCurrentAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryGetSlot(slot, out _))
        {
            return JogResult.Fail("Position slot must be 1 to 10.");
        }

        if (!IsLinked(snapshot))
        {
            return JogResult.Fail("Robot is disconnected. Position was not saved.");
        }

        var tcp = snapshot.TcpPosition.Freshness == Freshness.Live ? snapshot.TcpPosition.Value : null;
        var joints = snapshot.JointPositions.Freshness == Freshness.Live ? snapshot.JointPositions.Value : null;
        if (tcp is null && joints is null)
        {
            return JogResult.Fail("No live TCP or joint data to save.");
        }

        var saved = new SavedJogPose(slot, tcp, joints, DateTimeOffset.UtcNow);
        var next = Replace(slot, saved);
        await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _slots = next;
        }

        return JogResult.Ok($"Position {slot} saved.");
    }

    public async Task<JogResult> GoToAsync(int slot, RobotSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TryGetSlot(slot, out var pose))
        {
            return JogResult.Fail("Position slot must be 1 to 10.");
        }

        if (!IsLinked(snapshot))
        {
            return JogResult.Fail("Robot is disconnected. Motion was not sent.");
        }

        if (!_authorization.HasProcessChangePermission())
        {
            return JogResult.Fail("Permission is required to move the robot.");
        }

        if (!pose.HasPose)
        {
            return JogResult.Fail($"Position {slot} has no saved coordinates.");
        }

        try
        {
            var script = pose.Joints is { } joints
                ? UrScriptCommands.MoveJ(joints)
                : UrScriptCommands.MoveL(pose.Tcp!);
            await _urScript.SendAsync(script, cancellationToken).ConfigureAwait(false);
            return JogResult.Ok($"Move command sent to Position {slot}.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "URScript go-to Position {Slot} failed.", slot);
            return JogResult.Fail(ex.Message);
        }
    }

    public async Task<JogResult> ReleaseBrakesAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsLinked(snapshot))
        {
            return JogResult.Fail("Robot is disconnected. Brake release was not sent.");
        }

        if (!_authorization.HasProcessChangePermission())
        {
            return JogResult.Fail("Permission is required to release the brakes.");
        }

        var result = await _dashboard.BrakeReleaseAsync(cancellationToken).ConfigureAwait(false);
        return result.Success
            ? JogResult.Ok(result.Value ?? "Brake releasing.")
            : JogResult.Fail(result.Error ?? "Brake release failed.");
    }

    public static bool IsLinked(RobotSnapshot snapshot) =>
        snapshot.Dashboard.Link == LinkState.Connected || snapshot.Rtde.Link == LinkState.Connected;

    private bool TryGetSlot(int slot, out SavedJogPose pose)
    {
        if (slot is < 1 or > SavedJogPose.SlotCount)
        {
            pose = new SavedJogPose(slot, null, null, null);
            return false;
        }

        lock (_gate)
        {
            pose = _slots[slot - 1];
            return true;
        }
    }

    private IReadOnlyList<SavedJogPose> Replace(int slot, SavedJogPose saved)
    {
        lock (_gate)
        {
            var copy = _slots.ToArray();
            copy[slot - 1] = saved;
            return copy;
        }
    }
}
