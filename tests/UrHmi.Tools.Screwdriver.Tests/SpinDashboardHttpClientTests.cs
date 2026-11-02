using System.Net;
using System.Text;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class SpinDashboardHttpClientTests
{
    [Fact]
    public async Task Known_results_path_maps_final_torque_and_angle()
    {
        var handler = new MapHandler
        {
            ["/api/results/latest"] = (
                200,
                """{"name":"Final Torque","measuredValue":0,"angle":12}""",
                "application/json")
        };
        // The first known path is /api/results/latest; give it both values.
        handler["/api/results/latest"] = (
            200,
            """
            {
              "overallResultValues": [
                { "name": "Final Torque", "measuredValue": 0 },
                { "name": "Final Angle", "measuredValue": 12 }
              ]
            }
            """,
            "application/json");

        using var client = new SpinDashboardHttpClient(handler);
        var read = await client.ReadLatestAsync("http://10.42.0.1:80", CancellationToken.None);

        Assert.Equal(0, read.TorqueNm);
        Assert.Equal(12, read.AngleDeg);
        Assert.Equal("/api/results/latest", read.Path);
        Assert.Null(read.Error);
    }

    [Fact]
    public async Task Missing_json_results_return_an_error()
    {
        using var client = new SpinDashboardHttpClient(new MapHandler());
        var read = await client.ReadLatestAsync("http://10.42.0.1:80", CancellationToken.None);

        Assert.Null(read.TorqueNm);
        Assert.Null(read.AngleDeg);
        Assert.Contains("HTTP", read.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Spa_bundle_fetch_path_is_used_instead_of_guessed_latest_url()
    {
        var handler = new MapHandler
        {
            ["/"] = (
                200,
                """
                <html>
                  <head>
                    <script type="module" src="/assets/index-results.js"></script>
                  </head>
                  <body>Spin Dashboard</body>
                </html>
                """,
                "text/html"),
            ["/assets/index-results.js"] = (
                200,
                """
                const route = '#/results';
                fetch('/api/v2/joining-results?limit=1').then(r => r.json());
                """,
                "application/javascript"),
            ["/api/results/latest"] = (404, "", "text/plain")
        };
        const string resultsJson = """
            {
              "results": [
                {
                  "resultEvaluation": "NOT_OK",
                  "overallResultValues": [
                    { "name": "Final Torque", "measuredValue": 0 },
                    { "name": "Final Angle", "measuredValue": 1.5 }
                  ]
                }
              ]
            }
            """;
        handler["/api/v2/joining-results"] = (200, resultsJson, "application/json");
        handler["/api/v2/joining-results?limit=1"] = (200, resultsJson, "application/json");

        using var client = new SpinDashboardHttpClient(handler);
        var read = await client.ReadLatestAsync("http://10.42.0.1:80", CancellationToken.None);

        Assert.Equal(0, read.TorqueNm);
        Assert.Equal(1.5, read.AngleDeg);
        Assert.Contains("joining-results", read.Path, StringComparison.Ordinal);
        Assert.Null(read.Error);
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (int Status, string Body, string Type)> _responses =
            new(StringComparer.OrdinalIgnoreCase);

        public (int Status, string Body, string Type) this[string path]
        {
            set => _responses[path] = value;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            var path = uri is null
                ? string.Empty
                : string.IsNullOrEmpty(uri.Query)
                    ? uri.AbsolutePath
                    : uri.PathAndQuery;
            if (!_responses.TryGetValue(path, out var mapped)
                && !_responses.TryGetValue(uri?.AbsolutePath ?? string.Empty, out mapped))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain")
                });
            }

            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)mapped.Status)
            {
                Content = new StringContent(mapped.Body, Encoding.UTF8, mapped.Type)
            });
        }
    }
}
