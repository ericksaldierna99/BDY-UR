using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using UrHmi.Domain;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Infrastructure.Robot;

public sealed class TcpDashboardClient : IRobotDashboardClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WelcomeTimeout = TimeSpan.FromSeconds(1);
    private static readonly Encoding Text = Encoding.ASCII;

    private readonly ILogger<TcpDashboardClient> _logger;
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly object _healthGate = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private ChannelHealth _health = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);

    public TcpDashboardClient(ILogger<TcpDashboardClient> logger)
    {
        _logger = logger;
    }

    public ChannelHealth Health
    {
        get
        {
            lock (_healthGate)
            {
                return _health;
            }
        }
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectUnlockedAsync().ConfigureAwait(false);
            SetHealth(new ChannelHealth(ChannelIds.RobotDashboard, LinkState.Connecting, null, null, null));

            var tcp = new TcpClient { NoDelay = true };
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeout);
            try
            {
                await tcp.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or TimeoutException)
            {
                tcp.Dispose();
                SetHealth(ChannelHealth.Disconnected(ChannelIds.RobotDashboard, ex.Message));
                throw new InvalidOperationException($"Dashboard Server connect to {host}:{port} failed: {ex.Message}", ex);
            }

            _tcp = tcp;
            _stream = tcp.GetStream();
            await TryConsumeWelcomeAsync(cancellationToken).ConfigureAwait(false);
            SetHealth(new ChannelHealth(
                ChannelIds.RobotDashboard,
                LinkState.Connected,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                null));
            _logger.LogInformation("Dashboard Server connected to {Host}:{Port}.", host, port);
        }
        finally
        {
            _io.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectUnlockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _io.Release();
        }
    }

    public Task<DashboardCommandResult> GetRobotModeAsync(CancellationToken cancellationToken = default) =>
        SendAsync("robotmode", raw => DashboardProtocol.ParseNamed(raw, "Robotmode"), cancellationToken);

    public Task<DashboardCommandResult> GetSafetyStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync("safetystatus", raw => DashboardProtocol.ParseNamed(raw, "Safetystatus"), cancellationToken);

    public Task<DashboardCommandResult> GetProgramStateAsync(CancellationToken cancellationToken = default) =>
        SendAsync("programState", DashboardProtocol.ParseProgramState, cancellationToken);

    public Task<DashboardCommandResult> GetRunningAsync(CancellationToken cancellationToken = default) =>
        SendAsync("running", DashboardProtocol.ParseRunning, cancellationToken);

    public Task<DashboardCommandResult> GetPolyscopeVersionAsync(CancellationToken cancellationToken = default) =>
        SendAsync("PolyscopeVersion", raw => DashboardProtocol.ParseNamed(raw, "URSoftware"), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _io.Dispose();
    }

    private async Task<DashboardCommandResult> SendAsync(
        string command,
        Func<string, DashboardCommandResult> parse,
        CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is null)
            {
                return DashboardCommandResult.Failed("Dashboard Server is not connected.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            var payload = Text.GetBytes(command + "\n");
            await _stream.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await _stream.FlushAsync(timeout.Token).ConfigureAwait(false);
            var line = await ReadLineAsync(_stream, timeout.Token).ConfigureAwait(false);
            if (line is null)
            {
                SetHealth(Health.WithLink(LinkState.Disconnected, "Dashboard Server closed the connection."));
                await DisconnectUnlockedAsync().ConfigureAwait(false);
                return DashboardCommandResult.Failed("Dashboard Server closed the connection.");
            }

            var result = parse(line);
            if (result.Success)
            {
                SetHealth(Health.WithRx(DateTimeOffset.UtcNow, LinkState.Connected));
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            SetHealth(Health.WithLink(LinkState.Disconnected, ex.Message));
            await DisconnectUnlockedAsync().ConfigureAwait(false);
            return DashboardCommandResult.Failed(ex.Message);
        }
        finally
        {
            _io.Release();
        }
    }

    private async Task TryConsumeWelcomeAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(WelcomeTimeout);
            var line = await ReadLineAsync(_stream, cts.Token).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            if (DashboardProtocol.IsWelcome(line))
            {
                _logger.LogDebug("Dashboard Server welcome: {Line}", line);
                return;
            }

            _logger.LogDebug("Dashboard Server first line was not a welcome banner: {Line}", line);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Controllers that skip the banner are acceptable.
        }
    }

    private async Task DisconnectUnlockedAsync()
    {
        var stream = _stream;
        var tcp = _tcp;
        _stream = null;
        _tcp = null;
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        tcp?.Dispose();
        if (Health.Link != LinkState.Disconnected)
        {
            SetHealth(new ChannelHealth(
                ChannelIds.RobotDashboard,
                LinkState.Disconnected,
                Health.LastRxUtc,
                Health.Age,
                Health.LastError));
        }
    }

    private void SetHealth(ChannelHealth health)
    {
        lock (_healthGate)
        {
            _health = health;
        }
    }

    internal static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : Text.GetString(buffer.ToArray());
            }

            if (one[0] == (byte)'\n')
            {
                break;
            }

            if (one[0] != (byte)'\r')
            {
                buffer.WriteByte(one[0]);
            }
        }

        return Text.GetString(buffer.ToArray());
    }
}
