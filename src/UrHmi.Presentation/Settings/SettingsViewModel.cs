using CommunityToolkit.Mvvm.ComponentModel;
using UrHmi.Domain.Configuration;

namespace UrHmi.Presentation.Settings;

public sealed class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(HmiConfiguration configuration)
    {
        Configuration = configuration;
        SafeModeMessage = configuration.SafeMode
            ? "Started in safe mode: configuration was missing, invalid, or contained sensitive keys."
            : null;
    }

    public string Title => "Settings";

    public HmiConfiguration Configuration { get; }

    public string? SafeModeMessage { get; }

    public string RobotIp => string.IsNullOrWhiteSpace(Configuration.Robot.Ip)
        ? "(not set)"
        : Configuration.Robot.Ip;

    public int DashboardPort => Configuration.Robot.Dashboard.Port;

    public int RtdePort => Configuration.Robot.Rtde.Port;

    public string PlcIp => string.IsNullOrWhiteSpace(Configuration.Plc.Ip)
        ? "(not set)"
        : Configuration.Plc.Ip;

    public string PlcTransport => Configuration.Plc.Transport;

    public string Notice =>
        "Editing, validation, permissions, and connection diagnostics are not applied in Phase 0. Secrets are not stored in JSON.";
}
