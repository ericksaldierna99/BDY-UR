namespace UrHmi.Infrastructure.Robot;

internal static class RtdeProtocol
{
    public const byte RequestProtocolVersion = (byte)'V';
    public const byte SetupOutputs = (byte)'O';
    public const byte ControlStart = (byte)'S';
    public const byte ControlPause = (byte)'P';
    public const byte DataPackage = (byte)'U';
    public const byte TextMessage = (byte)'M';

    public const ushort ProtocolVersion2 = 2;
    public const int HeaderSize = 3;
    public const int MaxPackageSize = 65535;
}

internal enum RtdeValueType
{
    Bool,
    Uint8,
    Uint32,
    Uint64,
    Int32,
    Double,
    Vector3D,
    Vector6D,
    Vector6Int32,
    Vector6Uint32,
    NotFound,
    InUse,
    Unknown
}

internal sealed record RtdeOutputField(string Name, RtdeValueType Type);

internal sealed class RtdeOutputLayout
{
    public RtdeOutputLayout(
        byte recipeId,
        IReadOnlyList<RtdeOutputField> fields,
        IReadOnlyList<string> rejectedKeys)
    {
        RecipeId = recipeId;
        Fields = fields;
        RejectedKeys = rejectedKeys;
        NegotiatedKeys = fields.Select(f => f.Name).ToArray();
    }

    public byte RecipeId { get; }

    public IReadOnlyList<RtdeOutputField> Fields { get; }

    public IReadOnlyList<string> NegotiatedKeys { get; }

    public IReadOnlyList<string> RejectedKeys { get; }
}
