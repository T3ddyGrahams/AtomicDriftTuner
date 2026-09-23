using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Reads supported AC data without changing car physics. Saved ratios are list indexes.</summary>
public sealed class GearingDataService
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
        var ratios = ReadRatios(source, setup.Required("FINAL_GEAR_RATIO", "RATIOS"));
        var current = Index(saved.Required("FINAL_RATIO", "VALUE"), ratios.Count, "final drive");
        var count = Index(drive.Required("GEARS", "COUNT"), 11, "forward gear count");
        if (gear > count) throw new InvalidDataException($"This car has {count} forward gears. Choose one of those gears.");

        double gearRatio;
        string gearSource;
        var gearSets = setup.Sections.Keys.Any(x => x.StartsWith("GEAR_SET_", StringComparison.OrdinalIgnoreCase));
        var useGearSet = setup.Optional("GEARS", "USE_GEARSET") ?? "0";
        if (useGearSet is not ("0" or "1")) throw new InvalidDataException("Unrecognized gearbox selection format.");
        if (useGearSet == "1" && gearSets)
        {
            var selected = Index(saved.Required("GEARSET", "VALUE"), 256, "gearbox");
            var section = $"GEAR_SET_{selected}";
            gearRatio = Number(setup.Required(section, $"GEAR_{gear}"), 0.05, 20, "selected gear ratio");
            gearSource = $"Gearbox {selected}: {setup.Optional(section, "NAME") ?? section}";
        }
        else if (useGearSet == "0" && setup.Sections.ContainsKey($"GEAR_{gear}"))
        {
            var gears = ReadRatios(source, setup.Required($"GEAR_{gear}", "RATIOS"));
            // AC's internal index includes reverse. Content Manager maps GEAR_n to INTERNAL_GEAR_(n+1).
            var selected = Index(saved.Required($"INTERNAL_GEAR_{gear + 1}", "VALUE"), gears.Count, "selected gear");
            gearRatio = gears[selected].Ratio;
            gearSource = $"Adjustable gear {gear}: {gears[selected].Label}";
        }
        else
        {
            if (saved.Sections.ContainsKey($"INTERNAL_GEAR_{gear + 1}") || saved.Sections.ContainsKey($"GEAR_{gear}"))
                throw new InvalidDataException("The saved setup has a gear override that this car's definitions cannot resolve.");
            if (saved.Sections.ContainsKey("GEARSET"))
                throw new InvalidDataException("The saved setup selects a gearbox but no active gearset definition is available.");
            gearRatio = Number(drive.Required("GEARS", $"GEAR_{gear}"), 0.05, 20, "fixed gear ratio");
            gearSource = "Fixed gearbox from drivetrain.ini";
        }

        var traction = drive.Required("TRACTION", "TYPE").ToUpperInvariant();
        if (traction is not ("RWD" or "FWD"))
            throw new InvalidDataException("Version 1 supports RWD and FWD. AWD gearing needs additional differential and tyre modelling.");
        var compound = Index(saved.Required("TYRES", "VALUE"), 256, "tyre compound");
        var axle = traction == "RWD" ? "REAR" : "FRONT";
        var tyreSection = compound == 0 ? axle : $"{axle}_{compound}";
        var radius = Number(tyres.Required(tyreSection, "RADIUS"), 0.1, 1, "driven tyre radius");
        if (setup.Sections.ContainsKey("LIMITER") || saved.Sections.ContainsKey("LIMITER"))
            throw new InvalidDataException("This car has an adjustable RPM limiter. Version 1 cannot verify its active value yet.");
        var limiter = Number(engine.Required("ENGINE_DATA", "LIMITER"), 1000, 30000, "engine limiter");
        return new GearingData(baselineFull, carPath, ratios.AsReadOnly(), current, gear, gearRatio, radius, limiter,
            gearSource, $"{axle.ToLowerInvariant()} compound {compound}: {tyres.Optional(tyreSection, "NAME") ?? "unnamed"}",
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(_fingerprints, StringComparer.OrdinalIgnoreCase)))
        {
            CarDataEvidence = source.Evidence
        };
    }

    public string Save(GearingPlan plan, string outputPath)
    {
        if (!plan.HasChange) throw new InvalidOperationException("The current final drive is already the best match; there is no gearing change to save.");
        EnsureUnchanged(plan.Data);
        // Recompute from fresh disk data instead of trusting mutable UI analysis or ratio indexes.
        var car = new CarProfile { SourceFolderPath = plan.Data.CarPath, SourceFolderName = Path.GetFileName(plan.Data.CarPath) };
        var fresh = Load(car, plan.Data.BaselinePath, plan.Target.Gear);
        var verified = GearingPlanner.Plan(fresh, plan.Target);
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

    private static Ini ReadDataIni(CarDataSource source, string name) => ParseIni(ReadData(source, name), name);

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
        public string? Optional(string section, string key) => Sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;
        public string Required(string section, string key) => Optional(section, key) ??
            throw new InvalidDataException($"Required [{section}] {key} is missing. This car/setup is not supported by gearing version 1.");
    }
}
