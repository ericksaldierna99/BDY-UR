using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrHmi.App.Dispatching;
using UrHmi.Application.Authorization;
using UrHmi.Application.Configuration;
using UrHmi.Application.Telemetry;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Ports;
using UrHmi.Application.Jog;
using UrHmi.Infrastructure.Alarms;
using UrHmi.Infrastructure.Config;
using UrHmi.Infrastructure.Jog;
using UrHmi.Infrastructure.Plc;
using UrHmi.Infrastructure.Robot;
using UrHmi.Infrastructure.Secrets;
using UrHmi.Presentation.Alarms;
using UrHmi.Presentation.Dashboard;
using UrHmi.Presentation.Jems;
using UrHmi.Presentation.Navigation;
using UrHmi.Presentation.Settings;
using UrHmi.Presentation.Shell;
using UrHmi.Presentation.Tools;

namespace UrHmi.App.Composition;

internal static class ServiceRegistration
{
    public static void AddUrHmi(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(Dispatcher.CurrentDispatcher));
        services.AddSingleton<HmiConfigurationValidator>();
        services.AddSingleton<IConfigStore>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonFileConfigStore>>();
            var path = Path.Combine(AppContext.BaseDirectory, "hmi.json");
            return new JsonFileConfigStore(path, logger);
        });
        services.AddSingleton(sp =>
        {
            var store = sp.GetRequiredService<IConfigStore>();
            var loaded = store.LoadAsync().GetAwaiter().GetResult();
            var validator = sp.GetRequiredService<HmiConfigurationValidator>();
            var errors = validator.Validate(loaded);
            if (errors.Count > 0)
            {
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                    .CreateLogger("UrHmi.Config");
                logger.LogWarning("Configuration failed validation ({Count} error(s)). Starting in safe mode.", errors.Count);
                return HmiConfiguration.CreateDefault(safeMode: true);
            }

            return loaded;
        });

        services.AddSingleton<IUserPrompt, WpfUserPrompt>();
        services.AddSingleton<ISecretStore, UnconfiguredSecretStore>();
        services.AddSingleton<IAuthorizationService, MaintenanceRoleAuthorizationService>();
        services.AddSingleton<InMemoryAlarmStore>();
        services.AddSingleton<IAlarmStore>(sp => sp.GetRequiredService<InMemoryAlarmStore>());
        services.AddSingleton<IAlarmSink>(sp => sp.GetRequiredService<InMemoryAlarmStore>());

        services.AddSingleton<IRobotDashboardClient, TcpDashboardClient>();
        services.AddSingleton<IRtdeClient, RtdeClient>();
        services.AddSingleton<IUrScriptClient, UrScriptClient>();
        services.AddSingleton<IJogPoseStore>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonJogPoseStore>>();
            var path = Path.Combine(AppContext.BaseDirectory, "jog-poses.json");
            return new JsonJogPoseStore(path, logger);
        });
        services.AddSingleton<RobotJogService>();
        services.AddSingleton<IRobotJogService>(sp => sp.GetRequiredService<RobotJogService>());
        services.AddSingleton<IPlcChannel, PlaceholderPlcChannel>();

        services.AddSingleton<RobotTelemetryService>();
        services.AddSingleton<IRobotTelemetry>(sp => sp.GetRequiredService<RobotTelemetryService>());
        services.AddSingleton<IRobotChannelControl>(sp => sp.GetRequiredService<RobotTelemetryService>());
        services.AddHostedService(sp => sp.GetRequiredService<RobotTelemetryService>());

        services.AddSingleton<PlcTelemetryService>();
        services.AddSingleton<IPlcTelemetry>(sp => sp.GetRequiredService<PlcTelemetryService>());
        services.AddHostedService(sp => sp.GetRequiredService<PlcTelemetryService>());

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AlarmsViewModel>();
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<JemsViewModel>();
        services.AddTransient<SettingsViewModel>();

        services.AddSingleton<IWorkspaceModule>(sp => new WorkspaceModule(
            "dashboard",
            "Dashboard",
            1,
            () => sp.GetRequiredService<DashboardViewModel>()));
        services.AddSingleton<IWorkspaceModule>(sp => new WorkspaceModule(
            "alarms",
            "Alarm Management",
            2,
            () => sp.GetRequiredService<AlarmsViewModel>()));
        services.AddSingleton<IWorkspaceModule>(sp => new WorkspaceModule(
            "tools",
            "Tool Management",
            3,
            () => sp.GetRequiredService<ToolsViewModel>()));
        services.AddSingleton<IWorkspaceModule>(sp => new WorkspaceModule(
            "jems",
            "JEMS",
            4,
            () => sp.GetRequiredService<JemsViewModel>()));
        services.AddSingleton<IWorkspaceModule>(sp => new WorkspaceModule(
            "settings",
            "Settings",
            5,
            () => sp.GetRequiredService<SettingsViewModel>()));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
