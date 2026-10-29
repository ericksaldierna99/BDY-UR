using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Domain;
using UrHmi.Domain.Robot;
using UrHmi.Infrastructure.Robot;

namespace UrHmi.Infrastructure.Tests;

public sealed class RtdeClientTests
{
    [Fact]
    public async Task Negotiates_recipe_streams_a_frame_and_drops_not_found_keys()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeAsync(listener);

        var received = new TaskCompletionSource<RtdeFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new RtdeClient(NullLogger<RtdeClient>.Instance);
        using var sub = client.Frames.Subscribe(new FrameObserver(received));
        var recipe = new RtdeRecipe(
            "default",
            [DefaultRtdeOutputs.Timestamp, DefaultRtdeOutputs.ActualTcpPose, DefaultRtdeOutputs.RobotMode],
            10);

        await client.ConnectAsync(IPAddress.Loopback.ToString(), port, recipe);
        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await client.DisconnectAsync();
        listener.Stop();
        await server;

        Assert.Equal(LinkState.Disconnected, client.Health.Link);
        Assert.Equal([DefaultRtdeOutputs.Timestamp, DefaultRtdeOutputs.RobotMode], client.NegotiatedKeys);
        Assert.Equal([DefaultRtdeOutputs.ActualTcpPose], client.RejectedKeys);
        Assert.True(frame.Values.ContainsKey(DefaultRtdeOutputs.Timestamp));
        Assert.Equal(7, Assert.IsType<int>(frame.Values[DefaultRtdeOutputs.RobotMode]));
        Assert.False(frame.Values.ContainsKey(DefaultRtdeOutputs.ActualTcpPose));
    }

    private static async Task ServeAsync(TcpListener listener)
    {
        using var tcp = await listener.AcceptTcpClientAsync();
        await using var stream = tcp.GetStream();
        RtdeOutputLayout? layout = null;
        while (true)
        {
            byte[] header;
            try
            {
                header = await ReadExactAsync(stream, 3);
            }
            catch (IOException)
            {
                return;
            }

            var size = BinaryPrimitives.ReadUInt16BigEndian(header);
            var type = header[2];
            var payload = size > 3 ? await ReadExactAsync(stream, size - 3) : [];
            if (type == RtdeProtocol.RequestProtocolVersion)
            {
                await stream.WriteAsync(RtdeBinaryCodec.PackBoolResponse(RtdeProtocol.RequestProtocolVersion, true));
            }
            else if (type == RtdeProtocol.SetupOutputs)
            {
                var names = System.Text.Encoding.ASCII.GetString(payload.AsSpan(8)).Split(',');
                var types = names.Select(name =>
                    name == DefaultRtdeOutputs.ActualTcpPose ? "NOT_FOUND" :
                    name == DefaultRtdeOutputs.RobotMode ? "INT32" : "DOUBLE");
                layout = RtdeBinaryCodec.ParseSetupOutputs(
                    RtdeBinaryCodec.PackSetupOutputsResponse(1, types).AsSpan(3),
                    names);
                await stream.WriteAsync(RtdeBinaryCodec.PackSetupOutputsResponse(1, types));
            }
            else if (type == RtdeProtocol.ControlStart)
            {
                await stream.WriteAsync(RtdeBinaryCodec.PackBoolResponse(RtdeProtocol.ControlStart, true));
                if (layout is null)
                {
                    continue;
                }

                var values = new Dictionary<string, object?>
                {
                    [DefaultRtdeOutputs.Timestamp] = 12.5,
                    [DefaultRtdeOutputs.RobotMode] = 7
                };
                await stream.WriteAsync(RtdeBinaryCodec.PackDataPackage(layout.RecipeId, layout.Fields, values));
            }
        }
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0)
            {
                throw new IOException("closed");
            }

            offset += read;
        }

        return buffer;
    }

    private sealed class FrameObserver : IObserver<RtdeFrame>
    {
        private readonly TaskCompletionSource<RtdeFrame> _ready;

        public FrameObserver(TaskCompletionSource<RtdeFrame> ready)
        {
            _ready = ready;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => _ready.TrySetException(error);

        public void OnNext(RtdeFrame value) => _ready.TrySetResult(value);
    }
}
