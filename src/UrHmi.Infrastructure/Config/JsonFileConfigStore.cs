using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Ports;

namespace UrHmi.Infrastructure.Config;

public sealed class JsonFileConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILogger<JsonFileConfigStore> _logger;

    public JsonFileConfigStore(string path, ILogger<JsonFileConfigStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public async Task<HmiConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            var created = HmiConfiguration.CreateDefault();
            await SaveAsync(created, cancellationToken).ConfigureAwait(false);
            return created;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (ContainsSensitiveProperty(document.RootElement, out var sensitiveName))
            {
                _logger.LogWarning(
                    "Configuration file {Path} contains sensitive key '{Key}'. Starting in safe mode with defaults. Secrets must not live in JSON.",
                    _path,
                    sensitiveName);
                return HmiConfiguration.CreateDefault(safeMode: true);
            }

            var loaded = document.RootElement.Deserialize<HmiJsonDocument>(JsonOptions);
            if (loaded is null)
            {
                _logger.LogWarning("Configuration file {Path} was empty. Starting in safe mode.", _path);
                return HmiConfiguration.CreateDefault(safeMode: true);
            }

            return loaded.ToDomain();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Configuration file {Path} is invalid. Starting in safe mode.", _path);
            return HmiConfiguration.CreateDefault(safeMode: true);
        }
    }

    public async Task SaveAsync(HmiConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var payload = HmiJsonDocument.FromDomain(configuration);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using (var probe = JsonDocument.Parse(json))
        {
            if (ContainsSensitiveProperty(probe.RootElement, out var sensitiveName))
            {
                throw new InvalidOperationException(
                    $"Refusing to persist configuration because it contains sensitive key '{sensitiveName}'.");
            }
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }

    internal static bool ContainsSensitiveProperty(JsonElement element, out string? name)
    {
        name = null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (SensitiveConfigurationKeys.IsSensitive(property.Name))
                    {
                        name = property.Name;
                        return true;
                    }

                    if (ContainsSensitiveProperty(property.Value, out name))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsSensitiveProperty(item, out name))
                    {
                        return true;
                    }
                }

                return false;
            default:
                return false;
        }
    }

    private sealed class HmiJsonDocument
    {
        public RobotJson Robot { get; init; } = new();
        public PlcJson Plc { get; init; } = new();
        public ToolsJson Tools { get; init; } = new();
        public UiJson Ui { get; init; } = new();

        public HmiConfiguration ToDomain() =>
            new(
                new RobotConfiguration(
                    Robot.Ip ?? string.Empty,
                    new DashboardChannelConfiguration(Robot.Dashboard.Port, Robot.Dashboard.StaleTimeoutMs),
                    new RtdeChannelConfiguration(Robot.Rtde.Port, Robot.Rtde.StaleTimeoutMs, Robot.Rtde.RecipeId),
                    new UrScriptChannelConfiguration(Robot.UrScript.Port, Robot.UrScript.FallbackPort)),
                new PlcConfiguration(Plc.Ip ?? string.Empty, Plc.Transport, Plc.StaleTimeoutMs),
                new ToolsConfiguration(Tools.SelectedId, Tools.Enabled ?? Array.Empty<string>()),
                new UiConfiguration(Ui.Theme));

        public static HmiJsonDocument FromDomain(HmiConfiguration configuration) =>
            new()
            {
                Robot = new RobotJson
                {
                    Ip = configuration.Robot.Ip,
                    Dashboard = new DashboardJson
                    {
                        Port = configuration.Robot.Dashboard.Port,
                        StaleTimeoutMs = configuration.Robot.Dashboard.StaleTimeoutMs
                    },
                    Rtde = new RtdeJson
                    {
                        Port = configuration.Robot.Rtde.Port,
                        StaleTimeoutMs = configuration.Robot.Rtde.StaleTimeoutMs,
                        RecipeId = configuration.Robot.Rtde.RecipeId
                    },
                    UrScript = new UrScriptJson
                    {
                        Port = configuration.Robot.UrScript.Port,
                        FallbackPort = configuration.Robot.UrScript.FallbackPort
                    }
                },
                Plc = new PlcJson
                {
                    Ip = configuration.Plc.Ip,
                    Transport = configuration.Plc.Transport,
                    StaleTimeoutMs = configuration.Plc.StaleTimeoutMs
                },
                Tools = new ToolsJson
                {
                    SelectedId = configuration.Tools.SelectedId,
                    Enabled = configuration.Tools.Enabled.ToArray()
                },
                Ui = new UiJson { Theme = configuration.Ui.Theme }
            };
    }

    private sealed class RobotJson
    {
        public string? Ip { get; init; }
        public DashboardJson Dashboard { get; init; } = new();
        public RtdeJson Rtde { get; init; } = new();
        public UrScriptJson UrScript { get; init; } = new();
    }

    private sealed class DashboardJson
    {
        public int Port { get; init; } = 29999;
        public int StaleTimeoutMs { get; init; } = 2000;
    }

    private sealed class RtdeJson
    {
        public int Port { get; init; } = 30004;
        public int StaleTimeoutMs { get; init; } = 200;
        public string RecipeId { get; init; } = "default";
    }

    private sealed class UrScriptJson
    {
        public int Port { get; init; } = 30002;
        public int FallbackPort { get; init; } = 30001;
    }

    private sealed class PlcJson
    {
        public string? Ip { get; init; }
        public string Transport { get; init; } = "simulator";
        public int StaleTimeoutMs { get; init; } = 1000;
    }

    private sealed class ToolsJson
    {
        public string? SelectedId { get; init; }
        public string[]? Enabled { get; init; }
    }

    private sealed class UiJson
    {
        public string Theme { get; init; } = "industrial";
    }
}
