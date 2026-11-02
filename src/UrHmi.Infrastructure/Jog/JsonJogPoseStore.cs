using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrHmi.Domain.Ports;
using UrHmi.Infrastructure.Config;
using UrHmi.Domain.Robot;

namespace UrHmi.Infrastructure.Jog;

public sealed class JsonJogPoseStore : IJogPoseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILogger<JsonJogPoseStore> _logger;

    public JsonJogPoseStore(string path, ILogger<JsonJogPoseStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SavedJogPose>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return SavedJogPose.EmptySlots();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (JsonFileConfigStoreContainsSensitive(document.RootElement, out var sensitiveName))
            {
                _logger.LogWarning(
                    "Jog pose file {Path} contains sensitive key '{Key}'. Using empty slots.",
                    _path,
                    sensitiveName);
                return SavedJogPose.EmptySlots();
            }

            var loaded = JsonSerializer.Deserialize<JogFile>(json, JsonOptions);
            return Normalize(loaded?.Positions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Jog pose file {Path} is invalid. Using empty slots.", _path);
            return SavedJogPose.EmptySlots();
        }
    }

    public async Task SaveAsync(IReadOnlyList<SavedJogPose> poses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poses);
        var payload = new JogFile { Positions = Normalize(poses).Select(JogSlotJson.FromDomain).ToArray() };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using (var probe = JsonDocument.Parse(json))
        {
            if (JsonFileConfigStoreContainsSensitive(probe.RootElement, out var sensitiveName))
            {
                throw new InvalidOperationException(
                    $"Refusing to persist jog poses because they contain sensitive key '{sensitiveName}'.");
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

    private static IReadOnlyList<SavedJogPose> Normalize(IEnumerable<SavedJogPose>? poses)
    {
        var bySlot = (poses ?? []).Where(pose => pose.Slot is >= 1 and <= SavedJogPose.SlotCount)
            .GroupBy(pose => pose.Slot)
            .ToDictionary(group => group.Key, group => group.Last());
        return Enumerable.Range(1, SavedJogPose.SlotCount)
            .Select(slot => bySlot.TryGetValue(slot, out var pose) ? pose : new SavedJogPose(slot, null, null, null))
            .ToArray();
    }

    private static IReadOnlyList<SavedJogPose> Normalize(IEnumerable<JogSlotJson>? slots) =>
        Normalize(slots?.Select(slot => slot.ToDomain()));

    private static bool JsonFileConfigStoreContainsSensitive(JsonElement element, out string? name) =>
        JsonFileConfigStore.ContainsSensitiveProperty(element, out name);

    private sealed class JogFile
    {
        public JogSlotJson[]? Positions { get; init; }
    }

    private sealed class JogSlotJson
    {
        public int Slot { get; init; }
        public PoseJson? Tcp { get; init; }
        public JointJson? Joints { get; init; }
        public string? SavedUtc { get; init; }

        public SavedJogPose ToDomain()
        {
            DateTimeOffset? saved = null;
            if (DateTimeOffset.TryParse(SavedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                saved = parsed;
            }

            return new SavedJogPose(Slot, Tcp?.ToPose(), Joints?.ToPose(), saved);
        }

        public static JogSlotJson FromDomain(SavedJogPose pose) =>
            new()
            {
                Slot = pose.Slot,
                Tcp = pose.Tcp is { } tcp
                    ? new PoseJson { X = tcp.X, Y = tcp.Y, Z = tcp.Z, Rx = tcp.Rx, Ry = tcp.Ry, Rz = tcp.Rz }
                    : null,
                Joints = pose.Joints is { } joints
                    ? new JointJson { J1 = joints.J1, J2 = joints.J2, J3 = joints.J3, J4 = joints.J4, J5 = joints.J5, J6 = joints.J6 }
                    : null,
                SavedUtc = pose.SavedUtc?.ToString("O", CultureInfo.InvariantCulture)
            };
    }

    private sealed class PoseJson
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
        public double Rx { get; init; }
        public double Ry { get; init; }
        public double Rz { get; init; }

        public TcpPose ToPose() => new(X, Y, Z, Rx, Ry, Rz);
    }

    private sealed class JointJson
    {
        public double J1 { get; init; }
        public double J2 { get; init; }
        public double J3 { get; init; }
        public double J4 { get; init; }
        public double J5 { get; init; }
        public double J6 { get; init; }

        public JointPose ToPose() => new(J1, J2, J3, J4, J5, J6);
    }
}
