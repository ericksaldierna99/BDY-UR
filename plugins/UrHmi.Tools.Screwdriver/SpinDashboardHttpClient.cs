using System.Net.Http.Headers;
using System.Text.Json;

namespace UrHmi.Tools.Screwdriver;

public sealed class SpinDashboardHttpClient : ISpinHttpApi, IDisposable
{
    public static readonly string[] KnownPaths =
    [
        "/api/results/latest",
        "/api/result/latest",
        "/api/v1/results/latest",
        "/api/v1/result/latest",
        "/api/results",
        "/api/result",
        "/api/v1/results",
        "/results/latest",
        "/results",
        "/api/joining/results/latest",
        "/api/joining/results",
        "/api/tightening/results/latest",
        "/api/tightening/results"
    ];

    private static readonly string[] OpenApiPaths =
    [
        "/openapi.json",
        "/swagger.json",
        "/api/openapi.json",
        "/api/swagger.json"
    ];

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public SpinDashboardHttpClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(2);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _ownsClient = true;
    }

    internal SpinDashboardHttpClient(HttpClient http)
    {
        _http = http;
        _ownsClient = false;
    }

    public async Task<SpinHttpRead> ReadLatestAsync(string baseUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        var root = baseUrl.TrimEnd('/');
        var errors = new List<string>();
        var paths = new List<string>(KnownPaths);
        paths.AddRange(await DiscoverOpenApiResultPathsAsync(root, errors, cancellationToken).ConfigureAwait(false));

        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            var url = root + (path.StartsWith('/') ? path : "/" + path);
            try
            {
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{path} {(int)response.StatusCode}");
                    continue;
                }

                if (!TryParseJson(body, out var element))
                {
                    errors.Add($"{path} not JSON");
                    continue;
                }

                var pair = SpinBridgeResultDecoder.Decode(latestResult: SpinBridgeJson.ToObject(element));
                if (pair.TorqueNm is not null || pair.AngleDeg is not null)
                {
                    return new SpinHttpRead(pair.TorqueNm, pair.AngleDeg, path, null);
                }

                errors.Add($"{path} no Final Torque/Angle");
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        var detail = errors.Count == 0
            ? "no HTTP result paths were tried."
            : string.Join("; ", errors);
        if (detail.Length > 400)
        {
            detail = detail[..400] + "…";
        }

        return new SpinHttpRead(null, null, null, $"HTTP {root}: {detail}");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private async Task<IReadOnlyList<string>> DiscoverOpenApiResultPathsAsync(
        string root,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var found = new List<string>();
        foreach (var specPath in OpenApiPaths)
        {
            try
            {
                using var response = await _http.GetAsync(root + specPath, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!TryParseJson(body, out var document) ||
                    document.ValueKind != JsonValueKind.Object ||
                    !document.TryGetProperty("paths", out var paths))
                {
                    continue;
                }

                foreach (var path in paths.EnumerateObject())
                {
                    if (path.Name.Contains("result", StringComparison.OrdinalIgnoreCase)
                        && path.Value.ValueKind == JsonValueKind.Object
                        && path.Value.TryGetProperty("get", out _))
                    {
                        found.Add(path.Name);
                    }
                }

                if (found.Count > 0)
                {
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{specPath}: {ex.Message}");
            }
        }

        return found;
    }

    private static bool TryParseJson(string body, out JsonElement element)
    {
        element = default;
        var trimmed = body.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not '{' and not '[')
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
