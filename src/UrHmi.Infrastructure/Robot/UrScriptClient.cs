using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Ports;

namespace UrHmi.Infrastructure.Robot;

public sealed class UrScriptClient : IUrScriptClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly Encoding Text = Encoding.ASCII;

    private readonly HmiConfiguration _configuration;
    private readonly ILogger<UrScriptClient> _logger;

    public UrScriptClient(HmiConfiguration configuration, ILogger<UrScriptClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var host = _configuration.Robot.Ip?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("robot.ip is not set; URScript was not sent.");
        }

        var payload = script.EndsWith('\n') ? script : script + "\n";
        var ports = new[]
        {
            _configuration.Robot.UrScript.Port,
            _configuration.Robot.UrScript.FallbackPort
        }.Distinct();

        Exception? last = null;
        foreach (var port in ports)
        {
            try
            {
                await SendToAsync(host, port, payload, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("URScript sent to {Host}:{Port}.", host, port);
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or TimeoutException)
            {
                last = ex;
                _logger.LogWarning(ex, "URScript send to {Host}:{Port} failed.", host, port);
            }
        }

        throw new InvalidOperationException(
            $"URScript send to {host} failed on ports {string.Join(", ", ports)}.",
            last);
    }

    private static async Task SendToAsync(string host, int port, string payload, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient { NoDelay = true };
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(ConnectTimeout);
        await tcp.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
        await using var stream = tcp.GetStream();
        var bytes = Text.GetBytes(payload);
        await stream.WriteAsync(bytes, connectCts.Token).ConfigureAwait(false);
        await stream.FlushAsync(connectCts.Token).ConfigureAwait(false);
    }
}
