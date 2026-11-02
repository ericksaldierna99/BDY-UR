using Opc.Ua;

namespace UrHmi.Tools.Screwdriver.Tests;

public sealed class OpcValuesTests
{
    [Fact]
    public void Structure_fields_flatten_to_overall_result_values()
    {
        var result = FakeOpcStructure.Result(
            FakeOpcStructure.Joining(
                FakeOpcStructure.ResultValue("Final Torque (Nm)", 0, target: 2.4, evaluation: "NOT_OK", physicalQuantity: 2),
                FakeOpcStructure.ResultValue("Final Angle (°)", 1.5, target: 30, evaluation: "OK", physicalQuantity: 3)));

        var pair = SpinBridgeResultDecoder.Decode(latestResult: result);

        Assert.Equal(0, pair.TorqueNm);
        Assert.Equal(1.5, pair.AngleDeg);
    }

    [Fact]
    public void Iformattable_structure_is_not_skipped_as_a_scalar()
    {
        var joining = new FakeOpcStructure(
            ("overallResultValues", new FakeOpcStructure[]
            {
                FakeOpcStructure.ResultValue("Final Torque (Nm)", 2.4),
                FakeOpcStructure.ResultValue("Final Angle (°)", 30)
            }));

        Assert.True(joining is IFormattable);
        var pair = SpinBridgeResultDecoder.Decode(latestResult: joining);
        Assert.Equal(2.4, pair.TorqueNm);
        Assert.Equal(30, pair.AngleDeg);
    }

    [Fact]
    public void Array_of_extension_objects_unwraps_result_content()
    {
        var joining = FakeOpcStructure.Joining(
            FakeOpcStructure.ResultValue("Final Torque (Nm)", 1.25),
            FakeOpcStructure.ResultValue("Final Angle (°)", 45),
            FakeOpcStructure.ResultValue("Final Insertion Depth (mm)", 4.2, physicalQuantity: 6));
        var envelope = new FakeOpcStructure(("ResultContent", new FakeOpcStructure[] { joining }));

        var unwrapped = OpcValues.Unwrap(envelope);
        var pair = SpinBridgeResultDecoder.Decode(latestResult: unwrapped);

        Assert.Equal(1.25, pair.TorqueNm);
        Assert.Equal(45, pair.AngleDeg);
        Assert.Equal(4.2, pair.DepthMm);
    }
}
