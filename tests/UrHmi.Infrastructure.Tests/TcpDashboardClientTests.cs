using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Infrastructure.Robot;

namespace UrHmi.Infrastructure.Tests;

public sealed class TcpDashboardClientTests
{
    [Fact]
    public async Task Queries_mode_safety_and_program_from_a_dashboard_server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeAsync(listener);

        await using var client = new TcpDashboardClient(NullLogger<TcpDashboardClient>.Instance);
        await client.ConnectAsync(IPAddress.Loopback.ToString(), port);
        var mode = await client.GetRobotModeAsync();
        var safety = await client.GetSafetyStatusAsync();
        var program = await client.GetProgramStateAsync();
        var running = await client.GetRunningAsync();
        var version = await client.GetPolyscopeVersionAsync();
        var brakes = await client.BrakeReleaseAsync();
        await client.DisconnectAsync();
        listener.Stop();
        await server;

        Assert.Equal("RUNNING", mode.Value);
        Assert.Equal("NORMAL", safety.Value);
        Assert.Equal("PLAYING", program.Value);
        Assert.Equal("Program running", running.Value);
        Assert.Equal("5.25.2", version.Value);
        Assert.Equal("Brake releasing", brakes.Value);
    }

    private static async Task ServeAsync(TcpListener listener)
    {
        using var tcp = await listener.AcceptTcpClientAsync();
        await using var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("Connected: Universal Robots Dashboard Server\n"));
        while (true)
        {
            var line = await TcpDashboardClient.ReadLineAsync(stream, CancellationToken.None);
            if (line is null)
            {
                return;
            }

            var reply = line switch
            {
                "robotmode" => "Robotmode: RUNNING\n",
                "safetystatus" => "Safetystatus: NORMAL\n",
                "programState" => "PLAYING default.urp\n",
                "running" => "Program running: true\n",
                "PolyscopeVersion" => "URSoftware 5.25.2\n",
                "brakeRelease" => "Brake releasing\n",
                _ => "Error: Unknown command\n"
            };
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
        }
    }
}
