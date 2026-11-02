using System.Text.Json;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class SpinDashboardPathDiscoveryTests
{
    [Fact]
    public void Script_tags_and_modulepreload_are_collected()
    {
        const string html = """
            <html>
              <head>
                <link rel="modulepreload" href="/assets/index-abc.js">
                <script type="module" src="/assets/app.js"></script>
              </head>
            </html>
            """;

        var scripts = SpinDashboardPathDiscovery.ScriptSources(html);

        Assert.Contains("/assets/app.js", scripts);
        Assert.Contains("/assets/index-abc.js", scripts);
    }

    [Fact]
    public void Hash_route_is_not_an_http_path_but_fetch_targets_are()
    {
        const string js = """
            const router = { path: '#/results' };
            fetch('/api/v2/joining-results?limit=1');
            axios.get("/results");
            const ignored = '/assets/app.js';
            """;

        var paths = SpinDashboardPathDiscovery.ApiPaths(js);

        Assert.Contains("/api/v2/joining-results?limit=1", paths);
        Assert.Contains("/api/v2/joining-results", paths);
        Assert.Contains("/results", paths);
        Assert.DoesNotContain(paths, path => path.Contains('#', StringComparison.Ordinal));
        Assert.DoesNotContain("/assets/app.js", paths);
    }

    [Fact]
    public void Result_paths_rank_ahead_of_generic_api_paths()
    {
        var ranked = SpinDashboardPathDiscovery.Rank(
        [
            "/api/health",
            "/api/v2/joining-results",
            "/api/results?limit=1"
        ]);

        Assert.Equal("/api/results?limit=1", ranked[0]);
        Assert.Contains("/api/v2/joining-results", ranked);
    }

    [Fact]
    public void Vite_manifest_files_are_collected()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "index.html": {
                "file": "assets/index-xyz.js",
                "isEntry": true
              }
            }
            """);

        var files = SpinDashboardPathDiscovery.ManifestScripts(document.RootElement);
        Assert.Equal("assets/index-xyz.js", Assert.Single(files));
        Assert.Equal("/assets/index-xyz.js", SpinDashboardPathDiscovery.ResolveAgainst("http://10.42.0.1", files[0]));
    }

    [Fact]
    public void Rooted_script_src_is_not_treated_as_a_file_uri()
    {
        Assert.Equal(
            "/assets/index-results.js",
            SpinDashboardPathDiscovery.ResolveAgainst("http://10.42.0.1:80", "/assets/index-results.js"));
    }
}
