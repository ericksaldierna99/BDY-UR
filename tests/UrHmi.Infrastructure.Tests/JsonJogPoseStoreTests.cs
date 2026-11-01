using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Domain.Robot;
using UrHmi.Infrastructure.Jog;

namespace UrHmi.Infrastructure.Tests;

public sealed class JsonJogPoseStoreTests
{
    [Fact]
    public async Task Missing_file_returns_ten_empty_slots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urhmi-jog-{Guid.NewGuid():N}.json");
        var store = new JsonJogPoseStore(path, NullLogger<JsonJogPoseStore>.Instance);

        var loaded = await store.LoadAsync();

        Assert.Equal(SavedJogPose.SlotCount, loaded.Count);
        Assert.All(loaded, pose => Assert.False(pose.HasPose));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Save_and_load_round_trip_without_secrets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urhmi-jog-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonJogPoseStore(path, NullLogger<JsonJogPoseStore>.Instance);
            var tcp = new TcpPose(0.4, -0.15, 0.25, 0, 3.14, 0);
            var joints = new JointPose(0.1, -0.2, 0.3, 0.4, 0.5, 0.6);
            var savedUtc = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
            var slots = SavedJogPose.EmptySlots().ToArray();
            slots[6] = new SavedJogPose(7, tcp, joints, savedUtc);

            await store.SaveAsync(slots);
            var loaded = await store.LoadAsync();

            Assert.Equal(10, loaded.Count);
            Assert.Equal(tcp, loaded[6].Tcp);
            Assert.Equal(joints, loaded[6].Joints);
            Assert.Equal(savedUtc, loaded[6].SavedUtc);
            Assert.False(loaded[0].HasPose);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task File_with_sensitive_keys_loads_as_empty_slots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urhmi-jog-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"positions":[],"password":"nope"}""");
            var store = new JsonJogPoseStore(path, NullLogger<JsonJogPoseStore>.Instance);

            var loaded = await store.LoadAsync();

            Assert.Equal(10, loaded.Count);
            Assert.All(loaded, pose => Assert.False(pose.HasPose));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
