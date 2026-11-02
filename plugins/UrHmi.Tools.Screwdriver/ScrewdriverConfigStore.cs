using System.Text.Json;
using UrHmi.Domain.Configuration;

namespace UrHmi.Tools.Screwdriver;

public sealed class ScrewdriverConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ResolvePath(string? directory = null)
    {
        var dir = directory
            ?? Path.GetDirectoryName(typeof(ScrewdriverConfigStore).Assembly.Location)
            ?? AppContext.BaseDirectory;
        return Path.Combine(dir, ScrewdriverConfiguration.FileName);
    }

    public static ScrewdriverConfiguration LoadOrCreate(string? directory = null)
    {
        var path = ResolvePath(directory);
        if (!File.Exists(path))
        {
            var created = ScrewdriverConfiguration.CreateDefault();
            Save(created, path);
            return created;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (ContainsSensitiveProperty(document.RootElement, out _))
            {
                return ScrewdriverConfiguration.CreateDefault();
            }

            var loaded = document.RootElement.Deserialize<Document>(JsonOptions);
            var configuration = loaded?.ToDomain() ?? ScrewdriverConfiguration.CreateDefault();
            return configuration.Validate().Count == 0
                ? configuration
                : ScrewdriverConfiguration.CreateDefault();
        }
        catch (JsonException)
        {
            return ScrewdriverConfiguration.CreateDefault();
        }
    }

    public static void Save(ScrewdriverConfiguration configuration, string path)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (configuration.Validate().Count > 0)
        {
            throw new InvalidOperationException("Screwdriver configuration is invalid.");
        }

        var json = JsonSerializer.Serialize(Document.FromDomain(configuration), JsonOptions);
        using (var probe = JsonDocument.Parse(json))
        {
            if (ContainsSensitiveProperty(probe.RootElement, out var sensitiveName))
            {
                throw new InvalidOperationException(
                    $"Refusing to persist screwdriver configuration because it contains sensitive key '{sensitiveName}'.");
            }
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
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

    private sealed class Document
    {
        public string? ToolModel { get; init; }

        public SpinBridgeDocument SpinBridge { get; init; } = new();

        public ScrewdriverConfiguration ToDomain() =>
            new(
                string.IsNullOrWhiteSpace(ToolModel)
                    ? ScrewdriverConfiguration.DefaultToolModel
                    : ToolModel,
                SpinBridge.Ip ?? string.Empty,
                SpinBridge.OpcUaPort,
                SpinBridge.StaleTimeoutMs <= 0
                    ? ScrewdriverConfiguration.DefaultStaleTimeoutMs
                    : SpinBridge.StaleTimeoutMs);

        public static Document FromDomain(ScrewdriverConfiguration configuration) =>
            new()
            {
                ToolModel = configuration.ToolModel,
                SpinBridge = new SpinBridgeDocument
                {
                    Ip = configuration.SpinBridgeIp,
                    OpcUaPort = configuration.OpcUaPort,
                    StaleTimeoutMs = configuration.StaleTimeoutMs
                }
            };
    }

    private sealed class SpinBridgeDocument
    {
        public string? Ip { get; init; }

        public int OpcUaPort { get; init; } = ScrewdriverConfiguration.DefaultOpcUaPort;

        public int StaleTimeoutMs { get; init; } = ScrewdriverConfiguration.DefaultStaleTimeoutMs;
    }
}
