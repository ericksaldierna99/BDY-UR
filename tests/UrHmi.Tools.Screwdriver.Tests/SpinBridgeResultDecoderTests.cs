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
    public void ResultContent_wrapper_maps_zero_nm_and_angle()
    {
        var latest = new
        {
            ResultContent = new JoiningResult(
            [
                new ResultValue("Final Torque", 0),
                new ResultValue("Final Angle", 1.5)
            ])
        };

        var pair = SpinBridgeResultDecoder.Decode(latestResult: latest);

        Assert.Equal(0, pair.TorqueNm);
        Assert.Equal(1.5, pair.AngleDeg);
        Assert.Null(SpinBridgeResultDecoder.DescribeFailure(latest, pair));
    }

    [Fact]
    public void Physical_quantity_names_map_torque_and_angle()
    {
        var overall = new object[]
        {
            new Dictionary<string, object?> { ["physicalQuantity"] = "Torque", ["measuredValue"] = 2.1 },
            new Dictionary<string, object?> { ["physicalQuantity"] = "Angle", ["measuredValue"] = 12 }
        };

        var pair = SpinBridgeResultDecoder.Decode(overallResultValues: overall);

        Assert.Equal(2.1, pair.TorqueNm);
        Assert.Equal(12, pair.AngleDeg);
    }

    [Fact]
    public void Joining_result_overall_values_named_final_torque_and_angle()
    {
        var latest = new JoiningResult(
        [
            new ResultValue("Final Torque", 2.4),
            new ResultValue("Final Angle", 30)
        ]);

        var pair = SpinBridgeResultDecoder.Decode(latestResult: latest);

        Assert.Equal(2.4, pair.TorqueNm);
        Assert.Equal(30, pair.AngleDeg);
        Assert.Null(SpinBridgeResultDecoder.DescribeFailure(latest, pair));
    }

    [Fact]
    public void Nested_measured_value_objects_are_decoded()
    {
        var latest = new
        {
            overallResultValues = new object[]
            {
                new { name = "Final Torque", measuredValue = new { Value = 1.25 } },
                new { name = "Final Angle", measuredValue = new { Value = 45d } }
            }
        };

        var pair = SpinBridgeResultDecoder.Decode(latestResult: latest);

        Assert.Equal(1.25, pair.TorqueNm);
        Assert.Equal(45, pair.AngleDeg);
    }

    [Fact]
    public void Dictionary_overall_entries_are_decoded()
    {
        var overall = new object[]
        {
            new Dictionary<string, object?> { ["name"] = "Final Torque", ["measuredValue"] = 2.4 },
            new Dictionary<string, object?> { ["name"] = "Final Angle", ["measuredValue"] = 30 }
        };

        var pair = SpinBridgeResultDecoder.Decode(overallResultValues: overall);

        Assert.Equal(2.4, pair.TorqueNm);
        Assert.Equal(30, pair.AngleDeg);
    }

    [Fact]
    public void Json_dashboard_payload_maps_zero_nm_not_ok()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """
            {
              "results": [
                {
                  "resultEvaluation": "NOT_OK",
                  "overallResultValues": [
                    { "name": "Final Torque", "measuredValue": 0 },
                    { "name": "Final Angle", "measuredValue": 0 }
                  ]
                }
              ]
            }
            """);

        var pair = SpinBridgeResultDecoder.Decode(latestResult: SpinBridgeJson.ToObject(document.RootElement));

        Assert.Equal(0, pair.TorqueNm);
        Assert.Equal(0, pair.AngleDeg);
    }

    [Fact]
    public void Http_torque_angle_property_names_are_decoded()
    {
        var latest = new Dictionary<string, object?>
        {
            ["torque"] = 0.8,
            ["angle"] = 12
        };

        var pair = SpinBridgeResultDecoder.Decode(latestResult: latest);

        Assert.Equal(0.8, pair.TorqueNm);
        Assert.Equal(12, pair.AngleDeg);
    }

    [Fact]
    public void Undecoded_bytes_describe_a_failure()
    {
        var latest = new byte[] { 1, 2, 3, 4 };
        var pair = SpinBridgeResultDecoder.Decode(latestResult: latest);

        Assert.Null(pair.TorqueNm);
        Assert.Null(pair.AngleDeg);
        Assert.Contains(
            "undecoded",
            SpinBridgeResultDecoder.DescribeFailure(latest, pair),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Final Torque",
            SpinBridgeResultDecoder.DescribeFailure(new object(), pair) ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Documented_overall_result_values_map_measured_torque_angle_and_depth()
    {
        var overall = new object[]
        {
            ResultEntry("Final Torque (Nm)", measured: 0, target: 2.4, evaluation: "NOT_OK"),
            ResultEntry("Final Angle (°)", measured: 1.5, target: 30, evaluation: "OK"),
            ResultEntry("Final Insertion Depth (mm)", measured: 4.2, target: 5, evaluation: "OK")
        };

        var pair = SpinBridgeResultDecoder.Decode(
            latestResult: new Dictionary<string, object?>
            {
                ["ResultContent"] = new Dictionary<string, object?>
                {
                    ["OverallResultValues"] = overall
                }
            });

        Assert.Equal(0, pair.TorqueNm);
        Assert.Equal(1.5, pair.AngleDeg);
        Assert.Equal(4.2, pair.DepthMm);
        Assert.Null(SpinBridgeResultDecoder.DescribeFailure(overall, pair));
    }

    [Fact]
    public void Pascal_case_joining_result_fields_are_decoded()
    {
        var latest = new Dictionary<string, object?>
        {
            ["OverallResultValues"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["Name"] = "Final Torque (Nm)",
                    ["MeasuredValue"] = 2.4,
                    ["TargetValue"] = 2.5,
                    ["LowLimit"] = 1,
                    ["HighLimit"] = 4,
                    ["ResultEvaluation"] = "OK"
                },
                new Dictionary<string, object?>
                {
                    ["Name"] = "Final Angle (°)",
                    ["MeasuredValue"] = 30,
                    ["TargetValue"] = 30,
                    ["LowLimit"] = 0,
                    ["HighLimit"] = 90,
                    ["ResultEvaluation"] = "OK"
                }
            }
        };

        var pair = SpinBridgeResultDecoder.Decode(latestResult: latest);

        Assert.Equal(2.4, pair.TorqueNm);
        Assert.Equal(30, pair.AngleDeg);
    }

    [Fact]
    public void Physical_quantity_integer_does_not_overwrite_the_name()
    {
        var overall = new object[]
        {
            new Dictionary<string, object?>
            {
                ["Name"] = "Final Torque (Nm)",
                ["MeasuredValue"] = 0d,
                ["PhysicalQuantity"] = 2,
                ["ResultEvaluation"] = "NOT_OK"
            },
            new Dictionary<string, object?>
            {
                ["Name"] = "Final Angle (°)",
                ["MeasuredValue"] = 1.5d,
                ["PhysicalQuantity"] = 3
            },
            new Dictionary<string, object?>
            {
                ["Name"] = "Final Insertion Depth (mm)",
                ["MeasuredValue"] = 4.2d,
                ["PhysicalQuantity"] = 6
            }
        };

        var pair = SpinBridgeResultDecoder.Decode(overallResultValues: overall);

        Assert.Equal(0, pair.TorqueNm);
        Assert.Equal(1.5, pair.AngleDeg);
        Assert.Equal(4.2, pair.DepthMm);
    }

    [Fact]
    public void Physical_quantity_codes_map_when_name_is_missing()
    {
        var overall = new object[]
        {
            new Dictionary<string, object?> { ["physicalQuantity"] = 2, ["measuredValue"] = 2.4 },
            new Dictionary<string, object?> { ["physicalQuantity"] = 3, ["measuredValue"] = 30 }
        };

        var pair = SpinBridgeResultDecoder.Decode(overallResultValues: overall);

        Assert.Equal(2.4, pair.TorqueNm);
        Assert.Equal(30, pair.AngleDeg);
    }

    [Fact]
    public void Torque_and_angle_names_are_recognized()
    {
        Assert.True(SpinBridgeResultDecoder.IsTorqueName("Final Torque (Nm)"));
        Assert.True(SpinBridgeResultDecoder.IsTorqueName("torque"));
        Assert.True(SpinBridgeResultDecoder.IsAngleName("Final Angle (°)"));
        Assert.True(SpinBridgeResultDecoder.IsAngleName("Final Angle (deg)"));
        Assert.True(SpinBridgeResultDecoder.IsAngleName("angle"));
        Assert.True(SpinBridgeResultDecoder.IsOverallName("overallResultValues"));
        Assert.False(SpinBridgeResultDecoder.IsTorqueName("Peak Current (A)"));
    }

    private static Dictionary<string, object?> ResultEntry(
        string name,
        double measured,
        double target,
        string evaluation) =>
        new()
        {
            ["name"] = name,
            ["measuredValue"] = measured,
            ["targetValue"] = target,
            ["lowLimit"] = 0,
            ["highLimit"] = target * 2,
            ["resultEvaluation"] = evaluation,
            ["physicalQuantity"] = name,
            ["engineeringUnits"] = name
        };

    private sealed record NamedValue(string Name, object Value);

    private sealed class JoiningResult
    {
        public JoiningResult(object[] overallResultValues) => this.overallResultValues = overallResultValues;

        public object[] overallResultValues { get; }
    }

    private sealed class ResultValue
    {
        public ResultValue(string name, double measuredValue)
        {
            this.name = name;
            this.measuredValue = measuredValue;
        }

        public string name { get; }

        public double measuredValue { get; }
    }
}
