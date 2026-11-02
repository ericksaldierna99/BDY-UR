namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// PLC tag names owned by this module. They are not a Micro820 map; the adapter
/// publishes these keys when that map exists. Missing keys stay Unavailable.
/// </summary>
public static class ScrewdriverTags
{
    public const string Status = "screwdriver.status";
    public const string Result = "screwdriver.result";
    public const string TorqueNm = "screwdriver.torque_nm";
    public const string AngleDeg = "screwdriver.angle_deg";
    public const string CycleCount = "screwdriver.cycle_count";
    public const string ProgramId = "screwdriver.program_id";
    public const string FaultCode = "screwdriver.fault_code";
    public const string Enabled = "screwdriver.enabled";
    public const string CommandEnable = "screwdriver.cmd.enable";
    public const string CommandDisable = "screwdriver.cmd.disable";
    public const string CommandReset = "screwdriver.cmd.reset";

    public static readonly IReadOnlyList<string> Required =
    [
        Status,
        Result,
        TorqueNm,
        AngleDeg,
        CycleCount,
        ProgramId,
        FaultCode,
        Enabled
    ];
}
