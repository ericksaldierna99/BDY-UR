using Opc.Ua;

namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Unwraps OPC UA DataValue / Variant / ExtensionObject layers so the decoder
/// sees JoiningResultDataType or ResultDataType.ResultContent.
/// </summary>
internal static class OpcValues
{
#pragma warning disable CS0618
    public static object? Unwrap(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DataValue data:
                return Unwrap(data.WrappedValue);
            case Variant variant:
                return variant.IsNull ? null : Unwrap(variant.AsBoxedObject(Variant.BoxingBehavior.Legacy));
            case ExtensionObject extension:
                return extension.IsNull ? null : Unwrap(extension.Body);
            case LocalizedText text:
                return text.Text;
            case QualifiedName name:
                return name.Name;
            case Array array and not byte[] and not char[]:
                var items = new object?[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    items[i] = Unwrap(array.GetValue(i));
                }

                return items;
            default:
                return value;
        }
    }
#pragma warning restore CS0618
}
