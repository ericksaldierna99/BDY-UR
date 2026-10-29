using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using UrHmi.Domain;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;
using UrHmi.Infrastructure.Observables;

namespace UrHmi.Infrastructure.Robot;

public sealed class RtdeClient : IRtdeClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger<RtdeClient> _logger;
    private readonly ObserverBroadcaster<RtdeFrame> _frames = new();
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly object _healthGate = new();
    private ChannelHealth _health = ChannelHealth.Disconnected(ChannelIds.RobotRtde);
    private IReadOnlyList<string> _negotiatedKeys = [];
    private IReadOnlyList<string> _rejectedKeys = [];
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoop;

    public RtdeClient(ILogger<RtdeClient> logger)
    {
        _logger = logger;
        Frames = _frames;
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

    public IObservable<RtdeFrame> Frames { get; }

    public IReadOnlyList<string> NegotiatedKeys => _negotiatedKeys;

    public IReadOnlyList<string> RejectedKeys => _rejectedKeys;

    public async Task ConnectAsync(string host, int port, RtdeRecipe recipe, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(recipe);
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectUnlockedAsync().ConfigureAwait(false);
            SetHealth(new ChannelHealth(ChannelIds.RobotRtde, LinkState.Connecting, null, null, null));

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
                SetHealth(ChannelHealth.Disconnected(ChannelIds.RobotRtde, ex.Message));
                throw new InvalidOperationException($"RTDE connect to {host}:{port} failed: {ex.Message}", ex);
            }

            _tcp = tcp;
            _stream = tcp.GetStream();

            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(HandshakeTimeout);
            await HandshakeAsync(recipe, handshakeCts.Token).ConfigureAwait(false);

            _receiveCts = new CancellationTokenSource();
            var receiveToken = _receiveCts.Token;
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(receiveToken), CancellationToken.None);
            SetHealth(new ChannelHealth(ChannelIds.RobotRtde, LinkState.Connected, DateTimeOffset.UtcNow, TimeSpan.Zero, null));
            _logger.LogInformation(
                "RTDE connected to {Host}:{Port}. Negotiated {NegotiatedCount} key(s); rejected {RejectedCount}: {Rejected}.",
                host,
                port,
                _negotiatedKeys.Count,
                _rejectedKeys.Count,
                _rejectedKeys.Count == 0 ? "(none)" : string.Join(", ", _rejectedKeys));
        }
        catch
        {
            await DisconnectUnlockedAsync().ConfigureAwait(false);
            throw;
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

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _io.Dispose();
    }

    private async Task HandshakeAsync(RtdeRecipe recipe, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("RTDE stream is not open.");
        }

        await WriteAsync(RtdeBinaryCodec.PackProtocolVersionRequest(RtdeProtocol.ProtocolVersion2), cancellationToken)
            .ConfigureAwait(false);
        var version = await ReadPackageAsync(cancellationToken).ConfigureAwait(false);
        if (version.Type != RtdeProtocol.RequestProtocolVersion || !RtdeBinaryCodec.ReadProtocolVersionAccepted(version.Payload))
        {
            throw new InvalidOperationException("Controller rejected RTDE protocol version 2.");
        }

        await WriteAsync(RtdeBinaryCodec.PackSetupOutputs(recipe.FrequencyHz, recipe.OutputKeys), cancellationToken)
            .ConfigureAwait(false);
        var setup = await ReadPackageAsync(cancellationToken).ConfigureAwait(false);
        if (setup.Type != RtdeProtocol.SetupOutputs)
        {
            throw new InvalidOperationException($"Unexpected RTDE package 0x{setup.Type:X2} while setting up outputs.");
        }

        var layout = RtdeBinaryCodec.ParseSetupOutputs(setup.Payload, recipe.OutputKeys);
        if (layout.RecipeId == 0)
        {
            throw new InvalidOperationException("Controller rejected the RTDE output recipe (recipe id 0).");
        }

        _negotiatedKeys = layout.NegotiatedKeys;
        _rejectedKeys = layout.RejectedKeys;
        if (layout.RejectedKeys.Count > 0)
        {
            _logger.LogWarning(
                "RTDE keys not negotiated and will stay Unavailable: {Keys}",
                string.Join(", ", layout.RejectedKeys));
        }

        await WriteAsync(RtdeBinaryCodec.PackEmpty(RtdeProtocol.ControlStart), cancellationToken).ConfigureAwait(false);
        var start = await ReadPackageAsync(cancellationToken).ConfigureAwait(false);
        if (start.Type != RtdeProtocol.ControlStart || !RtdeBinaryCodec.ReadStartAccepted(start.Payload))
        {
            throw new InvalidOperationException("Controller rejected RTDE output start.");
        }

        _layout = layout;
    }

    private RtdeOutputLayout? _layout;

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null && _layout is not null)
            {
                var package = await ReadPackageAsync(cancellationToken).ConfigureAwait(false);
                if (package.Type == RtdeProtocol.TextMessage)
                {
                    continue;
                }

                if (package.Type != RtdeProtocol.DataPackage)
                {
                    continue;
                }

                var values = RtdeBinaryCodec.UnpackData(package.Payload, _layout);
                var now = DateTimeOffset.UtcNow;
                SetHealth(Health.WithRx(now, LinkState.Connected));
                _frames.Publish(new RtdeFrame(now, values));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on disconnect.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RTDE receive loop ended.");
            SetHealth(new ChannelHealth(
                ChannelIds.RobotRtde,
                LinkState.Disconnected,
                Health.LastRxUtc,
                Health.Age,
                ex.Message));
        }
    }

    private async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("RTDE stream is not open.");
        }

        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(byte Type, byte[] Payload)> ReadPackageAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("RTDE stream is not open.");
        }

        var header = await ReadExactAsync(_stream, RtdeProtocol.HeaderSize, cancellationToken).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (size < RtdeProtocol.HeaderSize)
        {
            throw new InvalidOperationException($"Invalid RTDE package size {size}.");
        }

        var type = header[2];
        var payloadLength = size - RtdeProtocol.HeaderSize;
        var payload = payloadLength == 0
            ? []
            : await ReadExactAsync(_stream, payloadLength, cancellationToken).ConfigureAwait(false);
        return (type, payload);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("RTDE connection closed while reading.");
            }

            offset += read;
        }

        return buffer;
    }

    private async Task DisconnectUnlockedAsync()
    {
        var receiveCts = _receiveCts;
        _receiveCts = null;
        if (receiveCts is not null)
        {
            await receiveCts.CancelAsync().ConfigureAwait(false);
        }

        var loop = _receiveLoop;
        _receiveLoop = null;
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Receive errors are reflected in Health.
            }
        }

        receiveCts?.Dispose();

        var stream = _stream;
        var tcp = _tcp;
        _stream = null;
        _tcp = null;
        _layout = null;
        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        tcp?.Dispose();
        if (Health.Link != LinkState.Disconnected)
        {
            SetHealth(new ChannelHealth(
                ChannelIds.RobotRtde,
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
}
