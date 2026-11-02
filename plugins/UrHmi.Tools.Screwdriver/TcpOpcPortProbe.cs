using System.Net.Sockets;

namespace UrHmi.Tools.Screwdriver;

public sealed class TcpOpcPortProbe : IOpcPortProbe
{
    public async Task<bool> IsOpenAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
