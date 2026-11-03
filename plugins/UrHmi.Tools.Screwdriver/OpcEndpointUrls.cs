namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Canonical opc.tcp URLs for SpinBridge. The OPC stack's certificate factory
/// also requires a well-formed application URI (not the assembly name).
/// </summary>
internal static class OpcEndpointUrls
{
    public const string Scheme = "opc.tcp";
    public const string ApplicationName = "UrHmi.Screwdriver";
    public const string ApplicationUri = "urn:bdy:urhmi:screwdriver";
    public const string ProductUri = "urn:bdy:urhmi:screwdriver";
    public const int DefaultPort = 4840;

    public static string Canonical(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "OPC UA port must be between 1 and 65535.");
        }

        var trimmed = host.Trim();
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return Canonical(trimmed);
        }

        var colon = trimmed.LastIndexOf(':');
        if (colon > 0
            && colon < trimmed.Length - 1
            && int.TryParse(trimmed[(colon + 1)..], out var embeddedPort)
            && embeddedPort is >= 1 and <= 65535
            && trimmed.AsSpan(0, colon).IndexOf(':') < 0)
        {
            return Format(trimmed[..colon], embeddedPort);
        }

        return Format(trimmed.TrimEnd('/'), port);
    }

    public static string Canonical(string endpointUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointUrl);
        var trimmed = endpointUrl.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = $"{Scheme}://{trimmed.TrimStart('/')}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new UriFormatException(
                $"Invalid OPC UA endpoint '{endpointUrl}'. Use {Scheme}://10.42.0.1:{DefaultPort}.");
        }

        var port = uri.Port > 0 ? uri.Port : DefaultPort;
        return Format(uri.Host, port);
    }

    private static string Format(string host, int port) => $"{Scheme}://{host}:{port}";
}
