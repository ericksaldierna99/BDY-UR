namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// TCP probe for OPC UA ports. Tests fake this so they never open sockets.
/// </summary>
public interface IOpcPortProbe
{
    Task<bool> IsOpenAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken);
}
