using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Domain.Configuration;
using UrHmi.Infrastructure.Config;

namespace UrHmi.Infrastructure.Tests;

public sealed class JsonFileConfigStoreTests
{
    [Fact]
    public async Task Save_and_load_round_trip_without_secrets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urhmi-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonFileConfigStore(path, NullLogger<JsonFileConfigStore>.Instance);
            var original = HmiConfiguration.CreateDefault() with
            {
                Robot = HmiConfiguration.CreateDefault().Robot with { Ip = "192.168.1.10" }
            };

            await store.SaveAsync(original);
            var loaded = await store.LoadAsync();

            Assert.Equal("192.168.1.10", loaded.Robot.Ip);
            Assert.Equal(29999, loaded.Robot.Dashboard.Port);
            Assert.Equal(30004, loaded.Robot.Rtde.Port);
            Assert.Equal(30002, loaded.Robot.UrScript.Port);
            Assert.Equal(30001, loaded.Robot.UrScript.FallbackPort);
            Assert.Equal("simulator", loaded.Plc.Transport);
            Assert.False(loaded.SafeMode);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
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
    public void Sensitive_json_keys_are_detected()
    {
        using var document = JsonDocument.Parse("""{"robot":{"ip":"10.0.0.1"},"password":"nope"}""");

        var found = JsonFileConfigStore.ContainsSensitiveProperty(document.RootElement, out var name);

        Assert.True(found);
        Assert.Equal("password", name);
    }

    [Fact]
    public async Task Load_enters_safe_mode_when_file_contains_secrets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"urhmi-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"robot":{"ip":""},"password":"secret"}""");
            var store = new JsonFileConfigStore(path, NullLogger<JsonFileConfigStore>.Instance);

            var loaded = await store.LoadAsync();

            Assert.True(loaded.SafeMode);
            Assert.Equal(HmiConfiguration.DefaultRobotIp, loaded.Robot.Ip);
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
