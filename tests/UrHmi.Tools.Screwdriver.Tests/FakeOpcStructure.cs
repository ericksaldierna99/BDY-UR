using Opc.Ua;

namespace UrHmi.Tools.Screwdriver.Tests;

/// <summary>
/// Stand-in for ComplexTypeSystem 2.0 <c>Opc.Ua.Encoders.Structure</c>:
/// IStructure + IFormattable + IEncodeable, nested values as
/// ArrayOf&lt;ExtensionObject&gt; the way ResultContent / overallResultValues
/// arrive on ResultReadyEvent and LatestResult.
/// </summary>
internal sealed class FakeOpcStructure : IStructure, IFormattable, IEncodeable, ICloneable
{
    private readonly Dictionary<string, Variant> _fields = new(StringComparer.OrdinalIgnoreCase);

    public FakeOpcStructure(params (string Name, object? Value)[] fields)
    {
        foreach (var (name, value) in fields)
        {
            _fields[name] = ToVariant(value);
        }
    }

    public Variant this[int index]
    {
        get => _fields.Values.ElementAt(index);
        set
        {
            var key = _fields.Keys.ElementAt(index);
            _fields[key] = value;
        }
    }

    public Variant this[string name]
    {
        get => _fields[name];
        set => _fields[name] = value;
    }

    public IReadOnlyList<IStructureField> GetFields() =>
        _fields.Keys.Select(name => (IStructureField)new FakeField(name)).ToArray();

    public string ToString(string? format, IFormatProvider? formatProvider) => "FakeOpcStructure";

    public ExpandedNodeId TypeId => ExpandedNodeId.Null;

    public ExpandedNodeId BinaryEncodingId => ExpandedNodeId.Null;

    public ExpandedNodeId XmlEncodingId => ExpandedNodeId.Null;

    public void Encode(IEncoder encoder)
    {
    }

    public void Decode(IDecoder decoder)
    {
    }

    public bool IsEqual(IEncodeable? encodeable) => ReferenceEquals(this, encodeable);

    public object Clone() => this;

    public static FakeOpcStructure ResultValue(
        string name,
        double measured,
        double? target = null,
        string? evaluation = null,
        int? physicalQuantity = null) =>
        new(
            ("Name", name),
            ("MeasuredValue", measured),
            ("TargetValue", target ?? measured),
            ("LowLimit", 0d),
            ("HighLimit", (target ?? measured) * 2),
            ("ResultEvaluation", evaluation ?? "OK"),
            ("PhysicalQuantity", physicalQuantity ?? 0));

    public static FakeOpcStructure Joining(params FakeOpcStructure[] overallResultValues) =>
        new(("OverallResultValues", overallResultValues));

    public static FakeOpcStructure Result(FakeOpcStructure joining) =>
        new(("ResultContent", new FakeOpcStructure[] { joining }));

    private static Variant ToVariant(object? value) =>
        value switch
        {
            null => Variant.Null,
            Variant variant => variant,
            double d => new Variant(d),
            float f => new Variant(f),
            int i => new Variant(i),
            byte b => new Variant(b),
            string s => new Variant(s),
            IEncodeable encodeable => new Variant(new ExtensionObject(encodeable, copy: false)),
            Array array and not byte[] and not char[] => EncodeableArray(array),
            _ => throw new InvalidOperationException($"Unsupported FakeOpcStructure field {value.GetType()}.")
        };

    private static Variant EncodeableArray(Array array)
    {
        var extensions = new ExtensionObject[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            extensions[i] = array.GetValue(i) switch
            {
                IEncodeable encodeable => new ExtensionObject(encodeable, copy: false),
                ExtensionObject extension => extension,
                null => ExtensionObject.Null,
                var other => throw new InvalidOperationException(
                    $"Unsupported array element {other.GetType()}.")
            };
        }

        return new Variant((ArrayOf<ExtensionObject>)extensions);
    }

    private sealed class FakeField(string name) : IStructureField
    {
        public string Name { get; } = name;

        public bool IsOptional => false;

        public TypeInfo TypeInfo { get; } = new(BuiltInType.ExtensionObject, ValueRanks.Any);
    }
}
