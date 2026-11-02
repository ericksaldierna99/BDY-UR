using UrHmi.Domain;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class SpinBridgeOpcUaClientTests
{
    [Fact]
    public async Task Poll_maps_final_torque_and_angle_as_live()
    {
        var channel = SpinTree(torque: 2.4, angle: 30);
        using var client = new SpinBridgeOpcUaClient(TestConfig(), channel);
        client.Start();

        var reading = await WaitForAsync(client, value =>
            value.Channel.Link == LinkState.Connected
            && value.TorqueNm.Freshness == Freshness.Live);

        Assert.Equal("opc.tcp://192.168.37.1:4840", Assert.Single(channel.ConnectCalls));
        Assert.Equal(2.4, reading.TorqueNm.Value);
        Assert.Equal(30, reading.AngleDeg.Value);
        Assert.Equal(Freshness.Live, reading.AngleDeg.Freshness);
        Assert.True(IndicatorLive(reading));
    }

    [Fact]
    public async Task Empty_latest_result_stays_connected_and_unavailable()
    {
        var channel = SpinTree(torque: null, angle: null);
        using var client = new SpinBridgeOpcUaClient(TestConfig(), channel);
        client.Start();

        var reading = await WaitForAsync(client, value =>
            value.Channel.Link == LinkState.Connected
            && value.TorqueNm.Freshness == Freshness.Unavailable);

        Assert.Equal(Freshness.Unavailable, reading.AngleDeg.Freshness);
        Assert.Null(reading.TorqueNm.LastValidUtc);
    }

    [Fact]
    public async Task Disconnect_after_a_live_read_marks_last_values_stale()
    {
        var channel = SpinTree(torque: 1.5, angle: 20);
        using var client = new SpinBridgeOpcUaClient(TestConfig(staleTimeoutMs: 200), channel);
        client.Start();

        await WaitForAsync(client, value => value.TorqueNm.Freshness == Freshness.Live);
        channel.IsConnected = false;

        var reading = await WaitForAsync(client, value =>
            value.Channel.Link == LinkState.Disconnected
            && value.TorqueNm.Freshness == Freshness.Stale);

        Assert.Equal(1.5, reading.TorqueNm.Value);
        Assert.Equal(20, reading.AngleDeg.Value);
        Assert.Equal(Freshness.Stale, reading.AngleDeg.Freshness);
    }

    [Fact]
    public async Task Empty_ip_never_opens_a_socket()
    {
        var channel = new FakeOpcUaChannel();
        var configuration = new ScrewdriverConfiguration("SDV35", "", 4840, 2000);
        using var client = new SpinBridgeOpcUaClient(configuration, channel);
        client.Start();

        var reading = await WaitForAsync(client, value =>
            value.Channel.Link == LinkState.Disconnected
            && value.Channel.LastError is not null);

        Assert.Empty(channel.ConnectCalls);
        Assert.Contains("not set", reading.Channel.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Freshness.Unavailable, reading.TorqueNm.Freshness);
    }

    [Fact]
    public async Task Objects_folder_fallback_finds_latest_result()
    {
        var latest = "ns=1;s=Latest";
        var channel = new FakeOpcUaChannel();
        channel.Map(SpinBridgeNodes.TighteningSystem);
        channel.Map(SpinBridgeNodes.ResultManagement);
        channel.Map(
            SpinBridgeNodes.ObjectsFolder,
            FakeOpcUaChannel.Node("ns=1;s=TS", SpinBridgeNodes.TighteningSystemName));
        channel.Map(
            "ns=1;s=TS",
            FakeOpcUaChannel.Node("ns=1;s=RM", SpinBridgeNodes.ResultManagementName));
        channel.Map(
            "ns=1;s=RM",
            FakeOpcUaChannel.Node("ns=1;s=Results", SpinBridgeNodes.ResultsName));
        channel.Map(
            "ns=1;s=Results",
            FakeOpcUaChannel.Node(latest, SpinBridgeNodes.LatestResultName));
        channel.Map(
            latest,
            FakeOpcUaChannel.Node("ns=1;s=T", SpinBridgeResultDecoder.FinalTorqueNm),
            FakeOpcUaChannel.Node("ns=1;s=A", SpinBridgeResultDecoder.FinalAngleDeg));
        channel.Values["ns=1;s=T"] = 0.5;
        channel.Values["ns=1;s=A"] = 7;

        using var client = new SpinBridgeOpcUaClient(TestConfig(), channel);
        client.Start();

        var reading = await WaitForAsync(client, value => value.TorqueNm.Freshness == Freshness.Live);
        Assert.Equal(0.5, reading.TorqueNm.Value);
        Assert.Equal(7, reading.AngleDeg.Value);
    }

    [Fact]
    public async Task Failed_connect_retries_the_configured_endpoint()
    {
        var channel = SpinTree(torque: 1, angle: 2);
        var attempts = 0;
        channel.ConnectHandler = (_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new IOException("connection refused");
            }

            channel.IsConnected = true;
            return Task.CompletedTask;
        };

        using var client = new SpinBridgeOpcUaClient(TestConfig(), channel);
        client.Start();

        var reading = await WaitForAsync(client, value => value.TorqueNm.Freshness == Freshness.Live);
        Assert.True(attempts >= 2);
        Assert.Equal(1, reading.TorqueNm.Value);
        Assert.All(channel.ConnectCalls, url => Assert.Equal("opc.tcp://192.168.37.1:4840", url));
    }

    private static bool IndicatorLive(SpinBridgeReading reading) =>
        reading.TorqueNm.Freshness == Freshness.Live && reading.AngleDeg.Freshness == Freshness.Live;

    private static ScrewdriverConfiguration TestConfig(int staleTimeoutMs = 2000) =>
        new(ScrewdriverConfiguration.DefaultToolModel, ScrewdriverConfiguration.DefaultSpinBridgeIp, 4840, staleTimeoutMs);

    private static FakeOpcUaChannel SpinTree(double? torque, double? angle)
    {
        const string latest = "ns=1;s=LatestResult";
        var channel = new FakeOpcUaChannel();
        channel.Map(
            SpinBridgeNodes.TighteningSystem,
            FakeOpcUaChannel.Node(SpinBridgeNodes.ResultManagement, SpinBridgeNodes.ResultManagementName));
        channel.Map(
            SpinBridgeNodes.ResultManagement,
            FakeOpcUaChannel.Node("ns=1;s=Results", SpinBridgeNodes.ResultsName));
        channel.Map(
            "ns=1;s=Results",
            FakeOpcUaChannel.Node(latest, SpinBridgeNodes.LatestResultName));
        channel.Map(
            latest,
            FakeOpcUaChannel.Node("ns=1;s=Overall", SpinBridgeNodes.OverallResultValuesName));
        if (torque is { } t && angle is { } a)
        {
            channel.Values["ns=1;s=Overall"] = new object[]
            {
                new NamedValue(SpinBridgeResultDecoder.FinalTorqueNm, t),
                new NamedValue(SpinBridgeResultDecoder.FinalAngleDeg, a)
            };
        }
        else
        {
            channel.Values["ns=1;s=Overall"] = Array.Empty<object>();
        }

        return channel;
    }

    private static async Task<SpinBridgeReading> WaitForAsync(
        ISpinBridgeClient client,
        Func<SpinBridgeReading, bool> match)
    {
        var tcs = new TaskCompletionSource<SpinBridgeReading>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = client.Subscribe(new MatchObserver(match, tcs));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var registration = timeout.Token.Register(() => tcs.TrySetException(
            new TimeoutException("Timed out waiting for SpinBridge reading.")));
        return await tcs.Task.ConfigureAwait(false);
    }

    private sealed record NamedValue(string Name, object Value);

    private sealed class MatchObserver : IObserver<SpinBridgeReading>
    {
        private readonly Func<SpinBridgeReading, bool> _match;
        private readonly TaskCompletionSource<SpinBridgeReading> _tcs;

        public MatchObserver(Func<SpinBridgeReading, bool> match, TaskCompletionSource<SpinBridgeReading> tcs)
        {
            _match = match;
            _tcs = tcs;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => _tcs.TrySetException(error);

        public void OnNext(SpinBridgeReading value)
        {
            if (_match(value))
            {
                _tcs.TrySetResult(value);
            }
        }
    }
}
