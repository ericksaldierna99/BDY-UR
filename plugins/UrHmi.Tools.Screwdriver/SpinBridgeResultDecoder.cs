using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace UrHmi.Tools.Screwdriver;

public readonly record struct SpinBridgeMeasuredPair(double? TorqueNm, double? AngleDeg, double? DepthMm = null);

public static class SpinBridgeResultDecoder
{
    public const string FinalTorqueNm = "Final Torque (Nm)";
    public const string FinalAngleDeg = "Final Angle (°)";
    public const string FinalInsertionDepthMm = "Final Insertion Depth (mm)";

    public static SpinBridgeMeasuredPair Decode(
        IReadOnlyDictionary<string, object?>? fields = null,
        object? overallResultValues = null,
        object? latestResult = null)
    {
        var pair = Extract(OpcValues.Unwrap(overallResultValues));
        pair = Merge(pair, Extract(OpcValues.Unwrap(latestResult)));
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
            null => "LatestResult value is empty.",
            _ => "Joining result did not contain overallResultValues Final Torque/Angle."
        };
    }

    private static SpinBridgeMeasuredPair Merge(SpinBridgeMeasuredPair left, SpinBridgeMeasuredPair right) =>
        new(
            left.TorqueNm ?? right.TorqueNm,
            left.AngleDeg ?? right.AngleDeg,
            left.DepthMm ?? right.DepthMm);

    private static SpinBridgeMeasuredPair ExtractMap(IReadOnlyDictionary<string, object?> fields)
    {
        if (TryFindOverall(fields, out var overall, 0))
        {
            var fromOverall = ExtractOverall(overall);
            if (fromOverall.TorqueNm is not null || fromOverall.AngleDeg is not null)
            {
                return fromOverall;
            }
        }

        double? torque = null;
        double? angle = null;
        double? depth = null;
        foreach (var (name, value) in fields)
        {
            Assign(name, value, ref torque, ref angle, ref depth);
        }

        if (torque is null && angle is null)
        {
            return Extract(fields);
        }

        return new SpinBridgeMeasuredPair(torque, angle, depth);
    }

    private static SpinBridgeMeasuredPair Extract(object? value)
    {
        if (TryFindOverall(value, out var overall, 0))
        {
            var fromOverall = ExtractOverall(overall);
            if (fromOverall.TorqueNm is not null || fromOverall.AngleDeg is not null)
            {
                return fromOverall;
            }
        }

        double? torque = null;
        double? angle = null;
        double? depth = null;
        Walk(value, ref torque, ref angle, ref depth, [], 0);
        return new SpinBridgeMeasuredPair(torque, angle, depth);
    }

    private static SpinBridgeMeasuredPair ExtractOverall(object? overall)
    {
        double? torque = null;
        double? angle = null;
        double? depth = null;
        foreach (var entry in Enumerate(overall))
        {
            if (!TryResultValue(entry, out var name, out var measured))
            {
                Walk(entry, ref torque, ref angle, ref depth, [], 0);
                continue;
            }

            Assign(name, measured, ref torque, ref angle, ref depth);
        }

        return new SpinBridgeMeasuredPair(torque, angle, depth);
    }

    private static bool TryFindOverall(object? value, out object? overall, int depth)
    {
        overall = null;
        if (value is null || depth > 6)
        {
            return false;
        }

        value = OpcValues.Unwrap(value);
        if (value is null)
        {
            return false;
        }

        if (TryGetNamedMember(value, "overallResultValues", out overall) && overall is not null)
        {
            return true;
        }

        if (TryGetNamedMember(value, "ResultContent", out var content) && content is not null)
        {
            if (TryFindOverall(content, out overall, depth + 1))
            {
                return true;
            }

            foreach (var item in Enumerate(content))
            {
                if (TryFindOverall(item, out overall, depth + 1))
                {
                    return true;
                }
            }
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (TryFindOverall(entry.Value, out overall, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (value is IEnumerable enumerable
            and not string
            and not byte[]
            and not XNode
            and not XmlNode)
        {
            foreach (var item in enumerable)
            {
                if (TryFindOverall(item, out overall, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetNamedMember(object value, string name, out object? member)
    {
        member = null;
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (key is not null && NamesEqual(key, name))
                {
                    member = OpcValues.Unwrap(entry.Value);
                    return member is not null;
                }
            }

            return false;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || !NamesEqual(property.Name, name))
            {
                continue;
            }

            try
            {
                member = OpcValues.Unwrap(property.GetValue(value));
            }
            catch (Exception)
            {
                return false;
            }

            return member is not null;
        }

        return false;
    }

    private static bool TryResultValue(object? entry, out string? name, out object? measured)
    {
        name = null;
        measured = null;
        if (entry is null)
        {
            return false;
        }

        entry = OpcValues.Unwrap(entry);
        if (entry is null || entry is XNode or XmlNode)
        {
            return false;
        }

        string? foundName = null;
        string? quantityName = null;
        object? foundMeasured = null;
        foreach (var (key, value) in Members(entry))
        {
            if (IsNameProperty(key))
            {
                foundName = ReadName(value);
            }
            else if (IsPhysicalQuantityProperty(key))
            {
                quantityName = NameFromPhysicalQuantity(value);
            }
            else if (IsMeasuredProperty(key))
            {
                foundMeasured = value;
            }
        }

        foundName = string.IsNullOrWhiteSpace(foundName) ? quantityName : foundName;
        if (string.IsNullOrWhiteSpace(foundName))
        {
            return false;
        }

        name = foundName;
        measured = foundMeasured ?? entry;
        return true;
    }

    private static IEnumerable<(string Name, object? Value)> Members(object value)
    {
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                if (key is not null)
                {
                    yield return (key, OpcValues.Unwrap(entry.Value));
                }
            }

            yield break;
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

            yield return (property.Name, OpcValues.Unwrap(propertyValue));
        }
    }

    private static IEnumerable<object?> Enumerate(object? value)
    {
        value = OpcValues.Unwrap(value);
        switch (value)
        {
            case null:
                yield break;
            case string or byte[] or XNode or XmlNode:
                yield return value;
                yield break;
            case IEnumerable enumerable and not IDictionary:
                foreach (var item in enumerable)
                {
                    yield return item;
                }

                yield break;
            default:
                yield return value;
                yield break;
        }
    }

    private static void Walk(
        object? value,
        ref double? torque,
        ref double? angle,
        ref double? depth,
        HashSet<object> visited,
        int depthLevel)
    {
        if (value is null || depthLevel > 8 || torque is not null && angle is not null && depth is not null)
        {
            return;
        }

        value = OpcValues.Unwrap(value);
        if (value is null)
        {
            return;
        }

        switch (value)
        {
            case byte[]:
                return;
            case string text:
                Assign(text, null, ref torque, ref angle, ref depth);
                return;
            case XmlNode node:
                WalkXmlElement(XElement.Load(node.CreateNavigator()!.ReadSubtree()), ref torque, ref angle, ref depth);
                return;
            case XElement element:
                WalkXmlElement(element, ref torque, ref angle, ref depth);
                return;
            case XDocument document when document.Root is { } root:
                WalkXmlElement(root, ref torque, ref angle, ref depth);
                return;
        }

        if (!IsScalar(value) && !visited.Add(value))
        {
            return;
        }

        if (TryResultValue(value, out var resultName, out var measured))
        {
            Assign(resultName, measured, ref torque, ref angle, ref depth);
            Walk(measured, ref torque, ref angle, ref depth, visited, depthLevel + 1);
            return;
        }

        if (TryGetNamedMember(value, "ResultContent", out var body) && body is not null)
        {
            Walk(body, ref torque, ref angle, ref depth, visited, depthLevel + 1);
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture);
                Assign(key, entry.Value, ref torque, ref angle, ref depth);
                Walk(entry.Value, ref torque, ref angle, ref depth, visited, depthLevel + 1);
            }

            return;
        }

        if (value is IEnumerable enumerable and not string and not byte[])
        {
            foreach (var item in enumerable)
            {
                Walk(item, ref torque, ref angle, ref depth, visited, depthLevel + 1);
                if (torque is not null && angle is not null && depth is not null)
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

        foreach (var (propertyName, propertyValue) in Members(value))
        {
            Assign(propertyName, propertyValue, ref torque, ref angle, ref depth);
            Walk(propertyValue, ref torque, ref angle, ref depth, visited, depthLevel + 1);
        }
    }

    private static void WalkXmlElement(XElement root, ref double? torque, ref double? angle, ref double? depth)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            var local = element.Name.LocalName;
            Assign(local, element.Value, ref torque, ref angle, ref depth);
            var name = element.Attribute("Name")?.Value
                ?? element.Attribute("name")?.Value
                ?? element.Element(element.Name.Namespace + "Name")?.Value
                ?? element.Element(element.Name.Namespace + "name")?.Value
                ?? FindChild(element, "Name", "BrowseName", "DisplayName");
            var measured = FindChild(
                element,
                "MeasuredValue",
                "measuredValue",
                "Value",
                "value",
                "ResultValue");
            if (!string.IsNullOrWhiteSpace(name))
            {
                Assign(name, measured ?? element.Value, ref torque, ref angle, ref depth);
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

    private static void Assign(
        string? name,
        object? value,
        ref double? torque,
        ref double? angle,
        ref double? depth)
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
        else if (depth is null && IsDepthName(name))
        {
            depth = number;
        }
    }

    internal static bool IsTorqueName(string name)
    {
        var normalized = Normalize(name);
        return normalized is "final torque (nm)" or "final torque nm" or "finaltorque" or "finaltorquenm"
            or "torque" or "torquenm"
            || normalized.Contains("final torque", StringComparison.Ordinal)
            || (normalized.Contains("torque", StringComparison.Ordinal)
                && normalized.Contains("nm", StringComparison.Ordinal)
                && (normalized.Contains("final", StringComparison.Ordinal) || normalized is "torque nm"));
    }

    internal static bool IsAngleName(string name)
    {
        var normalized = Normalize(name);
        return normalized is "final angle (°)" or "final angle (deg)" or "final angle deg" or "finalangle"
            or "finalangledeg" or "angle" or "angledeg" or "angle deg"
            || normalized.Contains("final angle", StringComparison.Ordinal)
            || (normalized.Contains("angle", StringComparison.Ordinal)
                && normalized.Contains("final", StringComparison.Ordinal)
                && (normalized.Contains("deg", StringComparison.Ordinal) || normalized.Contains('°')));
    }

    internal static bool IsDepthName(string name)
    {
        var normalized = Normalize(name);
        return normalized.Contains("insertion depth", StringComparison.Ordinal)
            || normalized is "final insertion depth (mm)" or "finalinsertiondepth";
    }

    internal static bool IsOverallName(string name) =>
        Normalize(name).Replace(" ", "", StringComparison.Ordinal)
            .Contains("overallresultvalues", StringComparison.Ordinal);

    private static bool NamesEqual(string actual, string expected) =>
        string.Equals(
            Normalize(actual).Replace(" ", "", StringComparison.Ordinal),
            Normalize(expected).Replace(" ", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string name)
    {
        var text = ReadName(name) ?? name;
        return string.Join(
            " ",
            text.Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

    private static bool IsPhysicalQuantityProperty(string name) =>
        name is "PhysicalQuantity" or "physicalQuantity";

    private static string? NameFromPhysicalQuantity(object? value)
    {
        if (TryToDouble(value, out var number))
        {
            return (int)number switch
            {
                2 => FinalTorqueNm,
                3 => FinalAngleDeg,
                6 => FinalInsertionDepthMm,
                _ => null
            };
        }

        var text = ReadName(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (IsTorqueName(text))
        {
            return FinalTorqueNm;
        }

        if (IsAngleName(text))
        {
            return FinalAngleDeg;
        }

        return IsDepthName(text) || Normalize(text) is "distance" or "length" or "depth"
            ? FinalInsertionDepthMm
            : null;
    }

    private static bool IsMeasuredProperty(string name) =>
        name is "MeasuredValue" or "measuredValue" or "Measured" or "measured";

    private static bool IsValueProperty(string name) =>
        IsMeasuredProperty(name)
        || name is "Value" or "value" or "ResultValue" or "Body";

    private static bool TryExtractMeasured(object? value, out double number)
    {
        number = 0;
        if (value is null || IsScalar(value) || value is byte[])
        {
            return false;
        }

        foreach (var (key, inner) in Members(value))
        {
            if (IsValueProperty(key) && TryToDouble(inner, out number))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScalar(object value)
    {
        if (value is string or byte[] or char[])
        {
            return true;
        }

        var type = value.GetType();
        return type.IsPrimitive || type.IsEnum
            || value is decimal or DateTime or DateTimeOffset or Guid or TimeSpan;
    }

    internal static bool TryToDouble(object? value, out double number)
    {
        value = OpcValues.Unwrap(value);
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
