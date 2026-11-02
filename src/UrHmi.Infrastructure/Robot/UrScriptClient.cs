using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Ports;

namespace UrHmi.Infrastructure.Robot;

public sealed class UrScriptClient : IUrScriptClient, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly Encoding Text = Encoding.ASCII;

    private readonly HmiConfiguration _configuration;
    private readonly ILogger<UrScriptClient> _logger;
    private readonly object _programGate = new();
    private TcpClient? _programClient;

    public UrScriptClient(HmiConfiguration configuration, ILogger<UrScriptClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendAsync(string script, CancellationToken cancellationToken = default) =>
        SendCoreAsync(script, primaryFirst: false, keepOpen: false, cancellationToken);

    public Task SendProgramAsync(string script, CancellationToken cancellationToken = default) =>
        SendCoreAsync(script, primaryFirst: true, keepOpen: true, cancellationToken);

    public void Dispose() => ReleaseProgramClient();

    private async Task SendCoreAsync(string script, bool primaryFirst, bool keepOpen, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var host = _configuration.Robot.Ip?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("robot.ip is not set; URScript was not sent.");
        }

        var payload = script.EndsWith('\n') ? script : script + "\n";
        var secondary = _configuration.Robot.UrScript.Port;
        var primary = _configuration.Robot.UrScript.FallbackPort;
        var ports = (primaryFirst ? new[] { primary, secondary } : new[] { secondary, primary }).Distinct();

        ReleaseProgramClient();

        Exception? last = null;
        foreach (var port in ports)
        {
            try
            {
                var tcp = await SendToAsync(host, port, payload, keepOpen, cancellationToken).ConfigureAwait(false);
                if (keepOpen && tcp is not null)
                {
                    RetainProgramClient(tcp);
                }

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

    private static async Task<TcpClient?> SendToAsync(
        string host,
        int port,
        string payload,
        bool keepOpen,
        CancellationToken cancellationToken)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();
            var bytes = Text.GetBytes(payload);
            await stream.WriteAsync(bytes, connectCts.Token).ConfigureAwait(false);
            await stream.FlushAsync(connectCts.Token).ConfigureAwait(false);
            if (keepOpen)
            {
                return tcp;
            }

            tcp.Dispose();
            return null;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    private void RetainProgramClient(TcpClient tcp)
    {
        lock (_programGate)
        {
            _programClient?.Dispose();
            _programClient = tcp;
        }
    }

    private void ReleaseProgramClient()
    {
        lock (_programGate)
        {
            _programClient?.Dispose();
            _programClient = null;
        }
    }
}
