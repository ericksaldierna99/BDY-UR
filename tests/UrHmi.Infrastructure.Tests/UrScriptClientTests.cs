using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Domain.Configuration;
using UrHmi.Infrastructure.Robot;

namespace UrHmi.Infrastructure.Tests;

public sealed class UrScriptClientTests
{
    [Fact]
    public async Task Empty_robot_ip_does_not_open_a_socket()
    {
        var client = new UrScriptClient(
            Config(string.Empty, 30002, 30001),
            NullLogger<UrScriptClient>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("movej([0,0,0,0,0,0], a=1, v=0.25)\n"));

        Assert.Contains("robot.ip is not set", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sends_script_to_secondary_port()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = ReceiveOnceAsync(listener);
        var client = new UrScriptClient(
            Config(IPAddress.Loopback.ToString(), port, port + 1),
            NullLogger<UrScriptClient>.Instance);

        await client.SendAsync("movej([0.1, 0, 0, 0, 0, 0], a=1, v=0.25)\n");
        listener.Stop();
        var payload = await received;

        Assert.Equal("movej([0.1, 0, 0, 0, 0, 0], a=1, v=0.25)\n", payload);
    }

    [Fact]
    public async Task Falls_back_to_primary_interface_when_secondary_is_closed()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var fallback = ((IPEndPoint)listener.LocalEndpoint).Port;
        var closedSecondary = FirstFreePort(fallback);
        var received = ReceiveOnceAsync(listener);
        var client = new UrScriptClient(
            Config(IPAddress.Loopback.ToString(), closedSecondary, fallback),
            NullLogger<UrScriptClient>.Instance);

        await client.SendAsync("brake_release()\n");
        listener.Stop();
        var payload = await received;

        Assert.Equal("brake_release()\n", payload);
    }

    [Fact]
    public async Task SendProgram_prefers_primary_and_keeps_the_connection_open()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var primary = ((IPEndPoint)listener.LocalEndpoint).Port;
        var closedSecondary = FirstFreePort(primary);
        using var client = new UrScriptClient(
            Config(IPAddress.Loopback.ToString(), closedSecondary, primary),
            NullLogger<UrScriptClient>.Instance);
        var accepted = listener.AcceptTcpClientAsync();

        var send = client.SendProgramAsync(
            "def start_freedrive():\n  freedrive_mode()\n  while True:\n    sync()\n  end\nend\nstart_freedrive()\n");
        using var tcp = await accepted;
        await using var stream = tcp.GetStream();
        var buffer = new byte[512];
        var read = await stream.ReadAsync(buffer);
        await send;
        listener.Stop();

        var payload = Encoding.ASCII.GetString(buffer, 0, read);
        Assert.Contains("freedrive_mode()", payload, StringComparison.Ordinal);
        Assert.Contains("start_freedrive()", payload, StringComparison.Ordinal);
        var remoteClosed = tcp.Client.Poll(50_000, SelectMode.SelectRead) && tcp.Client.Available == 0;
        Assert.False(remoteClosed);

        client.CloseProgram();
        var closedAfter = tcp.Client.Poll(200_000, SelectMode.SelectRead) && tcp.Client.Available == 0;
        Assert.True(closedAfter);
    }

    private static HmiConfiguration Config(string ip, int port, int fallback) =>
        HmiConfiguration.CreateDefault() with
        {
            Robot = HmiConfiguration.CreateDefault().Robot with
            {
                Ip = ip,
                UrScript = new UrScriptChannelConfiguration(port, fallback)
            }
        };

    private static async Task<string> ReceiveOnceAsync(TcpListener listener)
    {
        using var tcp = await listener.AcceptTcpClientAsync();
        await using var stream = tcp.GetStream();
        var buffer = new byte[256];
        var read = await stream.ReadAsync(buffer);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private static int FirstFreePort(int avoid)
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        if (port != avoid)
        {
            return port;
        }

        probe.Stop();
        using var retry = new TcpListener(IPAddress.Loopback, 0);
        retry.Start();
        return ((IPEndPoint)retry.LocalEndpoint).Port;
    }
}
