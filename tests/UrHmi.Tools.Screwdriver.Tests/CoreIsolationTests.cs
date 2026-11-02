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
