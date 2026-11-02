using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace UrHmi.Tools.Screwdriver;

public readonly record struct SpinBridgeMeasuredPair(double? TorqueNm, double? AngleDeg);

public static class SpinBridgeResultDecoder
{
    public const string FinalTorqueNm = "Final Torque (Nm)";
    public const string FinalAngleDeg = "Final Angle (°)";

    public static SpinBridgeMeasuredPair Decode(
        IReadOnlyDictionary<string, object?>? fields = null,
        object? overallResultValues = null,
        object? latestResult = null)
    {
        var pair = Extract(overallResultValues);
        pair = Merge(pair, Extract(latestResult));
        if (fields is not null)
        {
            pair = Merge(pair, ExtractMap(fields));
        }

        return pair;
    }

    internal static string? DescribeFailure(object? latest, SpinBridgeMeasuredPair pair)
    {
        if (pair.TorqueNm is not null || pair.AngleDeg is not null)
        {
            return null;
        }

        return latest switch
        {
            byte[] bytes =>
                $"LatestResult is {bytes.Length} undecoded bytes. JoiningResultDataType did not decode.",
            _ => null
        };
    }

    private static SpinBridgeMeasuredPair Merge(SpinBridgeMeasuredPair left, SpinBridgeMeasuredPair right) =>
        new(left.TorqueNm ?? right.TorqueNm, left.AngleDeg ?? right.AngleDeg);

    private static SpinBridgeMeasuredPair ExtractMap(IReadOnlyDictionary<string, object?> fields)
    {
        double? torque = null;
        double? angle = null;
        foreach (var (name, value) in fields)
        {
            Assign(name, value, ref torque, ref angle);
        }

        if (torque is null && angle is null)
        {
            return ExtractFromValues(fields.Values);
        }

        return new SpinBridgeMeasuredPair(torque, angle);
    }

    private static SpinBridgeMeasuredPair Extract(object? value)
    {
        double? torque = null;
        double? angle = null;
        Walk(value, ref torque, ref angle, [], 0);
        return new SpinBridgeMeasuredPair(torque, angle);
    }

    private static SpinBridgeMeasuredPair ExtractFromValues(IEnumerable<object?> values)
    {
        double? torque = null;
        double? angle = null;
        foreach (var value in values)
        {
            Walk(value, ref torque, ref angle, [], 0);
            if (torque is not null && angle is not null)
            {
                break;
            }
        }

        return new SpinBridgeMeasuredPair(torque, angle);
    }

    private static void Walk(
        object? value,
        ref double? torque,
        ref double? angle,
        HashSet<object> visited,
        int depth)
    {
        if (value is null || depth > 8 || torque is not null && angle is not null)
        {
            return;
        }

        switch (value)
        {
            case byte[]:
                return;
            case string text:
                Assign(text, null, ref torque, ref angle);
                return;
            case XmlNode node:
                WalkXmlElement(XElement.Load(node.CreateNavigator()!.ReadSubtree()), ref torque, ref angle);
                return;
            case XElement element:
                WalkXmlElement(element, ref torque, ref angle);
                return;
            case XDocument document when document.Root is { } root:
                WalkXmlElement(root, ref torque, ref angle);
                return;
        }

        if (!IsScalar(value) && !visited.Add(value))
        {
            return;
        }

        if (TryNamedPair(value, out var name, out var namedValue))
        {
            Assign(name, namedValue, ref torque, ref angle);
            Walk(namedValue, ref torque, ref angle, visited, depth + 1);
            return;
        }

        if (value is IDictionary dictionary)
        {
            string? dictName = null;
            object? dictMeasured = null;
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (key is not null && IsNameProperty(key))
                {
                    dictName = ReadName(entry.Value);
                }
                else if (key is not null && IsValueProperty(key))
                {
                    dictMeasured = entry.Value;
                }

                Assign(key, entry.Value, ref torque, ref angle);
                Walk(entry.Value, ref torque, ref angle, visited, depth + 1);
            }

            if (!string.IsNullOrWhiteSpace(dictName))
            {
                Assign(dictName, dictMeasured ?? dictionary, ref torque, ref angle);
            }

            return;
        }

        if (value is IEnumerable enumerable and not string and not byte[])
        {
            foreach (var item in enumerable)
            {
                Walk(item, ref torque, ref angle, visited, depth + 1);
                if (torque is not null && angle is not null)
                {
                    return;
                }
            }

            return;
        }

        if (IsScalar(value))
        {
            return;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception)
            {
                continue;
            }

            Assign(property.Name, propertyValue, ref torque, ref angle);
            Walk(propertyValue, ref torque, ref angle, visited, depth + 1);
        }
    }

    private static void WalkXmlElement(XElement root, ref double? torque, ref double? angle)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            var local = element.Name.LocalName;
            Assign(local, element.Value, ref torque, ref angle);
            var name = element.Attribute("Name")?.Value
                ?? element.Attribute("name")?.Value
                ?? element.Element(element.Name.Namespace + "Name")?.Value
                ?? element.Element(element.Name.Namespace + "name")?.Value
                ?? FindChild(element, "Name", "BrowseName", "DisplayName");
            var measured = FindChild(element, "MeasuredValue", "measuredValue", "Value", "value", "ResultValue");
            if (!string.IsNullOrWhiteSpace(name))
            {
                Assign(name, measured ?? element.Value, ref torque, ref angle);
            }
        }
    }

    private static string? FindChild(XElement element, params string[] names)
    {
        foreach (var child in element.Elements())
        {
            if (names.Any(name => string.Equals(child.Name.LocalName, name, StringComparison.OrdinalIgnoreCase)))
            {
                return child.Value;
            }
        }

        return null;
    }

    private static bool TryNamedPair(object value, out string? name, out object? namedValue)
    {
        name = null;
        namedValue = null;
        string? foundName = null;
        object? foundValue = null;
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception)
            {
                continue;
            }

            if (IsNameProperty(property.Name))
            {
                foundName = ReadName(propertyValue);
            }
            else if (IsValueProperty(property.Name))
            {
                foundValue = propertyValue;
            }
        }

        if (string.IsNullOrWhiteSpace(foundName) || foundValue is null)
        {
            return false;
        }

        name = foundName;
        namedValue = foundValue;
        return true;
    }

    private static void Assign(string? name, object? value, ref double? torque, ref double? angle)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!TryToDouble(value, out var number) && !TryExtractMeasured(value, out number))
        {
            return;
        }

        if (torque is null && IsTorqueName(name))
        {
            torque = number;
        }
        else if (angle is null && IsAngleName(name))
        {
            angle = number;
        }
    }

    internal static bool IsTorqueName(string name)
    {
        var normalized = Normalize(name);
        return normalized is "final torque (nm)" or "final torque nm" or "finaltorque" or "finaltorquenm"
            || normalized.Contains("final torque", StringComparison.Ordinal)
            || (normalized.Contains("torque", StringComparison.Ordinal)
                && normalized.Contains("nm", StringComparison.Ordinal)
                && normalized.Contains("final", StringComparison.Ordinal));
    }

    internal static bool IsAngleName(string name)
    {
        var normalized = Normalize(name);
        return normalized is "final angle (°)" or "final angle (deg)" or "final angle deg" or "finalangle"
            or "finalangledeg"
            || normalized.Contains("final angle", StringComparison.Ordinal)
            || (normalized.Contains("angle", StringComparison.Ordinal)
                && normalized.Contains("final", StringComparison.Ordinal)
                && (normalized.Contains("deg", StringComparison.Ordinal)
                    || normalized.Contains('°')));
    }

    internal static bool IsOverallName(string name) =>
        Normalize(name).Replace(" ", "", StringComparison.Ordinal)
            .Contains("overallresultvalues", StringComparison.Ordinal);

    private static string Normalize(string name)
    {
        var text = ReadName(name) ?? name;
        return string.Join(
            " ",
            text.Replace('°', '°').Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? ReadName(object? value) => value switch
    {
        null => null,
        string text => text.Trim(),
        _ => Convert.ToString(
                value.GetType().GetProperty("Text")?.GetValue(value) ?? value,
                CultureInfo.InvariantCulture)
            ?.Trim()
    };

    private static bool IsNameProperty(string name) =>
        name is "Name" or "name" or "BrowseName" or "DisplayName" or "Text";

    private static bool IsValueProperty(string name) =>
        name is "Value" or "value" or "MeasuredValue" or "measuredValue" or "ResultValue" or "Body"
            or "Measured";

    private static bool TryExtractMeasured(object? value, out double number)
    {
        number = 0;
        if (value is null || IsScalar(value) || value is byte[])
        {
            return false;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (key is not null && IsValueProperty(key) && TryToDouble(entry.Value, out number))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || !IsValueProperty(property.Name))
            {
                continue;
            }

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch (Exception)
            {
                continue;
            }

            if (TryToDouble(propertyValue, out number))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScalar(object value) =>
        value is string || value.GetType().IsPrimitive || value is IFormattable and not IEnumerable;

    internal static bool TryToDouble(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double d:
                number = d;
                return !double.IsNaN(d);
            case float f:
                number = f;
                return true;
            case decimal m:
                number = (double)m;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case uint u:
                number = u;
                return true;
            case short s:
                number = s;
                return true;
            case byte b:
                number = b;
                return true;
            case string text:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            default:
                var inner = value.GetType().GetProperty("Value")?.GetValue(value)
                    ?? value.GetType().GetProperty("WrappedValue")?.GetValue(value);
                if (inner is not null && !ReferenceEquals(inner, value))
                {
                    return TryToDouble(inner, out number);
                }

                number = 0;
                return false;
        }
    }
}
