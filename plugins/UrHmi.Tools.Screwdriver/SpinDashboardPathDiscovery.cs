using System.Text.Json;
using System.Text.RegularExpressions;

namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Pulls script URLs and XHR/fetch paths out of the Spin Dashboard SPA
/// (hash route <c>#/results</c> is client-side; JSON lives on whatever the bundle calls).
/// </summary>
internal static class SpinDashboardPathDiscovery
{
    private static readonly Regex ScriptSrc = new(
        """<script\b[^>]*\bsrc\s*=\s*["']([^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LinkHref = new(
        """<link\b[^>]*?(?:rel\s*=\s*["'](?:modulepreload|preload|module)["'][^>]*?href\s*=\s*["']([^"']+)["']|href\s*=\s*["']([^"']+)["'][^>]*?rel\s*=\s*["'](?:modulepreload|preload)["'])""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImportFrom = new(
        """(?:import\s*\(\s*|from\s+)["']([^"']+\.m?js(?:\?[^"']*)?)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FetchCall = new(
        """(?:\bfetch|\baxios\.(?:get|post|put|patch)|\bhttp\.(?:get|post)|\.open)\s*\(\s*["'`]([^"'`]+)["'`]""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuotedApi = new(
        """["'`](\/(?:api|rest|backend|data|graphql|v\d+)[^"'`\s]*)["'`]""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuotedResult = new(
        """["'`](\/[^"'`\s]*(?:result|joining|tightening|torque|angle)[^"'`\s]*)["'`]""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ScriptExtensions = [".js", ".mjs"];
    private static readonly string[] RejectedExtensions =
        [".css", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".woff", ".woff2", ".ttf", ".map", ".ico", ".webp"];

    public static IReadOnlyList<string> ScriptSources(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var found = new List<string>();
        foreach (Match match in ScriptSrc.Matches(html))
        {
            AddUnique(found, match.Groups[1].Value);
        }

        foreach (Match match in LinkHref.Matches(html))
        {
            var href = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (LooksLikeScript(href))
            {
                AddUnique(found, href);
            }
        }

        foreach (Match match in ImportFrom.Matches(html))
        {
            AddUnique(found, match.Groups[1].Value);
        }

        return found;
    }

    public static IReadOnlyList<string> ManifestScripts(JsonElement document)
    {
        var found = new List<string>();
        CollectManifestFiles(document, found);
        return found;
    }

    public static IReadOnlyList<string> ApiPaths(string javaScript)
    {
        if (string.IsNullOrWhiteSpace(javaScript))
        {
            return [];
        }

        var found = new List<string>();
        foreach (Match match in FetchCall.Matches(javaScript))
        {
            AddApiCandidate(found, match.Groups[1].Value);
        }

        foreach (Match match in QuotedApi.Matches(javaScript))
        {
            AddApiCandidate(found, match.Groups[1].Value);
        }

        foreach (Match match in QuotedResult.Matches(javaScript))
        {
            AddApiCandidate(found, match.Groups[1].Value);
        }

        return Rank(found);
    }

    public static IReadOnlyList<string> Rank(IEnumerable<string> paths)
    {
        return paths
            .SelectMany(Expand)
            .Where(path => !string.IsNullOrWhiteSpace(path) && IsTighteningResultPath(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Score)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsTighteningResultPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsIgnoredFallbackPath(path))
        {
            return false;
        }

        var lower = path.ToLowerInvariant();
        return lower.Contains("result", StringComparison.Ordinal)
            || lower.Contains("joining", StringComparison.Ordinal)
            || lower.Contains("tightening", StringComparison.Ordinal)
            || lower.Contains("torque", StringComparison.Ordinal)
            || lower.Contains("spin_database", StringComparison.Ordinal)
            || lower.Contains("spin_screw", StringComparison.Ordinal);
    }

    public static bool IsIgnoredFallbackPath(string path)
    {
        var lower = path.ToLowerInvariant();
        return lower.Contains("/api/system", StringComparison.Ordinal)
            || lower.Contains("wifi", StringComparison.Ordinal)
            || lower.Contains("delete", StringComparison.Ordinal)
            || lower.Contains("openbalena", StringComparison.Ordinal)
            || lower.Contains("is-new-update", StringComparison.Ordinal)
            || lower.Contains("connect-to", StringComparison.Ordinal)
            || lower.Contains("disable-wifi", StringComparison.Ordinal);
    }

    public static bool PreferJsonPost(string path)
    {
        var cut = path.Split('?', 2)[0];
        return cut.Contains("/spin_database/", StringComparison.OrdinalIgnoreCase)
            || cut.Contains("/spin_screw_process/", StringComparison.OrdinalIgnoreCase)
            || cut.Contains("get_result", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveAgainst(string root, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var trimmed = reference.Trim();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return ResolveAgainst(root, "http:" + trimmed);
        }

        if (trimmed.StartsWith('/'))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            return string.IsNullOrEmpty(absolute.Query)
                ? absolute.AbsolutePath
                : absolute.PathAndQuery;
        }

        return "/" + trimmed.TrimStart('.', '/');
    }

    private static void CollectManifestFiles(JsonElement element, List<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if ((property.Name.Equals("file", StringComparison.OrdinalIgnoreCase)
                            || property.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var file = property.Value.GetString();
                        if (LooksLikeScript(file))
                        {
                            AddUnique(found, file!);
                        }
                    }

                    CollectManifestFiles(property.Value, found);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectManifestFiles(item, found);
                }

                break;
            case JsonValueKind.String:
                var text = element.GetString();
                if (LooksLikeScript(text))
                {
                    AddUnique(found, text!);
                }

                break;
        }
    }

    private static IEnumerable<string> Expand(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            yield break;
        }

        yield return trimmed;
        var cut = trimmed.Split('?', 2)[0];
        if (!string.Equals(cut, trimmed, StringComparison.Ordinal))
        {
            yield return cut;
        }

        if (cut.EndsWith("/results", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains("limit=", StringComparison.OrdinalIgnoreCase))
        {
            yield return cut + "?limit=1";
        }
    }

    private static int Score(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("result", StringComparison.Ordinal)
            && (lower.Contains("latest", StringComparison.Ordinal) || lower.Contains("limit=", StringComparison.Ordinal)))
        {
            return 0;
        }

        if (lower.Contains("result", StringComparison.Ordinal))
        {
            return 1;
        }

        if (lower.Contains("joining", StringComparison.Ordinal)
            || lower.Contains("tightening", StringComparison.Ordinal)
            || lower.Contains("torque", StringComparison.Ordinal))
        {
            return 2;
        }

        if (lower.Contains("/api/", StringComparison.Ordinal) || lower.Contains("graphql", StringComparison.Ordinal))
        {
            return 3;
        }

        return 4;
    }

    private static void AddApiCandidate(List<string> found, string raw)
    {
        foreach (var normalized in NormalizeApi(raw))
        {
            AddUnique(found, normalized);
        }
    }

    private static IEnumerable<string> NormalizeApi(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var trimmed = raw.Trim();
        var hash = trimmed.IndexOf('#');
        if (hash == 0)
        {
            yield break;
        }

        if (hash > 0)
        {
            trimmed = trimmed[..hash];
        }

        if (trimmed.StartsWith("${", StringComparison.Ordinal) || trimmed.Contains("${", StringComparison.Ordinal))
        {
            var slash = trimmed.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (slash < 0)
            {
                slash = trimmed.IndexOf("/result", StringComparison.OrdinalIgnoreCase);
            }

            if (slash >= 0)
            {
                trimmed = trimmed[slash..];
            }
            else
            {
                yield break;
            }
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            trimmed = string.IsNullOrEmpty(absolute.Query)
                ? absolute.AbsolutePath
                : absolute.PathAndQuery;
        }

        if (!trimmed.StartsWith('/'))
        {
            if (trimmed.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("results", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("graphql", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "/" + trimmed;
            }
            else
            {
                yield break;
            }
        }

        if (LooksLikeAsset(trimmed))
        {
            yield break;
        }

        trimmed = trimmed.TrimEnd('?', '&', '=');
        if (trimmed.Length < 2)
        {
            yield break;
        }

        yield return trimmed;
    }

    private static bool LooksLikeScript(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var path = href.Split('?', 2)[0].Split('#', 2)[0];
        return ScriptExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeAsset(string path)
    {
        var cut = path.Split('?', 2)[0];
        return RejectedExtensions.Any(ext => cut.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            || ScriptExtensions.Any(ext => cut.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddUnique(List<string> found, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (!found.Contains(trimmed, StringComparer.Ordinal))
        {
            found.Add(trimmed);
        }
    }
}
