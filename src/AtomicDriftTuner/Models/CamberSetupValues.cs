namespace AtomicDriftTuner.Models;

/// <summary>Standard AC camber serialization. Other controls and custom LUTs retain their separate support rules.</summary>
public static class CamberSetupValues
{
    public static bool IsCamber(string section) => new[] { "CAMBER_LF", "CAMBER_RF", "CAMBER_LR", "CAMBER_RR" }
        .Contains(section, StringComparer.OrdinalIgnoreCase);

    public static bool TryRawRange(SetupRangeDefinition? definition, out SetupRangeDefinition raw)
    {
        raw = new();
        if (definition is null || !IsCamber(definition.Section) || definition.CamberValueMode is not (0 or 1 or 2) ||
            definition.Min is not double min || definition.Max is not double max || !double.IsFinite(min) || !double.IsFinite(max) || min > max ||
            Math.Abs(min) > 10000 || Math.Abs(max) > 10000) return false;
        // CM uses a fixed 0.1 step for camber. Modes 0/1 serialize value / 0.1;
        // mode 2 serializes (value - MIN) / 0.1. Raw counts are not degrees.
        double offset = definition.CamberValueMode == 2 ? min : 0;
        double low = (min - offset) * 10, high = (max - offset) * 10;
        if (!Integer(low) || !Integer(high)) return false;
        raw = new() { Section = definition.Section, Min = Math.Round(low), Max = Math.Round(high), Step = 1,
            Source = definition.Source, Name = definition.Name, Units = "saved VALUE" };
        return true;
    }

    public static bool IsLegal(SetupRangeDefinition? definition, double value) => TryRawRange(definition, out var raw) &&
        double.IsFinite(value) && Integer(value) && value >= raw.Min!.Value - .000001 && value <= raw.Max!.Value + .000001;

    public static bool TrySetupValue(SetupRangeDefinition? definition, double raw, out double value)
    {
        value = 0;
        if (!IsLegal(definition, raw)) return false;
        value = raw * .1 + (definition!.CamberValueMode == 2 ? definition.Min!.Value : 0);
        return true;
    }

    public static string Describe(SetupRangeDefinition? definition) => TryRawRange(definition, out var raw)
        ? $"{raw.Min:0} .. {raw.Max:0} saved VALUE; step 1 = 0.1 setup unit" + (definition!.CamberValueMode == 2 ? "; offset from MIN" : "")
        : "Camber saved-value mapping unavailable; reload a supported car definition.";

    private static bool Integer(double value) => Math.Abs(value - Math.Round(value)) <= .000001;
}
