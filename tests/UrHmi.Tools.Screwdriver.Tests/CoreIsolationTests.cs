namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class CoreIsolationTests
{
    [Fact]
    public void Core_projects_do_not_reference_the_screwdriver_plugin()
    {
        var root = FindRepoRoot();
        var coreProjects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(coreProjects);

        foreach (var project in coreProjects)
        {
            var text = File.ReadAllText(project);
            Assert.DoesNotContain("UrHmi.Tools.Screwdriver", text);
        }
    }

    [Fact]
    public void SpinBridge_ip_lives_in_plugin_config_not_core()
    {
        var root = FindRepoRoot();
        var pluginJson = File.ReadAllText(Path.Combine(root, "plugins", "UrHmi.Tools.Screwdriver", "screwdriver.json"));
        var hmiJson = File.ReadAllText(Path.Combine(root, "src", "UrHmi.App", "hmi.json"));
        var domainConfig = File.ReadAllText(Path.Combine(root, "src", "UrHmi.Domain", "Configuration", "HmiConfiguration.cs"));

        Assert.Contains("\"ip\": \"10.42.0.1\"", pluginJson, StringComparison.Ordinal);
        Assert.Contains("opcUaPort", pluginJson, StringComparison.Ordinal);
        Assert.Contains("staleTimeoutMs", pluginJson, StringComparison.Ordinal);
        Assert.DoesNotContain("spinBridge", hmiJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opcUaPort", hmiJson, StringComparison.Ordinal);
        Assert.DoesNotContain("SpinBridge", domainConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("opcUa", domainConfig, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plugin_lives_outside_src()
    {
        var root = FindRepoRoot();
        Assert.True(File.Exists(Path.Combine(root, "plugins", "UrHmi.Tools.Screwdriver", "UrHmi.Tools.Screwdriver.csproj")));
        Assert.True(File.Exists(Path.Combine(root, "plugins", "UrHmi.Tools.Screwdriver.Ui", "UrHmi.Tools.Screwdriver.Ui.csproj")));
        Assert.False(File.Exists(Path.Combine(root, "src", "UrHmi.Tools.Screwdriver", "UrHmi.Tools.Screwdriver.csproj")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "UrHmi.Tools.Screwdriver.Ui")));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UrHmi.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate UrHmi.slnx from " + AppContext.BaseDirectory);
    }
}
