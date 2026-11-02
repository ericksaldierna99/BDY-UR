using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Infrastructure.Plugins;

namespace UrHmi.Infrastructure.Tests;

public sealed class PluginDirectoryLoaderTests
{
    [Fact]
    public void Missing_directory_returns_empty_result()
    {
        var loader = new PluginDirectoryLoader(NullLogger<PluginDirectoryLoader>.Instance);
        var missing = Path.Combine(Path.GetTempPath(), "urhmi-plugins-" + Guid.NewGuid().ToString("N"));

        var result = loader.Load(missing);

        Assert.Empty(result.Modules);
        Assert.Empty(result.Views);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void Corrupt_plugin_dll_is_reported_and_does_not_throw()
    {
        var directory = Path.Combine(Path.GetTempPath(), "urhmi-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "UrHmi.Tools.Broken.dll"), "not a managed plugin");
            var loader = new PluginDirectoryLoader(NullLogger<PluginDirectoryLoader>.Instance);

            var result = loader.Load(directory);

            Assert.Empty(result.Modules);
            Assert.Empty(result.Views);
            var failure = Assert.Single(result.Failures);
            Assert.Contains("UrHmi.Tools.Broken.dll", failure.AssemblyPath, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Abstractions_assembly_is_not_treated_as_a_plugin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "urhmi-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "UrHmi.Tools.Abstractions.dll"), "skip");
            var loader = new PluginDirectoryLoader(NullLogger<PluginDirectoryLoader>.Instance);

            var result = loader.Load(directory);

            Assert.Empty(result.Modules);
            Assert.Empty(result.Views);
            Assert.Empty(result.Failures);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
