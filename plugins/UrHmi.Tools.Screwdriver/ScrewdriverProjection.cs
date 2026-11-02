using System.Globalization;
using UrHmi.Domain;
using UrHmi.Domain.Indicators;
using UrHmi.Domain.Plc;

namespace UrHmi.Tools.Screwdriver;

public sealed record ScrewdriverField(
    string Text,
    Freshness Freshness,
    string Caption,
    IndicatorKind Kind);

public sealed record ScrewdriverSnapshot(
    ScrewdriverField Status,
    ScrewdriverField Result,
    ScrewdriverField Torque,
    ScrewdriverField Angle,
    ScrewdriverField CycleCount,
    ScrewdriverField Program,
    ScrewdriverField Fault,
    ScrewdriverField Enabled,
    bool ActuationAvailable);

public static class ScrewdriverProjection
{
    public static ScrewdriverSnapshot Project(PlcSnapshot plc, SpinBridgeReading? spinBridge = null)
    {
        ArgumentNullException.ThrowIfNull(plc);
        spinBridge ??= SpinBridgeReading.Unavailable();

        var torque = FormatMeasured(spinBridge.TorqueNm, " Nm");
        var angle = FormatMeasured(spinBridge.AngleDeg, "°");
        if (spinBridge.Channel.Link is LinkState.Disconnected or LinkState.Faulted
            && torque.Freshness == Freshness.Unavailable)
        {
            torque = torque with { Kind = IndicatorKind.CommsLoss };
            angle = angle with { Kind = IndicatorKind.CommsLoss };
        }

        var mapFreshness = plc.Tags.Freshness;
        var tags = plc.Tags.Value;
        var actuation = plc.Channel.Link == LinkState.Connected;

        if (mapFreshness == Freshness.Unavailable || tags is null)
        {
            var empty = Unavailable();
            return new ScrewdriverSnapshot(
                empty with { Kind = IndicatorKind.CommsLoss },
                empty,
                torque,
                angle,
                empty,
                empty,
                empty,
                empty,
                ActuationAvailable: false);
        }

        var status = ReadText(tags, ScrewdriverTags.Status, mapFreshness);
        var result = ReadOptionalText(tags, ScrewdriverTags.Result, mapFreshness);
        var cycles = ReadNumber(tags, ScrewdriverTags.CycleCount, mapFreshness, "", "0");
        var program = ReadOptionalText(tags, ScrewdriverTags.ProgramId, mapFreshness);
        var fault = ReadOptionalText(tags, ScrewdriverTags.FaultCode, mapFreshness);
        var enabled = ReadEnabled(tags, mapFreshness);

        return new ScrewdriverSnapshot(
            status with { Kind = KindForStatus(status) },
            result with { Kind = KindForResult(result) },
            torque,
            angle,
            cycles,
            program,
            fault with { Kind = KindForFault(fault) },
            enabled,
            actuation);
    }

    public static string CaptionFor(Freshness freshness) => freshness switch
    {
        Freshness.Live => string.Empty,
        Freshness.Stale => "dato no actualizado",
        _ => "sin dato"
    };

    private static ScrewdriverField Unavailable() =>
        new("—", Freshness.Unavailable, CaptionFor(Freshness.Unavailable), IndicatorKind.Normal);

    private static ScrewdriverField FormatMeasured(TimedValue<double> value, string suffix)
    {
        if (value.Freshness == Freshness.Unavailable || value.LastValidUtc is null)
        {
            return Unavailable() with
            {
                Kind = value.Freshness == Freshness.Unavailable ? IndicatorKind.CommsLoss : IndicatorKind.Normal
            };
        }

        var text = value.Value.ToString("0.##", CultureInfo.InvariantCulture) + suffix;
        return new ScrewdriverField(text, value.Freshness, CaptionFor(value.Freshness), IndicatorKind.Normal);
    }

    private static ScrewdriverField ReadText(
        IReadOnlyDictionary<string, PlcValue> tags,
        string key,
        Freshness mapFreshness)
    {
        if (!tags.TryGetValue(key, out var value) || value.Value is null)
        {
            return Unavailable();
        }

        var text = Convert.ToString(value.Value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unavailable();
        }

        var freshness = Combine(mapFreshness, value.Freshness);
        return new ScrewdriverField(text.Trim(), freshness, CaptionFor(freshness), IndicatorKind.Normal);
    }

    private static ScrewdriverField ReadOptionalText(
        IReadOnlyDictionary<string, PlcValue> tags,
        string key,
        Freshness mapFreshness)
    {
        if (!tags.TryGetValue(key, out var value))
        {
            return Unavailable();
        }

        var freshness = Combine(mapFreshness, value.Freshness);
        var text = Convert.ToString(value.Value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ScrewdriverField("—", freshness, CaptionFor(freshness), IndicatorKind.Normal);
        }

        return new ScrewdriverField(text, freshness, CaptionFor(freshness), IndicatorKind.Normal);
    }

    private static ScrewdriverField ReadNumber(
        IReadOnlyDictionary<string, PlcValue> tags,
        string key,
        Freshness mapFreshness,
        string suffix,
        string format = "0.##")
    {
        if (!tags.TryGetValue(key, out var value) || value.Value is null)
        {
            return Unavailable();
        }

        if (!TryToDouble(value.Value, out var number))
        {
            return Unavailable();
        }

        var freshness = Combine(mapFreshness, value.Freshness);
        var text = number.ToString(format, CultureInfo.InvariantCulture) + suffix;
        return new ScrewdriverField(text, freshness, CaptionFor(freshness), IndicatorKind.Normal);
    }

    private static ScrewdriverField ReadEnabled(
        IReadOnlyDictionary<string, PlcValue> tags,
        Freshness mapFreshness)
    {
        if (!tags.TryGetValue(ScrewdriverTags.Enabled, out var value) || value.Value is null)
        {
            return Unavailable();
        }

        var freshness = Combine(mapFreshness, value.Freshness);
        var text = IsTruthy(value.Value) ? "Enabled" : "Disabled";
        return new ScrewdriverField(text, freshness, CaptionFor(freshness), IndicatorKind.Normal);
    }

    private static IndicatorKind KindForStatus(ScrewdriverField status)
    {
        if (status.Freshness == Freshness.Unavailable)
        {
            return IndicatorKind.CommsLoss;
        }

        return status.Text.ToLowerInvariant() switch
        {
            "fault" or "error" or "nok" => IndicatorKind.Fault,
            "running" or "ready" or "idle" or "ok" => IndicatorKind.Normal,
            _ => IndicatorKind.Warning
        };
    }

    private static IndicatorKind KindForResult(ScrewdriverField result)
    {
        if (result.Freshness == Freshness.Unavailable)
        {
            return IndicatorKind.Normal;
        }

        return result.Text.ToLowerInvariant() switch
        {
            "nok" or "ng" or "fail" => IndicatorKind.Fault,
            "ok" or "pass" or "none" => IndicatorKind.Normal,
            _ => IndicatorKind.Warning
        };
    }

    private static IndicatorKind KindForFault(ScrewdriverField fault)
    {
        if (fault.Freshness == Freshness.Unavailable)
        {
            return IndicatorKind.Normal;
        }

        return string.Equals(fault.Text, "—", StringComparison.Ordinal)
            ? IndicatorKind.Normal
            : IndicatorKind.Fault;
    }

    private static Freshness Combine(Freshness map, Freshness tag)
    {
        if (map == Freshness.Unavailable || tag == Freshness.Unavailable)
        {
            return Freshness.Unavailable;
        }

        if (map == Freshness.Stale || tag == Freshness.Stale)
        {
            return Freshness.Stale;
        }

        return Freshness.Live;
    }

    private static bool TryToDouble(object value, out double number)
    {
        switch (value)
        {
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case decimal m:
                number = (double)m;
                return true;
            case string s:
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    private static bool IsTruthy(object value) => value switch
    {
        bool flag => flag,
        int number => number != 0,
        long number => number != 0,
        double number => Math.Abs(number) > double.Epsilon,
        string text when bool.TryParse(text, out var flag) => flag,
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) =>
            number != 0,
        _ => false
    };
}
