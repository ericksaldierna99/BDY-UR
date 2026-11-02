using UrHmi.Application.Authorization;
using UrHmi.Application.Telemetry;
using UrHmi.Application.Tools;
using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Plc;
using UrHmi.Domain.Ports;
using UrHmi.Domain.Robot;

namespace UrHmi.Application.Tests;

public sealed class ToolHostTests
{
    [Fact]
    public async Task Write_is_rejected_without_process_permission()
    {
        var channel = new RecordingPlcChannel();
        var host = new ToolHost(
            new StubPlcTelemetry(),
            new StubRobotTelemetry(),
            new StubAlarms(),
            channel,
            new DenyAuthorization());

        var result = await host.WritePlcAsync(new PlcWriteRequest("c1", new Dictionary<string, object?>()));

        Assert.Equal(WriteOutcome.Rejected, result.Outcome);
        Assert.Contains("permission", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task Write_is_forwarded_to_the_plc_channel_when_permitted()
    {
        var channel = new RecordingPlcChannel
        {
            Result = new WriteResult(WriteOutcome.Accepted, "c2", "ok")
        };
        var host = new ToolHost(
            new StubPlcTelemetry(),
            new StubRobotTelemetry(),
            new StubAlarms(),
            channel,
            new AllowAuthorization());
        var request = new PlcWriteRequest(
            "c2",
            new Dictionary<string, object?> { ["tag"] = true });

        var result = await host.WritePlcAsync(request);

        Assert.Equal(WriteOutcome.Accepted, result.Outcome);
        var written = Assert.Single(channel.Writes);
        Assert.Same(request, written);
    }

    private sealed class AllowAuthorization : IAuthorizationService
    {
        public bool HasProcessChangePermission() => true;
    }

    private sealed class DenyAuthorization : IAuthorizationService
    {
        public bool HasProcessChangePermission() => false;
    }

    private sealed class RecordingPlcChannel : IPlcChannel
    {
        public List<PlcWriteRequest> Writes { get; } = [];

        public WriteResult Result { get; set; } =
            new(WriteOutcome.Accepted, "c", null);

        public ChannelHealth Health { get; } = ChannelHealth.Disconnected(ChannelIds.Plc);

        public IObservable<PlcSnapshot> Snapshots { get; } = NeverPlc.Instance;

        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<WriteResult> WriteAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            Writes.Add(request);
            return Task.FromResult(Result);
        }

        public Task DisconnectAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubPlcTelemetry : IPlcTelemetry
    {
        public PlcSnapshot Current { get; } = PlcSnapshot.Empty(DateTimeOffset.UtcNow);

        public IObservable<PlcSnapshot> Snapshots { get; } = NeverPlc.Instance;
    }

    private sealed class StubRobotTelemetry : IRobotTelemetry
    {
        public RobotSnapshot Current { get; } = RobotSnapshot.Empty(DateTimeOffset.UtcNow);

        public IObservable<RobotSnapshot> Snapshots { get; } = NeverRobot.Instance;
    }

    private sealed class StubAlarms : IAlarmSink
    {
        public void Raise(AlarmRecord alarm)
        {
        }

        public void Clear(string alarmId, DateTimeOffset clearedUtc)
        {
        }
    }

    private sealed class NeverPlc : IObservable<PlcSnapshot>
    {
        public static readonly NeverPlc Instance = new();

        public IDisposable Subscribe(IObserver<PlcSnapshot> observer) => Empty.Instance;
    }

    private sealed class NeverRobot : IObservable<RobotSnapshot>
    {
        public static readonly NeverRobot Instance = new();

        public IDisposable Subscribe(IObserver<RobotSnapshot> observer) => Empty.Instance;
    }

    private sealed class Empty : IDisposable
    {
        public static readonly Empty Instance = new();

        public void Dispose()
        {
        }
    }
}
