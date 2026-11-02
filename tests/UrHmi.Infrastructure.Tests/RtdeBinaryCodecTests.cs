using UrHmi.Domain.Robot;
using UrHmi.Infrastructure.Robot;

namespace UrHmi.Infrastructure.Tests;

public sealed class RtdeBinaryCodecTests
{
    [Fact]
    public void Setup_drops_not_found_keys_and_does_not_unpack_them()
    {
        var requested = new[] { DefaultRtdeOutputs.Timestamp, DefaultRtdeOutputs.ActualTcpPose, DefaultRtdeOutputs.RobotMode };
        var response = RtdeBinaryCodec.PackSetupOutputsResponse(1, ["DOUBLE", "NOT_FOUND", "INT32"]);
        var layout = RtdeBinaryCodec.ParseSetupOutputs(response.AsSpan(3), requested);

        Assert.Equal(1, layout.RecipeId);
        Assert.Equal([DefaultRtdeOutputs.Timestamp, DefaultRtdeOutputs.RobotMode], layout.NegotiatedKeys);
        Assert.Equal([DefaultRtdeOutputs.ActualTcpPose], layout.RejectedKeys);

        var data = RtdeBinaryCodec.PackDataPackage(
            1,
            layout.Fields,
            new Dictionary<string, object?>
            {
                [DefaultRtdeOutputs.Timestamp] = 1.5,
                [DefaultRtdeOutputs.RobotMode] = 7
            });
        var values = RtdeBinaryCodec.UnpackData(data.AsSpan(3), layout);

        Assert.Equal(1.5, (double)values[DefaultRtdeOutputs.Timestamp]!);
        Assert.Equal(7, (int)values[DefaultRtdeOutputs.RobotMode]!);
        Assert.False(values.ContainsKey(DefaultRtdeOutputs.ActualTcpPose));
    }

    [Fact]
    public void Vector6d_round_trips_big_endian()
    {
        var requested = new[] { DefaultRtdeOutputs.ActualTcpPose };
        var response = RtdeBinaryCodec.PackSetupOutputsResponse(2, ["VECTOR6D"]);
        var layout = RtdeBinaryCodec.ParseSetupOutputs(response.AsSpan(3), requested);
        var pose = new[] { 0.11, -0.22, 0.33, 0.01, 0.02, 0.03 };
        var data = RtdeBinaryCodec.PackDataPackage(2, layout.Fields, new Dictionary<string, object?>
        {
            [DefaultRtdeOutputs.ActualTcpPose] = pose
        });

        var values = RtdeBinaryCodec.UnpackData(data.AsSpan(3), layout);
        var unpacked = Assert.IsType<double[]>(values[DefaultRtdeOutputs.ActualTcpPose]);
        Assert.Equal(pose, unpacked);
    }

    [Fact]
    public void Recipe_id_zero_is_preserved_so_the_client_can_reject_the_setup()
    {
        var response = RtdeBinaryCodec.PackSetupOutputsResponse(0, ["NOT_FOUND"]);
        var layout = RtdeBinaryCodec.ParseSetupOutputs(response.AsSpan(3), ["missing"]);

        Assert.Equal(0, layout.RecipeId);
        Assert.Contains("missing", layout.RejectedKeys);
    }
}
