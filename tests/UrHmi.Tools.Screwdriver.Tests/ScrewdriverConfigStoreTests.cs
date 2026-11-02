using System.Text.Json;
using UrHmi.Domain.Configuration;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class ScrewdriverConfigStoreTests
{
    [Fact]
    public void Default_persists_spinbridge_ip_and_opcua_endpoint()
    {
        var configuration = ScrewdriverConfiguration.CreateDefault();

        Assert.Equal("10.42.0.1", configuration.SpinBridgeIp);
        Assert.Equal(4840, configuration.OpcUaPort);
        Assert.Equal(80, configuration.HttpPort);
        Assert.Equal("opc.tcp://10.42.0.1:4840", configuration.OpcUaEndpoint);
        Assert.Equal("http://10.42.0.1:80", configuration.HttpBase);
        Assert.Equal("SDV35", configuration.ToolModel);
        Assert.Equal(2000, configuration.StaleTimeoutMs);
        Assert.Empty(configuration.Validate());
    }

    [Fact]
    public void Round_trip_does_not_store_secrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "urhmi-sdv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ScrewdriverConfiguration.FileName);
            ScrewdriverConfigStore.Save(ScrewdriverConfiguration.CreateDefault(), path);
            var loaded = ScrewdriverConfigStore.LoadOrCreate(directory);
            var json = File.ReadAllText(path);

            Assert.Equal("10.42.0.1", loaded.SpinBridgeIp);
            Assert.Equal("opc.tcp://10.42.0.1:4840", loaded.OpcUaEndpoint);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            Assert.False(ScrewdriverConfigStore.ContainsSensitiveProperty(document.RootElement, out _));
            Assert.False(SensitiveConfigurationKeys.IsSensitive("ip"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void File_with_secrets_falls_back_to_defaults_without_saving_them()
    {
        var directory = Path.Combine(Path.GetTempPath(), "urhmi-sdv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ScrewdriverConfiguration.FileName);
            File.WriteAllText(path, """{"spinBridge":{"ip":"10.0.0.1"},"password":"nope"}""");

            var loaded = ScrewdriverConfigStore.LoadOrCreate(directory);

            Assert.Equal(ScrewdriverConfiguration.DefaultSpinBridgeIp, loaded.SpinBridgeIp);
            Assert.Contains("password", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_rejects_sensitive_keys()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sdv-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                var json = """{"toolModel":"SDV35","password":"x","spinBridge":{"ip":"10.42.0.1","opcUaPort":4840}}""";
                using var document = JsonDocument.Parse(json);
                if (ScrewdriverConfigStore.ContainsSensitiveProperty(document.RootElement, out var name))
                {
                    throw new InvalidOperationException($"sensitive:{name}");
                }
            });
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
