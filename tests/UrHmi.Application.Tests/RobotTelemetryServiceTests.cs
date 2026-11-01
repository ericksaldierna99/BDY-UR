using Microsoft.Extensions.Logging.Abstractions;
using UrHmi.Application.Telemetry;
using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Configuration;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Tests;

public sealed class RobotTelemetryServiceTests
{
    [Fact]
    public async Task Empty_robot_ip_does_not_connect()
    {
        var dashboard = new FakeDashboardClient();
        var rtde = new FakeRtdeClient();
        var alarms = new FakeAlarmSink();
        var sut = new RobotTelemetryService(
            dashboard,
            rtde,
            alarms,
            Unconfigured(),
            NullLogger<RobotTelemetryService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        Assert.Equal(0, dashboard.ConnectCalls);
        Assert.Equal(0, rtde.ConnectCalls);
        Assert.Equal(LinkState.Disconnected, sut.Current.Dashboard.Link);
        Assert.Equal(LinkState.Disconnected, sut.Current.Rtde.Link);
        Assert.Empty(alarms.Raised);
    }

    [Fact]
    public async Task Probe_without_ip_explains_that_sockets_stay_closed()
    {
        var sut = new RobotTelemetryService(
            new FakeDashboardClient(),
            new FakeRtdeClient(),
            new FakeAlarmSink(),
            Unconfigured(),
            NullLogger<RobotTelemetryService>.Instance);

        var probe = await sut.ProbeAsync();

        Assert.False(probe.IpConfigured);
        Assert.Contains("robot.ip is not set", probe.Summary, StringComparison.Ordinal);
    }

    private static HmiConfiguration Unconfigured() =>
        HmiConfiguration.CreateDefault() with
        {
            Robot = HmiConfiguration.CreateDefault().Robot with { Ip = string.Empty }
        };

    private sealed class FakeDashboardClient : IRobotDashboardClient
    {
        public int ConnectCalls { get; private set; }

        public ChannelHealth Health { get; private set; } = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            Health = new ChannelHealth(ChannelIds.RobotDashboard, LinkState.Connected, DateTimeOffset.UtcNow, TimeSpan.Zero, null);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            Health = ChannelHealth.Disconnected(ChannelIds.RobotDashboard);
            return Task.CompletedTask;
        }

        public Task<DashboardCommandResult> GetRobotModeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("not connected"));

        public Task<DashboardCommandResult> GetSafetyStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("not connected"));

        public Task<DashboardCommandResult> GetProgramStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("not connected"));

        public Task<DashboardCommandResult> GetRunningAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("not connected"));

        public Task<DashboardCommandResult> GetPolyscopeVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("not connected"));

        public Task<DashboardCommandResult> BrakeReleaseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DashboardCommandResult.Failed("not connected"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRtdeClient : IRtdeClient
    {
        public int ConnectCalls { get; private set; }

        public ChannelHealth Health { get; private set; } = ChannelHealth.Disconnected(ChannelIds.RobotRtde);

        public IObservable<RtdeFrame> Frames { get; } = new SilentObservable<RtdeFrame>();

        public IReadOnlyList<string> NegotiatedKeys { get; } = [];

        public IReadOnlyList<string> RejectedKeys { get; } = [];

        public Task ConnectAsync(string host, int port, RtdeRecipe recipe, CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            Health = new ChannelHealth(ChannelIds.RobotRtde, LinkState.Connected, DateTimeOffset.UtcNow, TimeSpan.Zero, null);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            Health = ChannelHealth.Disconnected(ChannelIds.RobotRtde);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAlarmSink : IAlarmSink
    {
        public List<AlarmRecord> Raised { get; } = [];

        public void Raise(AlarmRecord alarm) => Raised.Add(alarm);

        public void Clear(string alarmId, DateTimeOffset clearedUtc)
        {
        }
    }

    private sealed class SilentObservable<T> : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer) => Empty.Instance;

        private sealed class Empty : IDisposable
        {
            public static readonly Empty Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
