using System.Globalization;
using System.Text.RegularExpressions;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Read-only presentation of existing recommendations and immutable run snapshots.</summary>
public static class SetupComparisonPresentation
{
    public sealed record Row(string Key, string Setting, string Category, string Before, string After,
        string Difference, string Status, string Explanation, string Details, bool Changed, bool Unavailable = false)
    {
        public bool Emphasized => Changed || Unavailable;
        public string BeforeHeading { get; init; } = "Before";
        public string AfterHeading { get; init; } = "Recommended";
    }

    public static IReadOnlyList<Row> Proposed(CarSetupAnalysis analysis, bool generated, string goal = "") =>
        Sort(analysis.Parameters.Select(p =>
        {
            var changed = generated && p.Changed;
            var old = p.CurrentValue;
            var next = generated ? p.RecommendedValue ?? old : old;
            var delta = old is double a && next is double b && double.IsFinite(a) && double.IsFinite(b) ? b - a : (double?)null;
            var difference = delta is null ? "Unavailable" : !changed ? "No change" : Signed(delta.Value) + " saved units";
            if (changed && old is double c && next is double d && CamberSetupValues.TrySetupValue(p.Range, c, out var camberBefore) &&
                CamberSetupValues.TrySetupValue(p.Range, d, out var camberAfter)) difference = Signed(camberAfter - camberBefore) + " setup units";
            else if (changed && p.Range?.DirectValueRangeVerified == true && !string.IsNullOrWhiteSpace(p.Range.Units))
                difference = Signed(delta!.Value) + " " + p.Range.Units;
            var explanation = !generated ? "Generate a setup to see ADT's recommendations." : p.Reason;
            if (changed && goal.Length > 0) explanation = goal + "\n" + explanation;
            return new Row(p.Section, Label(p.Section, p.Range?.Name), Category(p.Category),
                ProposalValue(p, old, p.CurrentRaw, analysis), generated ? ProposalValue(p, next, p.RecommendedRaw, analysis) : "Not generated",
                generated ? difference : "—", !generated ? "Baseline" : changed ? "Proposed change" : "Unchanged",
                explanation, $"{p.Section}: saved VALUE {p.CurrentRaw} → {(generated ? p.RecommendedRaw : "not generated")}.\n{p.RangeText}\n{p.DecodedContext}",
                changed, generated && delta is null);
        }));

    public static IReadOnlyList<Row> Recorded(TuneVersion? before, TuneVersion? after)
    {
        var keys = (before?.Settings.Keys ?? Enumerable.Empty<string>()).Union(after?.Settings.Keys ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        return Sort(keys.Select(key =>
        {
            double old = 0, next = 0;
            var hasOld = before?.Settings.TryGetValue(key, out old) == true && double.IsFinite(old);
            var hasNew = after?.Settings.TryGetValue(key, out next) == true && double.IsFinite(next);
            var changed = hasOld && hasNew && Math.Abs(old - next) > .000001;
            var car = key.StartsWith("ACSetup.", StringComparison.Ordinal);
            var generated = key.StartsWith("Generated.", StringComparison.Ordinal);
            var oldMapping = car && hasOld ? Verified(before!.DecodedSetup, key[8..], old) : null;
            var newMapping = car && hasNew ? Verified(after!.DecodedSetup, key[8..], next) : null;
            var mappingChanged = hasOld && hasNew && !changed && oldMapping is not null && newMapping is not null && oldMapping.Value != newMapping.Value;
            var difference = !hasOld || !hasNew ? "Cannot compare" : mappingChanged ? "Saved value unchanged; meaning differs" : changed ? Signed(next - old) + " saved units" : "No change";
            if (changed && car && (key == "ACSetup.FINAL_RATIO" || key.StartsWith("ACSetup.INTERNAL_GEAR_", StringComparison.Ordinal)) &&
                oldMapping?.NumericValue is double ratioBefore && newMapping?.NumericValue is double ratioAfter && double.IsFinite(ratioBefore) && double.IsFinite(ratioAfter))
                difference = Signed(ratioAfter - ratioBefore) + " ratio";
            var note = car ? "Recorded setup values. The run does not establish why this individual setting changed or whether it helped."
                : generated ? "Generated ADT targets saved with each run; these are not wheelbase or in-game FFB readbacks."
                : "Unassigned or unknown snapshot value; no tunable control or physical unit is inferred.";
            return new Row(key, Label(key), car ? Group(key[8..]) : generated ? "FFB targets" : "Other captured values",
                hasOld ? RecordedValue(before!, key, old) : "Not captured", hasNew ? RecordedValue(after!, key, next) : "Not captured",
                difference,
                !hasOld || !hasNew ? "Missing evidence" : mappingChanged ? "Mapping differs" : changed ? "Changed" : "Unchanged",
                mappingChanged ? "The same saved value has different verified meanings in these snapshots. Car data may have changed; this is not an equivalent unchanged setup." : note,
                $"{key}\nBefore: {Source(before, car)}\nAfter: {Source(after, car)}" +
                (car ? "\nStored VALUE units can differ from the game's display. Verified mappings are shown only when they match that snapshot's saved value." : ""), changed, !hasOld || !hasNew || mappingChanged)
            { AfterHeading = generated ? "After target" : "Recorded after", BeforeHeading = generated ? "Before target" : "Before" };
        }));
    }

    private static string Source(TuneVersion? tune, bool car) => tune is null ? "No tune snapshot" : car
        ? tune.SetupSource == "csp-current-setup" ? "Current CSP setup, sampled periodically" : "Attached setup file: " + tune.SetupFileName
        : tune.Source;

    private static string RecordedValue(TuneVersion tune, string key, double value)
    {
        var mapping = key.StartsWith("ACSetup.", StringComparison.Ordinal) ? Verified(tune.DecodedSetup, key[8..], value) : null;
        return mapping is null ? F(value) + " (saved value)" : mapping.Value + $" (saved {F(value)})";
    }

    private static string ProposalValue(CarSetupParameter p, double? value, string raw, CarSetupAnalysis analysis)
    {
        if (value is not double number || !double.IsFinite(number)) return raw.Length == 0 ? "Unavailable" : raw + " (saved text)";
        if (CamberSetupValues.TrySetupValue(p.Range, number, out var camber)) return F(camber) + " setup units";
        var mapping = Verified(analysis.Physics?.DecodedSettings ?? [], p.Section, number);
        if (mapping is not null) return mapping.Value + $" (saved {F(number)})";
        if (p.Range?.DirectValueRangeVerified == true && !string.IsNullOrWhiteSpace(p.Range.Units)) return F(number) + " " + p.Range.Units;
        return F(number) + " (saved value)";
    }

    private static DecodedSetupSetting? Verified(IEnumerable<DecodedSetupSetting> values, string section, double raw)
    {
        var matches = values.Where(d => d.Section.Equals(section, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 && matches[0].Status == DecodedSetupSetting.Verified &&
            double.TryParse(matches[0].SavedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var saved) &&
            double.IsFinite(saved) && Math.Abs(saved - raw) < .000001 ? matches[0] : null;
    }

    private static IReadOnlyList<Row> Sort(IEnumerable<Row> rows) => rows.OrderByDescending(r => r.Emphasized)
        .ThenBy(r => r.Category, StringComparer.CurrentCulture).ThenBy(r => r.Setting, StringComparer.CurrentCulture).ToArray();
    private static string F(double n) => n.ToString("0.#######", CultureInfo.CurrentCulture);
    private static string Signed(double n) => n > 0 ? "+" + F(n) : F(n);
    private static string Category(CarSetupCategory c) => c == CarSetupCategory.Tires ? "Tyres" : c.ToString();
    private static string Group(string key) => key.ToUpperInvariant() switch
    {
        var s when s.StartsWith("PRESSURE_") || s == "TYRES" => "Tyres",
        var s when s.StartsWith("CAMBER_") || s.StartsWith("TOE_") || s.StartsWith("CASTER_") => "Alignment",
        var s when s.StartsWith("DAMP_") => "Dampers",
        var s when s.StartsWith("ARB_") || s.StartsWith("SPRING_") || s.StartsWith("ROD_") || s.StartsWith("PACKER_") => "Suspension",
        var s when s.StartsWith("DIFF_") => "Differential",
        var s when s.StartsWith("BRAKE_") || s == "FRONT_BIAS" => "Brakes",
        var s when s.StartsWith("INTERNAL_GEAR_") || s is "FINAL_RATIO" or "GEARSET" => "Gearing",
        var s when s.StartsWith("WING_") => "Aero",
        "FUEL" => "Fuel",
        "ENGINE_MAPS" or "ABS" or "TRACTION_CONTROL" => "Electronics",
        _ => "Other"
    };

    public static string Label(string key, string? carLabel = null)
    {
        if (key.StartsWith("Generated.", StringComparison.Ordinal))
        {
            var parts = key.Split('.', 3);
            return (parts.Length > 2 ? Humanize(parts[2]) : Humanize(key)) + " (" + (parts.Length > 1 ? parts[1] : "FFB") + " target)";
        }
        var section = key.StartsWith("ACSetup.", StringComparison.Ordinal) ? key[8..] : key;
        var s = section.ToUpperInvariant();
        var corners = new Dictionary<string, string> { ["LF"] = "front left", ["RF"] = "front right", ["LR"] = "rear left", ["RR"] = "rear right" };
        var names = new Dictionary<string, string> { ["PRESSURE"] = "Tyre pressure", ["CAMBER"] = "Camber", ["TOE_OUT"] = "Toe", ["CASTER"] = "Caster",
            ["SPRING_RATE"] = "Spring rate", ["ROD_LENGTH"] = "Ride-height rod length", ["PACKER_RANGE"] = "Bump stop range",
            ["DAMP_BUMP"] = "Bump damping", ["DAMP_REBOUND"] = "Rebound damping", ["DAMP_FAST_BUMP"] = "Fast bump damping", ["DAMP_FAST_REBOUND"] = "Fast rebound damping" };
        foreach (var (corner, position) in corners)
            if (s.EndsWith("_" + corner) && names.TryGetValue(s[..^3], out var name)) return name + " — " + position;
        if (s.StartsWith("INTERNAL_GEAR_") && int.TryParse(s[14..], out var gear) && gear is >= 2 and <= 11) return $"Gear {gear - 1} ratio";
        return s switch
        {
            "ARB_FRONT" => "Front anti-roll bar", "ARB_REAR" => "Rear anti-roll bar", "DIFF_POWER" => "Differential — on throttle",
            "DIFF_COAST" => "Differential — off throttle", "DIFF_PRELOAD" => "Differential preload", "FINAL_RATIO" => "Final-drive ratio",
            "GEARSET" => "Gear set", "ENGINE_MAPS" => "ECU / engine map", "FRONT_BIAS" or "BRAKE_BIAS" => "Front brake balance",
            "BRAKE_POWER" => "Brake strength", "TYRES" => "Tyre compound", "FUEL" => "Fuel amount", "ABS" => "ABS",
            "TRACTION_CONTROL" => "Traction control", _ => string.IsNullOrWhiteSpace(carLabel) ? Humanize(section) : carLabel
        };
    }
    private static string Humanize(string value) => Regex.Replace(value.Replace('_', ' ').Replace('.', ' '), "(?<=[a-z0-9])(?=[A-Z])", " ").Trim();
}
