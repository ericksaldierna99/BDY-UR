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
