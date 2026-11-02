using System.Xml.Linq;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class SpinBridgeResultDecoderTests
{
    [Fact]
    public void Overall_result_values_map_final_torque_nm_and_angle_deg()
    {
        var overall = new object[]
        {
            new NamedValue(SpinBridgeResultDecoder.FinalTorqueNm, 2.4),
            new NamedValue(SpinBridgeResultDecoder.FinalAngleDeg, 30)
        };

        var pair = SpinBridgeResultDecoder.Decode(overallResultValues: overall);

        Assert.Equal(2.4, pair.TorqueNm);
        Assert.Equal(30, pair.AngleDeg);
    }

    [Fact]
    public void Named_child_nodes_win_over_empty_overall_values()
    {
        var fields = new Dictionary<string, object?>
        {
            ["Final Torque (Nm)"] = 1.25,
            ["Final Angle (°)"] = 45
        };

        var pair = SpinBridgeResultDecoder.Decode(fields, overallResultValues: Array.Empty<object>());

        Assert.Equal(1.25, pair.TorqueNm);
        Assert.Equal(45, pair.AngleDeg);
    }

    [Fact]
    public void Xml_overall_result_values_are_decoded()
    {
        var xml = XElement.Parse(
            """
            <OverallResultValues>
              <ResultValue>
                <Name>Final Torque (Nm)</Name>
                <MeasuredValue>3.5</MeasuredValue>
              </ResultValue>
              <ResultValue>
                <Name>Final Angle (°)</Name>
                <Value>12</Value>
              </ResultValue>
            </OverallResultValues>
            """);

        var pair = SpinBridgeResultDecoder.Decode(overallResultValues: xml);

        Assert.Equal(3.5, pair.TorqueNm);
        Assert.Equal(12, pair.AngleDeg);
    }

    [Fact]
    public void Empty_latest_result_returns_nulls()
    {
        var pair = SpinBridgeResultDecoder.Decode(
            new Dictionary<string, object?>(),
            overallResultValues: Array.Empty<object>());

        Assert.Null(pair.TorqueNm);
        Assert.Null(pair.AngleDeg);
    }

    [Fact]
    public void Torque_and_angle_names_are_recognized()
    {
        Assert.True(SpinBridgeResultDecoder.IsTorqueName("Final Torque (Nm)"));
        Assert.True(SpinBridgeResultDecoder.IsAngleName("Final Angle (°)"));
        Assert.True(SpinBridgeResultDecoder.IsAngleName("Final Angle (deg)"));
        Assert.True(SpinBridgeResultDecoder.IsOverallName("overallResultValues"));
        Assert.False(SpinBridgeResultDecoder.IsTorqueName("Peak Current (A)"));
    }

    private sealed record NamedValue(string Name, object Value);
}
