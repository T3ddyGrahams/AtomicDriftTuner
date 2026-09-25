using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Reads supported AC data without changing car physics. Saved ratios are list indexes.</summary>
public sealed partial class GearingDataService
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private readonly Dictionary<string, string> _fingerprints = new(StringComparer.OrdinalIgnoreCase);

    public GearingData Load(CarProfile car, string baselinePath, int gear)
    {
        _fingerprints.Clear();
        if (gear is < 1 or > 10) throw new InvalidDataException("Choose a forward gear from 1 to 10.");
        if (string.IsNullOrWhiteSpace(car.SourceFolderPath))
            throw new InvalidDataException("Select an installed Assetto Corsa car first, then open Gearing again.");
        var carPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(car.SourceFolderPath));
        var source = CarDataSource.Open(car);
        var baselineFull = Path.GetFullPath(baselinePath);
        var saved = ParseIni(Read(baselineFull), Path.GetFileName(baselineFull));
        if (!saved.Required("CAR", "MODEL").Equals(Path.GetFileName(carPath.TrimEnd(Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This setup belongs to a different car. Choose a baseline saved for the selected car.");
        var setup = ReadDataIni(source, "setup.ini");
        var drive = ReadDataIni(source, "drivetrain.ini");
        var tyres = ReadDataIni(source, "tyres.ini");
        var engine = ReadDataIni(source, "engine.ini");
        bool adjustableFinal = setup.Sections.ContainsKey("FINAL_GEAR_RATIO");
        if (!adjustableFinal && saved.Sections.ContainsKey("FINAL_RATIO"))
            throw new InvalidDataException("The saved final-drive selection has no matching definition. Save a fresh baseline for this car.");
        var ratios = adjustableFinal ? ReadRatios(source, setup.Required("FINAL_GEAR_RATIO", "RATIOS"))
            : new List<FinalDriveRatio> { new(0, "Fixed final drive", Number(drive.Required("GEARS", "FINAL"), .05, 30, "fixed final drive")) };
        var current = adjustableFinal ? Index(saved.Required("FINAL_RATIO", "VALUE"), ratios.Count, "final drive") : 0;
        var count = Index(drive.Required("GEARS", "COUNT"), 11, "forward gear count");
        if (gear > count) throw new InvalidDataException($"This car has {count} forward gears. Choose one of those gears.");

        double gearRatio;
        string gearSource;
        string gearboxKind = "Fixed individual gears";
        int selectedGearChoices = 1;
        var gearSets = setup.Sections.Keys.Any(x => x.StartsWith("GEAR_SET_", StringComparison.OrdinalIgnoreCase));
        var useGearSet = setup.Optional("GEARS", "USE_GEARSET") ?? "0";
        if (useGearSet is not ("0" or "1")) throw new InvalidDataException("Unrecognized gearbox selection format.");
        if (useGearSet == "1" && gearSets)
        {
            var selected = Index(saved.Required("GEARSET", "VALUE"), 256, "gearbox");
            var section = $"GEAR_SET_{selected}";
            gearRatio = Number(setup.Required(section, $"GEAR_{gear}"), 0.05, 20, "selected gear ratio");
            gearSource = $"Gearbox {selected}: {setup.Optional(section, "NAME") ?? section}";
            gearboxKind = $"{setup.Sections.Keys.Count(k => k.StartsWith("GEAR_SET_", StringComparison.OrdinalIgnoreCase))} preset gearboxes";
        }
        else if (useGearSet == "0" && setup.Sections.ContainsKey($"GEAR_{gear}"))
        {
            var gears = ReadRatios(source, setup.Required($"GEAR_{gear}", "RATIOS"));
            // AC's internal index includes reverse. Content Manager maps GEAR_n to INTERNAL_GEAR_(n+1).
            var selected = Index(saved.Required($"INTERNAL_GEAR_{gear + 1}", "VALUE"), gears.Count, "selected gear");
            gearRatio = gears[selected].Ratio;
            gearSource = $"Adjustable gear {gear}: {gears[selected].Label}";
            gearboxKind = "Individually adjustable gear";
            selectedGearChoices = gears.Count;
        }
        else
        {
            if (saved.Sections.ContainsKey($"INTERNAL_GEAR_{gear + 1}") || saved.Sections.ContainsKey($"GEAR_{gear}"))
                throw new InvalidDataException("The saved setup has a gear override that this car's definitions cannot resolve.");
            if (saved.Sections.ContainsKey("GEARSET"))
                throw new InvalidDataException("The saved setup selects a gearbox but no active gearset definition is available.");
            gearRatio = Number(drive.Required("GEARS", $"GEAR_{gear}"), 0.05, 20, "fixed gear ratio");
            gearSource = "Fixed gearbox from drivetrain.ini";
            if (useGearSet == "0" && Enumerable.Range(1, count).Any(g => setup.Sections.ContainsKey($"GEAR_{g}")))
                gearboxKind = "Mixed gearbox (some individually adjustable gears)";
        }

        var traction = drive.Required("TRACTION", "TYPE").ToUpperInvariant();
        if (traction is not ("RWD" or "FWD"))
            throw new InvalidDataException("Version 1 supports RWD and FWD. AWD gearing needs additional differential and tyre modelling.");
        var compound = Index(saved.Required("TYRES", "VALUE"), 256, "tyre compound");
        var axle = traction == "RWD" ? "REAR" : "FRONT";
        var tyreSection = compound == 0 ? axle : $"{axle}_{compound}";
        var radius = Number(tyres.Required(tyreSection, "RADIUS"), 0.1, 1, "driven tyre radius");
        var (limiter, limiterSource) = ReadLimiter(engine, setup, saved);
        return new GearingData(baselineFull, carPath, ratios.AsReadOnly(), current, gear, gearRatio, radius, limiter,
            gearSource, $"{axle.ToLowerInvariant()} compound {compound}: {tyres.Optional(tyreSection, "NAME") ?? "unnamed"}",
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(_fingerprints, StringComparer.OrdinalIgnoreCase)))
        {
            CarDataEvidence = source.Evidence, GearCount = count, GearboxKind = gearboxKind, LimiterSource = limiterSource,
            SelectedGearChoices = selectedGearChoices, FinalDriveAdjustable = adjustableFinal,
            DefinitionWarnings = Array.AsReadOnly(setup.InvalidSections.Select(p =>
                $"setup.ini [{p.Key}] has a {p.Value}; it is not used by this gearing calculation and is left unchanged.").ToArray()),
            RpmEstimate = ReadRpmEstimate(source, engine, limiter)
        };
    }

    private static (double Rpm, string Source) ReadLimiter(Ini engine, Ini setup, Ini saved)
    {
        var baseRpm = Number(engine.Required("ENGINE_DATA", "LIMITER"), 1000, 30000, "engine limiter");
        // Only the standard ENGINE_LIMITER percentage control is mapped. A differently named
        // limiter may be a mod-defined control; never guess that it has the same units.
        if (setup.Sections.ContainsKey("LIMITER") || saved.Sections.ContainsKey("LIMITER"))
            throw new InvalidDataException("This setup has an unsupported LIMITER control. ADT cannot infer its RPM mapping.");
        var defined = setup.Sections.ContainsKey("ENGINE_LIMITER");
        var selected = saved.Sections.ContainsKey("ENGINE_LIMITER");
        if (!defined && !selected) return (baseRpm, $"engine.ini base limiter: {baseRpm:N0} RPM (no saved adjustable limiter)");
        if (!defined || !selected)
            throw new InvalidDataException("The adjustable engine limiter needs both its setup.ini definition and a saved ENGINE_LIMITER value. Save a fresh baseline in AC and select it again.");
        if (setup.Optional("ENGINE_LIMITER", "RATIOS") is not null || setup.Optional("ENGINE_LIMITER", "LUT") is not null)
            throw new InvalidDataException("The engine limiter uses an unsupported list/curve mapping; a standard percentage control is required.");
        var min = Number(setup.Required("ENGINE_LIMITER", "MIN"), 1, 200, "limiter minimum percentage");
        var max = Number(setup.Required("ENGINE_LIMITER", "MAX"), min, 200, "limiter maximum percentage");
        var step = Number(setup.Required("ENGINE_LIMITER", "STEP"), .000001, 200, "limiter step");
        var mode = Index(setup.Optional("ENGINE_LIMITER", "SHOW_CLICKS") ?? "0", 3, "limiter display mode");
        var raw = Number(saved.Required("ENGINE_LIMITER", "VALUE"), 0, 1_000_000, "saved engine limiter");
        if (mode != 0 && raw != Math.Truncate(raw))
            throw new InvalidDataException("The saved engine-limiter click count is not a whole number.");
        // Content Manager's saved-value mapping: actual, step-normalized, or zero-based steps.
        // The decoded control is percent of engine.ini LIMITER, never an RPM value or an ECU map.
        var percent = mode switch { 1 => raw * step, 2 => min + raw * step, _ => raw };
        var clicks = (percent - min) / step;
        if (!double.IsFinite(percent) || percent < min - .000001 || percent > max + .000001 ||
            Math.Abs(clicks - Math.Round(clicks)) > .000001)
            throw new InvalidDataException("The saved engine limiter does not match this car's percentage range and steps. Save a fresh baseline in AC.");
        var rpm = baseRpm * percent / 100;
        if (!double.IsFinite(rpm) || rpm is < 1000 or > 30000)
            throw new InvalidDataException("The selected engine limiter resolves outside the supported RPM range.");
        return (rpm, $"Saved ENGINE_LIMITER {raw:0.######}, SHOW_CLICKS={mode}, STEP={step:0.######}: {percent:0.######}% × engine.ini {baseRpm:N0} RPM = {rpm:N0} RPM. " +
            "This is the selected baseline's limit, not a live ECU/script readback. Load this setup in AC before testing.");
    }

    public GearingPlan Calculate(CarProfile car, string baselinePath, GearingTarget target)
    {
        target.Validate();
        var first = Load(car, baselinePath, target.Gear);
        var second = target.Sweeper is { } s ? Load(car, baselinePath, s.Gear) : null;
        EnsureUnchanged(first);
        if (second is not null) EnsureUnchanged(second);
        return GearingPlanner.Plan(first, target, second);
    }

    public string Save(GearingPlan plan, string outputPath)
    {
        if (!plan.HasChange) throw new InvalidOperationException("The current final drive is already the best match; there is no gearing change to save.");
        EnsureUnchanged(plan.Data);
        if (plan.SweeperData is not null) EnsureUnchanged(plan.SweeperData);
        // Recompute from fresh disk data instead of trusting mutable UI analysis or ratio indexes.
        var car = new CarProfile { SourceFolderPath = plan.Data.CarPath, SourceFolderName = Path.GetFileName(plan.Data.CarPath) };
        var verified = Calculate(car, plan.Data.BaselinePath, plan.Target);
        if (verified.Recommended.FinalDrive != plan.Recommended.FinalDrive)
            throw new InvalidDataException("Gearing data changed. Calculate again before saving.");
        var output = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(output), ".ini", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Save the gearing setup as an .ini file.");
        if (output.StartsWith(plan.Data.CarPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save in your Assetto Corsa setups folder, outside the installed car folder.");
        var service = new AssettoCorsaSetupService();
        var analysis = service.LoadBaseline(plan.Data.BaselinePath, car);
        var parameter = analysis.Parameters.SingleOrDefault(p => p.Section.Equals("FINAL_RATIO", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The baseline has no writable FINAL_RATIO value. Save it in Assetto Corsa again.");
        parameter.RecommendedValue = verified.Recommended.FinalDrive.Index;
        EnsureUnchanged(plan.Data);
        return service.WriteGenerated(analysis, output);
    }

    private static void EnsureUnchanged(GearingData data)
    {
        if (data.CarDataEvidence is null)
            throw new InvalidDataException("The car's data source was not captured. Calculate again before saving.");
        CarDataSource.EnsureUnchanged(data.CarDataEvidence);
        foreach (var (path, hash) in data.Fingerprints)
            if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(ReadBytes(path))) != hash)
                throw new InvalidDataException("The baseline setup or car data changed after calculation. Calculate again before saving.");
    }

    private static string ReadData(CarDataSource source, string name)
    {
        // Ratio references must be flat files from this exact car-data snapshot.
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
            throw new InvalidDataException("Unsupported ratio-file path in this car's setup definition.");
        return source.ReadText(name) ?? throw new InvalidDataException(
            $"Required car-data file '{name}' is missing. Gearing needs the car's setup, drivetrain, engine, selected tyres and referenced ratio lists.");
    }

    private string Read(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"Required baseline '{Path.GetFileName(path)}' is missing. Choose a setup saved for this car.");
        var bytes = ReadBytes(path);
        _fingerprints[path] = Convert.ToHexString(SHA256.HashData(bytes));
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    private static byte[] ReadBytes(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("A gearing source file is too large to read.");
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new InvalidDataException("A gearing source file grew too large to read.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static List<FinalDriveRatio> ReadRatios(CarDataSource source, string file)
    {
        var result = new List<FinalDriveRatio>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in ReadData(source, file).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//")) continue;
            var pair = line.Split('|');
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) || !labels.Add(pair[0].Trim().Replace("//", "/")))
                throw new InvalidDataException($"Ambiguous or malformed ratio list '{file}'. ADT cannot safely map its saved indexes.");
            var ratio = Number(pair[1].Split(';')[0].Trim(), 0.05, 30, "ratio list entry");
            result.Add(new FinalDriveRatio(result.Count, pair[0].Trim(), ratio));
            if (result.Count > 1024) throw new InvalidDataException("This ratio list has too many entries.");
        }
        if (result.Count == 0) throw new InvalidDataException($"The ratio list '{file}' is empty.");
        return result;
    }

    private static Ini ReadDataIni(CarDataSource source, string name)
    {
        var text = ReadData(source, name);
        if (!name.Equals("setup.ini", StringComparison.OrdinalIgnoreCase)) return ParseIni(text, name);
        var definitions = CarSetupDefinitionFile.Parse(text);
        var ini = new Ini();
        foreach (var pair in definitions.Sections) ini.Sections.Add(pair.Key, pair.Value);
        foreach (var pair in definitions.InvalidSections) ini.InvalidSections.Add(pair.Key, pair.Value);
        return ini;
    }

    private static Ini ParseIni(string text, string name)
    {
        var ini = new Ini();
        Dictionary<string, string>? section = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                var close = line.IndexOf(']');
                if (close <= 1) throw new InvalidDataException($"Malformed section in {name}.");
                var sectionName = line[1..close].Trim();
                section = new(StringComparer.OrdinalIgnoreCase);
                if (!ini.Sections.TryAdd(sectionName, section))
                    throw new InvalidDataException($"Duplicate [{sectionName}] section in {name}. Gearing cannot be resolved safely.");
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals > 0 && section is not null && !section.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim()))
                throw new InvalidDataException($"Duplicate setting in {name}. Gearing cannot be resolved safely.");
        }
        return ini;
    }

    private static int Index(string raw, int count, string label)
    {
        var value = Number(raw, 0, count - 1, label);
        if (value != Math.Truncate(value)) throw new InvalidDataException($"The saved {label} index is not a whole number.");
        return (int)value;
    }

    private static double Number(string raw, double min, double max, string label)
    {
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Missing, invalid or unsupported {label}: '{raw}'.");
        return value;
    }

    private sealed class Ini
    {
        public Dictionary<string, Dictionary<string, string>> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> InvalidSections { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Optional(string section, string key)
        {
            if (InvalidSections.TryGetValue(section, out var reason))
                throw new InvalidDataException($"setup.ini [{section}] has a {reason}. Gearing needs this definition and cannot resolve it safely.");
            return Sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;
        }
        public string Required(string section, string key) => Optional(section, key) ??
            throw new InvalidDataException($"Required [{section}] {key} is missing. This car/setup is not supported by gearing version 1.");
    }
}
