using System.Globalization;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>
/// Interprets only documented, explicit mappings. The supplied reader owns source selection,
/// immutable reads and fingerprints. This class neither accesses disk nor runs mod code.
/// </summary>
public sealed class CarSetupDecoder
{
    private const int MaxText = 1024 * 1024;
    private const int MaxRows = 4096;
    private const string MapLimit = "These are configured torque multipliers, not horsepower or measured engine output. " +
        "CSP version, controllers, scripts and other engine effects are not simulated; confirm the selection in game.";

    public IReadOnlyList<DecodedSetupSetting> Decode(IReadOnlyList<CarSetupParameter> baseline, Func<string, string?> readText)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(readText);
        var source = new Source(readText);
        var duplicates = baseline.GroupBy(p => p.Section, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<DecodedSetupSetting>(baseline.Count);
        foreach (var parameter in baseline)
        {
            try
            {
                if (duplicates.Contains(parameter.Section))
                    throw new InvalidDataException("The saved setup repeats this section; no unique selection can be established.");
                result.Add(DecodeOne(parameter, source));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                // Errors are intentionally local to a setting, not fatal to the rest of the analysis.
                result.Add(Unknown(parameter, ex.Message));
            }
        }
        return result.AsReadOnly();
    }

    private static DecodedSetupSetting DecodeOne(CarSetupParameter p, Source source)
    {
        var section = p.Section.ToUpperInvariant();
        if (section == "FINAL_RATIO") return Ratio(p, source, "FINAL_GEAR_RATIO", "Final drive");
        if (section == "GEARSET") return GearSet(p, source);
        if (section.StartsWith("INTERNAL_GEAR_", StringComparison.Ordinal))
        {
            if (!int.TryParse(section[14..], NumberStyles.None, CultureInfo.InvariantCulture, out var internalGear) || internalGear is < 2 or > 11)
                return Unknown(p, "Only documented forward-gear selections INTERNAL_GEAR_2 through INTERNAL_GEAR_11 are supported.");
            var setup = source.Ini("setup.ini");
            var useSets = setup.Optional("GEARS", "USE_GEARSET");
            if (useSets is not (null or "0")) return Unknown(p, "Individual gear selection cannot be verified while the setup selects a gearset or an unknown gearbox mode.");
            // AC includes reverse in the internal index. CM's CarSetupValues maps GEAR_n to INTERNAL_GEAR_(n+1).
            return Ratio(p, source, $"GEAR_{internalGear - 1}", $"Gear {internalGear - 1}");
        }
        if (section == "ENGINE_MAPS") return EngineMap(p, source);
        return Unknown(p, "No verified decoder is available for this control. The saved value is preserved; units and mechanical effect are not inferred.");
    }

    private static DecodedSetupSetting Ratio(CarSetupParameter p, Source source, string definition, string label)
    {
        var setup = source.Ini("setup.ini");
        var file = setup.Required(definition, "RATIOS");
        var rows = Pairs(source.RequiredText(file), "ratio list");
        var seenLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ratios = new List<double>();
        foreach (var row in rows)
        {
            if (!seenLabels.Add(row.Left)) throw new InvalidDataException("The ratio list contains duplicate labels; the mapping is ambiguous.");
            ratios.Add(Number(row.Right, 0.001, 100, "ratio"));
        }
        var index = Index(p.CurrentRaw, rows.Count, "saved ratio selection");
        var ratio = ratios[index];
        return new(p.Section, p.CurrentRaw, DecodedSetupSetting.Verified,
            $"{label} {F(ratio)}:1 ({rows[index].Left})", $"setup.ini [{definition}] RATIOS → {file}",
            $"Saved index {index} selects entry {index + 1} in file order. The label is shown separately from the numeric ratio; " +
            "this verifies the file mapping, not whether that setup was loaded in game.", ratio);
    }

    private static DecodedSetupSetting GearSet(CarSetupParameter p, Source source)
    {
        var setup = source.Ini("setup.ini");
        if (setup.Required("GEARS", "USE_GEARSET") != "1")
            return Unknown(p, "The setup does not explicitly enable gearset selection.");
        var index = Index(p.CurrentRaw, 256, "saved gearset selection");
        var section = $"GEAR_SET_{index}";
        var values = setup.Section(section);
        var ratios = new List<string>();
        foreach (var item in values.Where(kv => kv.Key.StartsWith("GEAR_", StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!int.TryParse(item.Key[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var gear) || gear is < 1 or > 10)
                throw new InvalidDataException("The selected gearset uses an unsupported gear identifier.");
            var ratio = Number(item.Value, 0, 100, "gearset ratio");
            if (ratio > 0) ratios.Add($"gear {gear}: {F(ratio)}:1");
        }
        if (ratios.Count == 0) throw new InvalidDataException("The selected gearset contains no readable forward-gear ratios.");
        var name = setup.Optional(section, "NAME") ?? $"Gearset {index}";
        return new(p.Section, p.CurrentRaw, DecodedSetupSetting.Verified, $"{name} ({string.Join(", ", ratios)})",
            $"setup.ini [GEARS] USE_GEARSET=1; [{section}]",
            "The saved selection explicitly identifies this gearset. Only positive ratios supplied by that section are shown; no missing gears or final drive are inferred.");
    }

    private static DecodedSetupSetting EngineMap(CarSetupParameter p, Source source)
    {
        // CSP's official Powertrain wiki documents setup ENGINE_MAPS name|index LUT and
        // engine.ini [MAP] MAP_n as rpm|torque_multiplier, with a direct-index alternative.
        // It does not establish row-vs-value serialization for every custom named/numeric LUT.
        var setup = source.Ini("setup.ini");
        setup.Section("ENGINE_MAPS");
        var clicks = setup.Optional("ENGINE_MAPS", "SHOW_CLICKS");
        if (clicks is not (null or "0"))
            return Unknown(p, "This engine-map control uses clicks or an unknown display mode. Its saved-value convention has not been verified.", "setup.ini [ENGINE_MAPS]");
        var saved = Index(p.CurrentRaw, 4096, "saved engine-map selection");
        var mapIndex = saved;
        string name = $"Engine map {saved}";
        var mappingSource = "setup.ini [ENGINE_MAPS] (direct index)";
        var lut = setup.Optional("ENGINE_MAPS", "LUT");
        if (!string.IsNullOrWhiteSpace(lut))
        {
            var rows = Pairs(source.RequiredText(lut), "engine-map selection LUT");
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var indexes = new List<int>();
            foreach (var row in rows)
            {
                if (!labels.Add(row.Left)) throw new InvalidDataException("The engine-map selection LUT repeats a label or input value.");
                indexes.Add(Index(row.Right, 4096, "engine-map LUT target"));
            }
            var mapMatches = indexes.Select((value, row) => (value, row)).Where(x => x.value == saved).ToList();
            if (mapMatches.Count > 1) throw new InvalidDataException("Multiple engine-map labels identify the saved map value; its selected label is ambiguous.");
            var numericLabels = rows.All(row => double.TryParse(row.Left, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number));
            if (numericLabels)
            {
                // A numeric first column might be a visible label or a controller input.
                // Never silently interpolate or assume which convention a custom mod uses.
                var candidates = rows.Select((row, i) => (input: Number(row.Left, 0, 100000, "engine-map LUT input"), target: indexes[i]))
                    .Where(row => row.input == saved).ToList();
                if (candidates.Count != 1)
                    return Unknown(p, "The numeric engine-map LUT has no unique exact saved-input match. No row-index or interpolation fallback was used.", lut);
                var candidate = candidates[0].target;
                return new(p.Section, p.CurrentRaw, DecodedSetupSetting.Partial, $"Numeric LUT entry {saved} → map {candidate}",
                    $"setup.ini [ENGINE_MAPS] LUT → {lut}",
                    "This explicit LUT entry is readable, but a numeric first column may be a display label rather than a saved-value input. " +
                    "Its connection to the saved selection is not verified; ADT will not infer ECU behavior or power from it.");
            }
            if (saved >= rows.Count || mapMatches.Count != 1 || indexes[saved] != saved)
            {
                var candidates = new List<string>();
                if (saved < rows.Count) candidates.Add($"row {saved}: {rows[saved].Left} → map {indexes[saved]}");
                if (mapMatches.Count == 1) candidates.Add($"map value {saved}: {rows[mapMatches[0].row].Left}");
                return new(p.Section, p.CurrentRaw, DecodedSetupSetting.Partial,
                    candidates.Count > 0 ? string.Join("; ", candidates) : "Engine-map selection not resolved",
                    $"setup.ini [ENGINE_MAPS] LUT → {lut}",
                    "The named LUT uses nonmatching row and map indexes. The saved-value convention is not established for this format, " +
                    "so these are candidate interpretations, not a verified active map. No power estimate is made.");
            }
            name = rows[saved].Left;
            mapIndex = indexes[saved];
            mappingSource = $"setup.ini [ENGINE_MAPS] LUT → {lut} (row and map indexes agree)";
        }
        else
        {
            // Direct indexing is documented; optional range metadata must also accept the value.
            var min = setup.Optional("ENGINE_MAPS", "MIN");
            var max = setup.Optional("ENGINE_MAPS", "MAX");
            if (min is not null && saved < Number(min, 0, 4095, "engine-map minimum") ||
                max is not null && saved > Number(max, 0, 4095, "engine-map maximum"))
                throw new InvalidDataException("The saved engine-map selection is outside the declared range.");
            var step = setup.Optional("ENGINE_MAPS", "STEP");
            if (step is not null)
            {
                var increment = Number(step, double.Epsilon, 4095, "engine-map step");
                var start = min is null ? 0 : Number(min, 0, 4095, "engine-map minimum");
                var steps = (saved - start) / increment;
                if (!double.IsFinite(steps) || Math.Abs(steps - Math.Round(steps)) > 0.000001)
                    throw new InvalidDataException("The saved engine-map selection does not match its declared step.");
            }
        }

        var selectedSource = $"{mappingSource}; engine.ini [MAP] MAP_{mapIndex}";
        try
        {
            var engine = source.Ini("engine.ini");
            var mapFile = engine.Required("MAP", $"MAP_{mapIndex}");
            var rows = Pairs(source.RequiredText(mapFile), "engine torque-multiplier LUT");
            var points = new List<(double Rpm, double Multiplier)>();
            double previous = -1;
            foreach (var row in rows)
            {
                var rpm = Number(row.Left, 0, 100000, "engine-map RPM");
                var multiplier = Number(row.Right, 0, 100, "engine-map torque multiplier");
                if (rpm <= previous) throw new InvalidDataException("The torque-multiplier LUT contains duplicate or unordered RPM points.");
                points.Add((rpm, multiplier)); previous = rpm;
            }
            var min = points.Min(x => x.Multiplier); var max = points.Max(x => x.Multiplier);
            var range = min == max ? $"{F(min)}×" : $"{F(min)}–{F(max)}×";
            return new(p.Section, p.CurrentRaw, DecodedSetupSetting.Verified,
                $"{name} (map {mapIndex}; configured torque multiplier {range}, {F(points[0].Rpm)}–{F(points[^1].Rpm)} RPM)",
                $"{selectedSource} → {mapFile}", MapLimit);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return new(p.Section, p.CurrentRaw, DecodedSetupSetting.Partial, $"{name} (map {mapIndex}); effect unavailable", selectedSource,
                $"The selection mapping is readable, but the referenced engine-map definition cannot be verified: {ex.Message} {MapLimit}");
        }
    }

    private static DecodedSetupSetting Unknown(CarSetupParameter p, string why, string source = "") =>
        new(p.Section, p.CurrentRaw, DecodedSetupSetting.Unsupported, $"Saved value {p.CurrentRaw}", source, why);
    private static string F(double n) => n.ToString("0.####", CultureInfo.InvariantCulture);
    private static double Number(string raw, double min, double max, string label)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number) || number < min || number > max)
            throw new InvalidDataException($"The {label} is malformed or outside the supported bounds.");
        return number;
    }
    private static int Index(string raw, int count, string label)
    {
        var number = Number(raw, 0, Math.Max(0, count - 1), label);
        if (count == 0 || number != Math.Truncate(number)) throw new InvalidDataException($"The {label} is not a valid zero-based integer selection.");
        return (int)number;
    }
    private static string Clean(string line)
    {
        var text = line.Trim().TrimStart('\uFEFF');
        if (text.StartsWith('#') || text.StartsWith("//", StringComparison.Ordinal)) return "";
        var comment = text.IndexOf(';');
        return (comment < 0 ? text : text[..comment]).Trim();
    }
    private static List<(string Left, string Right)> Pairs(string text, string label)
    {
        var result = new List<(string, string)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = Clean(raw);
            if (line.Length == 0) continue;
            var cells = line.Split('|');
            if (cells.Length != 2 || cells.Any(string.IsNullOrWhiteSpace) || cells[0].Length > 256 || cells[1].Length > 128)
                throw new InvalidDataException($"The {label} does not use the supported two-column format.");
            result.Add((cells[0].Trim(), cells[1].Trim()));
            if (result.Count > MaxRows) throw new InvalidDataException($"The {label} exceeds the supported row limit.");
        }
        if (result.Count == 0) throw new InvalidDataException($"The {label} is empty.");
        return result;
    }

    private sealed class Source(Func<string, string?> reader)
    {
        private readonly Dictionary<string, string?> _text = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Ini> _ini = new(StringComparer.OrdinalIgnoreCase);
        public string RequiredText(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name is "." or ".." ||
                name != name.Trim() || name.EndsWith('.') || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or ' ')) ||
                !(name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".lut", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".rto", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Only flat local INI/LUT/RTO references are supported; nested paths, inline expressions and scripts are not interpreted.");
            if (!_text.TryGetValue(name, out var text)) _text[name] = text = reader(name);
            if (text is null) throw new InvalidDataException($"Required car-data file '{name}' is missing or unavailable.");
            if (text.Length > MaxText || text.Contains('\0')) throw new InvalidDataException($"Car-data file '{name}' is oversized or not readable text.");
            return text;
        }
        public Ini Ini(string name)
        {
            if (!_ini.TryGetValue(name, out var result)) _ini[name] = result = new Ini(RequiredText(name));
            return result;
        }
    }

    private sealed class Ini
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _invalid = new(StringComparer.OrdinalIgnoreCase);
        public Ini(string text)
        {
            string? current = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = Clean(raw);
                if (line.Length == 0) continue;
                if (line.StartsWith('['))
                {
                    if (!line.EndsWith(']') || line.Length is < 3 or > 256)
                    { if (current is not null) _invalid.Add(current); current = null; continue; }
                    var name = line[1..^1].Trim();
                    if (name.Length == 0)
                    { if (current is not null) _invalid.Add(current); current = null; continue; }
                    current = name;
                    if (!_sections.TryAdd(current, new(StringComparer.OrdinalIgnoreCase))) _invalid.Add(current);
                    continue;
                }
                // Never attach a global VALUE or unnamed field to the next setup control.
                if (current is null) continue;
                var at = line.IndexOf('=');
                if (at <= 0 || line.Length > 4096) { _invalid.Add(current); continue; }
                var key = line[..at].Trim(); var value = line[(at + 1)..].Trim();
                if (key.Length == 0 || !_sections[current].TryAdd(key, value)) _invalid.Add(current);
            }
        }
        public IReadOnlyDictionary<string, string> Section(string section)
        {
            if (_invalid.Contains(section)) throw new InvalidDataException($"Section [{section}] has malformed or duplicate definitions.");
            if (!_sections.TryGetValue(section, out var result)) throw new InvalidDataException($"Required section [{section}] is unavailable.");
            return result;
        }
        public string Required(string section, string key)
        {
            var value = Optional(section, key);
            return !string.IsNullOrWhiteSpace(value) ? value :
                throw new InvalidDataException($"Required field [{section}] {key} is unavailable.");
        }
        public string? Optional(string section, string key)
        {
            if (_invalid.Contains(section)) throw new InvalidDataException($"Section [{section}] has malformed or duplicate definitions.");
            return _sections.TryGetValue(section, out var fields) && fields.TryGetValue(key, out var value) ? value : null;
        }
    }
}
