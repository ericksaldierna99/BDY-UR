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

    internal TimeSpan ModePollInterval { get; set; } = TimeSpan.FromMilliseconds(300);

    internal TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(45);

    internal TimeSpan RunningTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal TimeSpan SafetyTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public async Task<JogResult> InitializeAsync(
        RobotSnapshot snapshot,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsLinked(snapshot))
        {
            return JogResult.Fail("Robot is disconnected. Initialize was not sent.");
        }

        if (!_authorization.HasProcessChangePermission())
        {
            return JogResult.Fail("Permission is required to initialize the robot.");
        }

        var remote = await _dashboard.IsInRemoteControlAsync(cancellationToken).ConfigureAwait(false);
        if (remote.Success && string.Equals(remote.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return JogResult.Fail("Teach pendant must be in Remote.");
        }

        var mode = await ReadModeAsync(cancellationToken).ConfigureAwait(false);
        if (mode is null)
        {
            return JogResult.Fail("Could not read robotmode.");
        }

        if (!IsRunning(mode))
        {
            if (IsPowerOff(mode) || IsUnknown(mode))
            {
                progress?.Invoke("Power on…");
                var power = await _dashboard.PowerOnAsync(cancellationToken).ConfigureAwait(false);
                if (!power.Success)
                {
                    return JogResult.Fail(power.Error ?? "Power on failed.");
                }
            }

            progress?.Invoke("Waiting for booting to complete (IDLE)…");
            var idle = await WaitForModeAsync(
                value => IsIdle(value) || IsRunning(value),
                "booting complete (IDLE)",
                IdleTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!idle.Succeeded)
            {
                return idle;
            }

            mode = await ReadModeAsync(cancellationToken).ConfigureAwait(false);
            if (!IsRunning(mode))
            {
                progress?.Invoke("Releasing brakes…");
                var brakes = await _dashboard.BrakeReleaseAsync(cancellationToken).ConfigureAwait(false);
                if (!brakes.Success)
                {
                    return JogResult.Fail(brakes.Error ?? "Brake release failed.");
                }

                progress?.Invoke("Waiting for robot active (RUNNING)…");
                var running = await WaitForModeAsync(
                    IsRunning,
                    "robot active (RUNNING)",
                    RunningTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (!running.Succeeded)
                {
                    return running;
                }
            }
        }

        progress?.Invoke("Waiting for normal safety…");
        var safety = await EnsureNormalSafetyAsync(progress, cancellationToken).ConfigureAwait(false);
        if (!safety.Succeeded)
        {
            return safety;
        }

        return JogResult.Ok("Robot initialized: power on, booting complete, RUNNING, brakes released, safety NORMAL.");
    }

    public async Task<JogResult> EnableFreedriveAsync(RobotSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsLinked(snapshot))
        {
            return JogResult.Fail("Robot is disconnected. Freedrive was not sent.");
        }

        if (!_authorization.HasProcessChangePermission())
        {
            return JogResult.Fail("Permission is required to enable freedrive.");
        }

        var remote = await _dashboard.IsInRemoteControlAsync(cancellationToken).ConfigureAwait(false);
        if (remote.Success && string.Equals(remote.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return JogResult.Fail("Teach pendant must be in Remote.");
        }

        var mode = await ReadModeAsync(cancellationToken).ConfigureAwait(false);
        if (!IsRunning(mode))
        {
            return JogResult.Fail("Robot must be powered on with brakes released. Use Initialize robot first.");
        }

        try
        {
            await _urScript.SendProgramAsync(UrScriptCommands.EnableFreedrive(), cancellationToken).ConfigureAwait(false);
            return JogResult.Ok("Freedrive enabled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "URScript freedrive_mode failed.");
            return JogResult.Fail(ex.Message);
        }
    }

    public async Task<JogResult> DeleteAsync(int slot, CancellationToken cancellationToken = default)
    {
        if (!TryGetSlot(slot, out var pose))
        {
            return JogResult.Fail("Position slot must be 1 to 10.");
        }

        if (!pose.HasPose)
        {
            return JogResult.Fail($"Position {slot} has no saved coordinates.");
        }

        var next = Replace(slot, new SavedJogPose(slot, null, null, null));
        await _store.SaveAsync(next, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _slots = next;
        }

        return JogResult.Ok($"Position {slot} deleted.");
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

    private async Task<string?> ReadModeAsync(CancellationToken cancellationToken)
    {
        var result = await _dashboard.GetRobotModeAsync(cancellationToken).ConfigureAwait(false);
        return result.Success ? result.Value : null;
    }

    private async Task<JogResult> WaitForModeAsync(
        Func<string?, bool> match,
        string description,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? last = null;
        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await ReadModeAsync(cancellationToken).ConfigureAwait(false);
            if (match(last))
            {
                return JogResult.Ok(description);
            }

            await Task.Delay(
                    ModePollInterval > TimeSpan.Zero ? ModePollInterval : TimeSpan.FromMilliseconds(1),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return JogResult.Fail($"Timed out waiting for {description}. Last robotmode: {last ?? "unknown"}.");
    }

    private async Task<JogResult> EnsureNormalSafetyAsync(Action<string>? progress, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + SafetyTimeout;
        string? last = null;
        var unlocked = false;
        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var safety = await _dashboard.GetSafetyStatusAsync(cancellationToken).ConfigureAwait(false);
            last = safety.Value;
            if (safety.Success && IsNormal(safety.Value))
            {
                return JogResult.Ok("normal");
            }

            if (!unlocked
                && safety.Success
                && string.Equals(safety.Value, "PROTECTIVE_STOP", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Invoke("Unlocking protective stop…");
                await _dashboard.UnlockProtectiveStopAsync(cancellationToken).ConfigureAwait(false);
                unlocked = true;
            }

            await Task.Delay(
                    ModePollInterval > TimeSpan.Zero ? ModePollInterval : TimeSpan.FromMilliseconds(1),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return JogResult.Fail($"Timed out waiting for safety NORMAL. Last safetystatus: {last ?? "unknown"}.");
    }

    private static bool IsRunning(string? mode) => EqualsMode(mode, "RUNNING");

    private static bool IsIdle(string? mode) => EqualsMode(mode, "IDLE");

    private static bool IsPowerOff(string? mode) => EqualsMode(mode, "POWER_OFF");

    private static bool IsUnknown(string? mode) => string.IsNullOrWhiteSpace(mode);

    private static bool IsNormal(string? safety) => EqualsMode(safety, "NORMAL");

    private static bool EqualsMode(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
