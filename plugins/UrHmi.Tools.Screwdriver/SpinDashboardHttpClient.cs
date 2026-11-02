using System.Net.Http.Headers;
using System.Text.Json;

namespace UrHmi.Tools.Screwdriver;

public sealed class SpinDashboardHttpClient : ISpinHttpApi, IDisposable
{
    public static readonly string[] KnownPaths =
    [
        "/api/results",
        "/api/result",
        "/api/v1/results",
        "/api/v1/result",
        "/api/v1/results/latest",
        "/api/v1/result/latest",
        "/api/results/latest",
        "/api/result/latest",
        "/results/latest",
        "/results",
        "/api/joining/results/latest",
        "/api/joining/results",
        "/api/tightening/results/latest",
        "/api/tightening/results",
        "/spin_database/get_results_filtered",
        "/spin_database/get_result",
        "/spin_screw_process/program_result"
    ];

    private static readonly string[] JsonPostBodies =
    [
        """{"limit":1}""",
        "{}",
        """{"ok":true,"nok":true,"limit":1}"""
    ];

    private static readonly string[] OpenApiPaths =
    [
        "/openapi.json",
        "/swagger.json",
        "/api/openapi.json",
        "/api/swagger.json"
    ];

    private static readonly string[] SpaDocuments =
    [
        "/",
        "/index.html"
    ];

    private static readonly string[] ManifestPaths =
    [
        "/manifest.json",
        "/asset-manifest.json",
        "/.vite/manifest.json"
    ];

    private const int MaxScriptFiles = 8;
    private const int MaxProbePaths = 20;
    private const int MaxBodyChars = 2_000_000;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly object _cacheGate = new();
    private string? _cachedWorkingPath;
    private bool _cachedPost;
    private IReadOnlyList<string>? _cachedDiscoveredPaths;
    private bool _discoveryAttempted;

    public SpinDashboardHttpClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = TimeSpan.FromSeconds(2);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
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
        string? cachedWorking;
        bool cachedPost;
        IReadOnlyList<string>? cachedDiscovered;
        bool discoveryAttempted;
        lock (_cacheGate)
        {
            cachedWorking = _cachedWorkingPath;
            cachedPost = _cachedPost;
            cachedDiscovered = _cachedDiscoveredPaths;
            discoveryAttempted = _discoveryAttempted;
        }

        if (cachedWorking is not null)
        {
            var cached = await TryReadPathAsync(root, cachedWorking, cachedPost, cancellationToken)
                .ConfigureAwait(false);
            if (cached is { } hit)
            {
                return hit;
            }

            lock (_cacheGate)
            {
                if (_cachedWorkingPath == cachedWorking)
                {
                    _cachedWorkingPath = null;
                    _cachedPost = false;
                }
            }
        }

        var paths = new List<string>();
        if (discoveryAttempted)
        {
            if (cachedDiscovered is { Count: > 0 })
            {
                paths.AddRange(cachedDiscovered);
            }
        }
        else
        {
            var discovered = await DiscoverSpaResultPathsAsync(root, errors, cancellationToken)
                .ConfigureAwait(false);
            var openApi = await DiscoverOpenApiResultPathsAsync(root, errors, cancellationToken)
                .ConfigureAwait(false);
            var combined = discovered.Concat(openApi).Distinct(StringComparer.Ordinal).ToArray();
            lock (_cacheGate)
            {
                _cachedDiscoveredPaths = combined;
                _discoveryAttempted = true;
            }

            paths.AddRange(combined);
        }

        paths.AddRange(KnownPaths);

        var tried = 0;
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            if (!SpinDashboardPathDiscovery.IsTighteningResultPath(path))
            {
                continue;
            }

            if (tried >= MaxProbePaths)
            {
                break;
            }

            tried++;
            var preferPost = SpinDashboardPathDiscovery.PreferJsonPost(path);
            var order = preferPost ? new[] { true, false } : new[] { false };
            foreach (var post in order)
            {
                var read = await TryReadPathAsync(root, path, post, cancellationToken)
                    .ConfigureAwait(false);
                if (read is { } probed)
                {
                    return probed;
                }
            }
        }

        return new SpinHttpRead(
            null,
            null,
            null,
            $"HTTP fallback: no JSON tightening results at {root}.");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private async Task<IReadOnlyList<string>> DiscoverSpaResultPathsAsync(
        string root,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var scripts = new List<string>();
        var found = new List<string>();

        foreach (var page in SpaDocuments)
        {
            var body = await TryGetBodyAsync(root + page, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                continue;
            }

            scripts.AddRange(SpinDashboardPathDiscovery.ScriptSources(body));
            found.AddRange(SpinDashboardPathDiscovery.ApiPaths(body));
        }

        foreach (var manifestPath in ManifestPaths)
        {
            var body = await TryGetBodyAsync(root + manifestPath, cancellationToken).ConfigureAwait(false);
            if (body is null || !TryParseJson(body, out var document))
            {
                continue;
            }

            scripts.AddRange(SpinDashboardPathDiscovery.ManifestScripts(document));
            found.AddRange(SpinDashboardPathDiscovery.ApiPaths(body));
        }

        var fetched = 0;
        foreach (var src in scripts.Distinct(StringComparer.Ordinal))
        {
            if (fetched >= MaxScriptFiles || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var resolved = SpinDashboardPathDiscovery.ResolveAgainst(root, src);
            if (resolved is null)
            {
                continue;
            }

            var url = resolved.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? resolved
                : root + (resolved.StartsWith('/') ? resolved : "/" + resolved);
            var js = await TryGetBodyAsync(url, cancellationToken).ConfigureAwait(false);
            if (js is null)
            {
                continue;
            }

            fetched++;
            found.AddRange(SpinDashboardPathDiscovery.ApiPaths(js));
        }

        var ranked = SpinDashboardPathDiscovery.Rank(found);
        if (ranked.Count == 0 && fetched == 0 && scripts.Count == 0)
        {
            errors.Add("SPA #/results: no script or API paths found in index.html");
        }

        return ranked;
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

    private async Task<SpinHttpRead?> TryReadPathAsync(
        string root,
        string path,
        bool post,
        CancellationToken cancellationToken)
    {
        var url = root + (path.StartsWith('/') ? path : "/" + path);
        if (post)
        {
            foreach (var payload in JsonPostBodies)
            {
                var read = await TrySendAsync(url, path, payload, cancellationToken)
                    .ConfigureAwait(false);
                if (read is { } hit)
                {
                    return hit;
                }
            }

            return null;
        }

        return await TrySendAsync(url, path, postBody: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SpinHttpRead?> TrySendAsync(
        string url,
        string path,
        string? postBody,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = postBody is null
                ? await _http.GetAsync(url, cancellationToken).ConfigureAwait(false)
                : await _http.PostAsync(
                        url,
                        new StringContent(postBody, System.Text.Encoding.UTF8, "application/json"),
                        cancellationToken)
                    .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (!TryParseJson(body, out var element))
            {
                return null;
            }

            var pair = SpinBridgeResultDecoder.Decode(latestResult: SpinBridgeJson.ToObject(element));
            if (pair.TorqueNm is not null || pair.AngleDeg is not null)
            {
                lock (_cacheGate)
                {
                    _cachedWorkingPath = path;
                    _cachedPost = postBody is not null;
                }

                return new SpinHttpRead(pair.TorqueNm, pair.AngleDeg, path, null);
            }

            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<string?> TryGetBodyAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return body.Length <= MaxBodyChars ? body : body[..MaxBodyChars];
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
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
