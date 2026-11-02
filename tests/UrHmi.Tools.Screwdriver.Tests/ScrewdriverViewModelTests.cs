using UrHmi.Domain;
using UrHmi.Domain.Alarms;
using UrHmi.Domain.Plc;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class ScrewdriverViewModelTests
{
    [Fact]
    public async Task Enable_writes_the_plugin_owned_command_tag()
    {
        var host = new FakeHost
        {
            NextResult = new WriteResult(WriteOutcome.Rejected, "x", "PLC writes are not available in this phase.")
        };
        host.PlcFeed.Publish(ConnectedPlc(DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            [ScrewdriverTags.Status] = "ready"
        }));
        using var vm = new ScrewdriverViewModel(host);

        Assert.True(vm.ActuationAvailable);
        await vm.EnableCommand.ExecuteAsync(null);

        var write = Assert.Single(host.Writes);
        Assert.True(write.Tags.ContainsKey(ScrewdriverTags.CommandEnable));
        Assert.Contains(ScrewdriverTags.CommandEnable, vm.CommandStatus, StringComparison.Ordinal);
        Assert.Contains("Rejected", vm.CommandStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_fault_writes_the_reset_tag()
    {
        var host = new FakeHost();
        host.PlcFeed.Publish(ConnectedPlc(DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            [ScrewdriverTags.Status] = "fault"
        }));
        using var vm = new ScrewdriverViewModel(host);

        await vm.ResetFaultCommand.ExecuteAsync(null);

        Assert.Contains(ScrewdriverTags.CommandReset, Assert.Single(host.Writes).Tags.Keys);
    }

    [Fact]
    public void Live_fault_raises_a_tooling_alarm_and_clear_removes_it()
    {
        var host = new FakeHost();
        using var vm = new ScrewdriverViewModel(host);

        host.PlcFeed.Publish(ConnectedPlc(DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            [ScrewdriverTags.FaultCode] = "OVERCURRENT"
        }));

        var alarm = Assert.Single(host.Raised);
        Assert.Equal(ScrewdriverViewModel.FaultAlarmId, alarm.Id);
        Assert.Equal(AlarmOrigin.Tooling, alarm.Origin);
        Assert.Contains("OVERCURRENT", alarm.Description, StringComparison.Ordinal);

        host.PlcFeed.Publish(ConnectedPlc(DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            [ScrewdriverTags.FaultCode] = null
        }));

        Assert.Contains(ScrewdriverViewModel.FaultAlarmId, host.Cleared);
    }

    [Fact]
    public void Commands_are_disabled_when_the_plc_is_disconnected()
    {
        using var vm = new ScrewdriverViewModel(new FakeHost());

        Assert.False(vm.ActuationAvailable);
        Assert.False(vm.EnableCommand.CanExecute(null));
    }

    private static PlcSnapshot ConnectedPlc(DateTimeOffset now, IReadOnlyDictionary<string, object?> values)
    {
        var tags = values.ToDictionary(
            pair => pair.Key,
            pair => new PlcValue(pair.Key, pair.Value, now, Freshness.Live),
            StringComparer.Ordinal);
        return new PlcSnapshot(
            now,
            new ChannelHealth(ChannelIds.Plc, LinkState.Connected, now, TimeSpan.Zero, null),
            TimedValue<IReadOnlyDictionary<string, PlcValue>>.Live(tags, now),
            null);
    }
}
