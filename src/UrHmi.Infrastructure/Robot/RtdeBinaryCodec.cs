using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace UrHmi.Infrastructure.Robot;

internal static class RtdeBinaryCodec
{
    public static byte[] PackHeaderAndPayload(byte command, ReadOnlySpan<byte> payload)
    {
        var size = RtdeProtocol.HeaderSize + payload.Length;
        if (size > RtdeProtocol.MaxPackageSize)
        {
            throw new InvalidOperationException($"RTDE package size {size} exceeds {RtdeProtocol.MaxPackageSize}.");
        }

        var buffer = new byte[size];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), (ushort)size);
        buffer[2] = command;
        payload.CopyTo(buffer.AsSpan(RtdeProtocol.HeaderSize));
        return buffer;
    }

    public static byte[] PackProtocolVersionRequest(ushort version)
    {
        Span<byte> payload = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(payload, version);
        return PackHeaderAndPayload(RtdeProtocol.RequestProtocolVersion, payload);
    }

    public static byte[] PackSetupOutputs(double frequencyHz, IReadOnlyList<string> names)
    {
        var namesBytes = Encoding.ASCII.GetBytes(string.Join(',', names));
        var payload = new byte[8 + namesBytes.Length];
        WriteDoubleBigEndian(payload.AsSpan(0, 8), frequencyHz);
        namesBytes.CopyTo(payload.AsSpan(8));
        return PackHeaderAndPayload(RtdeProtocol.SetupOutputs, payload);
    }

    public static byte[] PackEmpty(byte command) => PackHeaderAndPayload(command, ReadOnlySpan<byte>.Empty);

    public static byte[] PackDataPackage(byte recipeId, IReadOnlyList<RtdeOutputField> fields, IReadOnlyDictionary<string, object?> values)
    {
        var payload = new List<byte> { recipeId };
        foreach (var field in fields)
        {
            if (!values.TryGetValue(field.Name, out var value) || value is null)
            {
                throw new InvalidOperationException($"Missing RTDE test value for '{field.Name}'.");
            }

            payload.AddRange(PackValue(field.Type, value));
        }

        return PackHeaderAndPayload(RtdeProtocol.DataPackage, payload.ToArray());
    }

    public static bool ReadProtocolVersionAccepted(ReadOnlySpan<byte> payload) =>
        payload.Length == 1 && payload[0] != 0;

    public static bool ReadStartAccepted(ReadOnlySpan<byte> payload) =>
        payload.Length == 1 && payload[0] != 0;

    public static RtdeOutputLayout ParseSetupOutputs(ReadOnlySpan<byte> payload, IReadOnlyList<string> requestedKeys)
    {
        if (payload.Length < 1)
        {
            throw new InvalidOperationException("RTDE setup outputs response is empty.");
        }

        var recipeId = payload[0];
        var typesText = Encoding.ASCII.GetString(payload[1..]);
        var types = typesText.Split(',', StringSplitOptions.None);
        if (types.Length != requestedKeys.Count)
        {
            throw new InvalidOperationException(
                $"RTDE setup outputs returned {types.Length} type(s) for {requestedKeys.Count} requested key(s).");
        }

        var fields = new List<RtdeOutputField>(types.Length);
        var rejected = new List<string>();
        for (var i = 0; i < types.Length; i++)
        {
            var type = ParseType(types[i]);
            if (type is RtdeValueType.NotFound or RtdeValueType.InUse)
            {
                rejected.Add(requestedKeys[i]);
                continue;
            }

            if (type == RtdeValueType.Unknown)
            {
                rejected.Add(requestedKeys[i]);
                continue;
            }

            fields.Add(new RtdeOutputField(requestedKeys[i], type));
        }

        return new RtdeOutputLayout(recipeId, fields, rejected);
    }

    public static Dictionary<string, object?> UnpackData(ReadOnlySpan<byte> payload, RtdeOutputLayout layout)
    {
        if (payload.Length < 1)
        {
            throw new InvalidOperationException("RTDE data package is empty.");
        }

        var recipeId = payload[0];
        if (recipeId != layout.RecipeId)
        {
            throw new InvalidOperationException($"RTDE data recipe id {recipeId} does not match setup id {layout.RecipeId}.");
        }

        var offset = 1;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in layout.Fields)
        {
            var (value, size) = UnpackValue(payload[offset..], field.Type);
            values[field.Name] = value;
            offset += size;
        }

        return values;
    }

    public static RtdeValueType ParseType(string typeName)
    {
        var name = typeName.Trim();
        if (name.Equals("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return RtdeValueType.NotFound;
        }

        if (name.Equals("IN_USE", StringComparison.OrdinalIgnoreCase))
        {
            return RtdeValueType.InUse;
        }

        return name.ToUpperInvariant() switch
        {
            "BOOL" => RtdeValueType.Bool,
            "UINT8" or "UINT8_T" => RtdeValueType.Uint8,
            "UINT32" or "UINT32_T" => RtdeValueType.Uint32,
            "UINT64" or "UINT64_T" => RtdeValueType.Uint64,
            "INT32" or "INT32_T" => RtdeValueType.Int32,
            "DOUBLE" => RtdeValueType.Double,
            "VECTOR3D" => RtdeValueType.Vector3D,
            "VECTOR6D" => RtdeValueType.Vector6D,
            "VECTOR6INT32" => RtdeValueType.Vector6Int32,
            "VECTOR6UINT32" => RtdeValueType.Vector6Uint32,
            _ => RtdeValueType.Unknown
        };
    }

    public static byte[] PackSetupOutputsResponse(byte recipeId, IEnumerable<string> types)
    {
        var typesBytes = Encoding.ASCII.GetBytes(string.Join(',', types));
        var payload = new byte[1 + typesBytes.Length];
        payload[0] = recipeId;
        typesBytes.CopyTo(payload.AsSpan(1));
        return PackHeaderAndPayload(RtdeProtocol.SetupOutputs, payload);
    }

    public static byte[] PackBoolResponse(byte command, bool accepted)
    {
        return PackHeaderAndPayload(command, accepted ? [1] : [0]);
    }

    private static byte[] PackValue(RtdeValueType type, object value) => type switch
    {
        RtdeValueType.Bool => [(bool)value ? (byte)1 : (byte)0],
        RtdeValueType.Uint8 => [(byte)Convert.ToByte(value, CultureInfo.InvariantCulture)],
        RtdeValueType.Uint32 => PackUInt32(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
        RtdeValueType.Uint64 => PackUInt64(Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
        RtdeValueType.Int32 => PackInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
        RtdeValueType.Double => PackDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        RtdeValueType.Vector3D => PackDoubles((double[])value),
        RtdeValueType.Vector6D => PackDoubles((double[])value),
        RtdeValueType.Vector6Int32 => PackInt32s((int[])value),
        RtdeValueType.Vector6Uint32 => PackUInt32s((uint[])value),
        _ => throw new InvalidOperationException($"Cannot pack RTDE type {type}.")
    };

    private static (object Value, int Size) UnpackValue(ReadOnlySpan<byte> buffer, RtdeValueType type) => type switch
    {
        RtdeValueType.Bool when buffer.Length >= 1 => (buffer[0] != 0, 1),
        RtdeValueType.Uint8 when buffer.Length >= 1 => (buffer[0], 1),
        RtdeValueType.Uint32 when buffer.Length >= 4 => (BinaryPrimitives.ReadUInt32BigEndian(buffer), 4),
        RtdeValueType.Uint64 when buffer.Length >= 8 => (BinaryPrimitives.ReadUInt64BigEndian(buffer), 8),
        RtdeValueType.Int32 when buffer.Length >= 4 => (BinaryPrimitives.ReadInt32BigEndian(buffer), 4),
        RtdeValueType.Double when buffer.Length >= 8 => (ReadDoubleBigEndian(buffer), 8),
        RtdeValueType.Vector3D when buffer.Length >= 24 => (ReadDoubles(buffer, 3), 24),
        RtdeValueType.Vector6D when buffer.Length >= 48 => (ReadDoubles(buffer, 6), 48),
        RtdeValueType.Vector6Int32 when buffer.Length >= 24 => (ReadInt32s(buffer, 6), 24),
        RtdeValueType.Vector6Uint32 when buffer.Length >= 24 => (ReadUInt32s(buffer, 6), 24),
        _ => throw new InvalidOperationException($"RTDE data package is truncated for type {type}.")
    };

    private static byte[] PackUInt32(uint value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] PackUInt64(ulong value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] PackInt32(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] PackDouble(double value)
    {
        var buffer = new byte[8];
        WriteDoubleBigEndian(buffer, value);
        return buffer;
    }

    private static byte[] PackDoubles(double[] values)
    {
        var buffer = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            WriteDoubleBigEndian(buffer.AsSpan(i * 8, 8), values[i]);
        }

        return buffer;
    }

    private static byte[] PackInt32s(int[] values)
    {
        var buffer = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(i * 4, 4), values[i]);
        }

        return buffer;
    }

    private static byte[] PackUInt32s(uint[] values)
    {
        var buffer = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(i * 4, 4), values[i]);
        }

        return buffer;
    }

    private static double[] ReadDoubles(ReadOnlySpan<byte> buffer, int count)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadDoubleBigEndian(buffer.Slice(i * 8, 8));
        }

        return values;
    }

    private static int[] ReadInt32s(ReadOnlySpan<byte> buffer, int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(i * 4, 4));
        }

        return values;
    }

    private static uint[] ReadUInt32s(ReadOnlySpan<byte> buffer, int count)
    {
        var values = new uint[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(i * 4, 4));
        }

        return values;
    }

    private static void WriteDoubleBigEndian(Span<byte> destination, double value)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination, BitConverter.DoubleToInt64Bits(value));
    }

    private static double ReadDoubleBigEndian(ReadOnlySpan<byte> source) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(source));
}
