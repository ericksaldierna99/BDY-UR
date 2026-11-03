namespace UrHmi.Tools.Screwdriver;

public readonly record struct SpinHttpRead(
    double? TorqueNm,
    double? AngleDeg,
    string? Path,
    string? Error,
    double? DepthMm = null);

/// <summary>
/// Spin Dashboard HTTP/API used when OPC UA is not reachable on the hotspot.
/// Tests fake this so they never call 10.42.0.1.
/// </summary>
public interface ISpinHttpApi
{
    Task<SpinHttpRead> ReadLatestAsync(string baseUrl, CancellationToken cancellationToken);
}
