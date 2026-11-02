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
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!_responses.TryGetValue(path, out var mapped))
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
