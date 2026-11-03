using System.Xml.Linq;
using Opc.Ua;

namespace UrHmi.Tools.Screwdriver;

/// <summary>
/// Unwraps OPC UA DataValue / Variant / ExtensionObject / IStructure so the
/// decoder sees <c>overallResultValues</c> as dictionaries of named fields.
/// ComplexTypeSystem 2.0 stand-ins are <see cref="IStructure"/> (indexer +
/// GetFields) whose nested joining values are <see cref="ArrayOf{T}"/> of
/// <see cref="ExtensionObject"/>, not CLR properties named OverallResultValues.
/// </summary>
internal static class OpcValues
{
#pragma warning disable CS0618
    public static object? Unwrap(object? value) => Unwrap(value, []);

    private static object? Unwrap(object? value, HashSet<object> visited)
    {
        switch (value)
        {
            case null:
                return null;
            case DataValue data:
                return Unwrap(data.WrappedValue, visited);
            case Variant variant:
                return UnwrapVariant(variant, visited);
            case ExtensionObject extension:
                return UnwrapExtension(extension, visited);
            case IStructure structure:
                return StructureToDictionary(structure, visited);
            case LocalizedText text:
                return text.Text;
            case QualifiedName name:
                return name.Name;
            case XmlElement xml when !xml.IsNull && !string.IsNullOrWhiteSpace(xml.OuterXml):
                return ParseXml(xml.OuterXml) ?? xml.OuterXml;
            case IConvertableToArray convertible:
                return Unwrap(convertible.ToArray(), visited);
            case Array array and not byte[] and not char[]:
                return UnwrapItems(array, visited);
            default:
                return value;
        }
    }

    private static object? UnwrapVariant(Variant variant, HashSet<object> visited)
    {
        if (variant.IsNull)
        {
            return null;
        }

        if (variant.TryGetValue(out ExtensionObject extension) && !extension.IsNull)
        {
            return UnwrapExtension(extension, visited);
        }

        if (variant.TryGetValue(out ArrayOf<ExtensionObject> extensions) && !extensions.IsNull)
        {
            return UnwrapItems(extensions, visited);
        }

        if (variant.TryGetValue(out ArrayOf<Variant> variants) && !variants.IsNull)
        {
            return UnwrapItems(variants, visited);
        }

        if (variant.TryGetValue(out XmlElement xml) && !xml.IsNull && !string.IsNullOrWhiteSpace(xml.OuterXml))
        {
            return ParseXml(xml.OuterXml) ?? xml.OuterXml;
        }

        var boxed = variant.AsBoxedObject(Variant.BoxingBehavior.Legacy)
            ?? variant.AsBoxedObject(Variant.BoxingBehavior.None);
        return Unwrap(boxed, visited);
    }

    private static object? UnwrapExtension(ExtensionObject extension, HashSet<object> visited)
    {
        if (extension.IsNull)
        {
            return null;
        }

        if (extension.TryGetValue(out IEncodeable? encodeable, null!) && encodeable is not null)
        {
            return Unwrap(encodeable, visited);
        }

        return Unwrap(extension.Body, visited);
    }

    private static object UnwrapItems<T>(ArrayOf<T> values, HashSet<object> visited)
    {
        var items = new object?[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            items[i] = Unwrap(values[i], visited);
        }

        return items;
    }

    private static object UnwrapItems(Array array, HashSet<object> visited)
    {
        var items = new object?[array.Length];
        for (var i = 0; i < array.Length; i++)
        {
            items[i] = Unwrap(array.GetValue(i), visited);
        }

        return items;
    }

    private static object? ParseXml(string outerXml)
    {
        try
        {
            return XElement.Parse(outerXml);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object StructureToDictionary(IStructure structure, HashSet<object> visited)
    {
        if (!visited.Add(structure))
        {
            return structure;
        }

        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in structure.GetFields())
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                continue;
            }

            map[field.Name] = Unwrap(structure[field.Name], visited);
        }

        return map;
    }
#pragma warning restore CS0618
}
