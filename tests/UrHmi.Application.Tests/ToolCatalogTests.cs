using UrHmi.Application.Tools;
using UrHmi.Domain.Configuration;
using UrHmi.Tools.Abstractions;

namespace UrHmi.Application.Tests;

public sealed class ToolCatalogTests
{
    [Fact]
    public void Enabled_filters_discovered_modules_by_configuration()
    {
        var catalog = new ToolCatalog(
            [new FakeTool("alpha", "Alpha"), new FakeTool("beta", "Beta")],
            new ToolsConfiguration("beta", ["beta"]));

        Assert.Equal(2, catalog.Discovered.Count);
        var enabled = Assert.Single(catalog.Enabled);
        Assert.Equal("beta", enabled.Id);
        Assert.Equal("beta", catalog.Selected?.Id);
    }

    [Fact]
    public void Unknown_selected_id_does_not_throw()
    {
        var catalog = new ToolCatalog(
            [new FakeTool("alpha", "Alpha")],
            new ToolsConfiguration("missing", ["missing"]));

        Assert.Empty(catalog.Enabled);
        Assert.Null(catalog.Selected);
        Assert.Null(catalog.FindEnabled("alpha"));
    }

    [Fact]
    public void Empty_enabled_list_exposes_no_modules()
    {
        var catalog = new ToolCatalog(
            [new FakeTool("alpha", "Alpha")],
            new ToolsConfiguration(null, []));

        Assert.Empty(catalog.Enabled);
        Assert.Null(catalog.Selected);
    }

    [Fact]
    public void Catalog_does_not_depend_on_a_screwdriver_type()
    {
        var application = typeof(ToolCatalog).Assembly;
        Assert.DoesNotContain(
            application.GetReferencedAssemblies(),
            name => name.Name is "UrHmi.Tools.Screwdriver");
        Assert.Null(application.GetType("UrHmi.Tools.Screwdriver.ScrewdriverToolModule"));
    }

    private sealed class FakeTool : IToolModule
    {
        public FakeTool(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public IReadOnlyList<string> RequiredPlcTags => [];

        public object CreateViewModel(IToolHost host) => new object();
    }
}
