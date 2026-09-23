using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Read-only base physics context, never a reconstruction of the live pit setup.</summary>
public sealed class CarPhysicsService
{
    private static readonly string[] Files = ["car.ini", "suspensions.ini", "tyres.ini", "drivetrain.ini", "engine.ini", "brakes.ini", "setup.ini"];
    private sealed class Ini : Dictionary<string, Dictionary<string, string>>
    {
        public Ini() : base(StringComparer.OrdinalIgnoreCase) { }
        public string? Value(string section, string key) => TryGetValue(section, out var values) ? values.GetValueOrDefault(key) : null;
    }

    public CarPhysicsSnapshot Read(CarProfile car, IReadOnlyList<CarSetupParameter>? baseline = null, bool enabled = true, CarDataSource? suppliedSource = null)
    {
        if (!enabled) return new() { Status = "Car physics import is off. Existing setup guidance remains available." };
        if (string.IsNullOrWhiteSpace(car.SourceFolderPath)) return new() { Status = "Select an installed car to read its base physics." };
        try
        {
            var source = suppliedSource ?? CarDataSource.Open(car);
            var root = source.Evidence.CarPath;
            var ini = new Dictionary<string, Ini>(); var notes = new List<string>();
            foreach (var name in Files)
            {
                try
                {
                    var text = source.ReadText(name);
                    if (text is null) { notes.Add(name + ": not available."); continue; }
                    ini[name] = Parse(Encoding.UTF8.GetBytes(text));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                { notes.Add(name + ": unavailable or malformed; its values were not imported."); }
            }
            var facts = new List<CarPhysicsFact>();
            string? Raw(string file, string section, string key) => ini.TryGetValue(file, out var content) ? content.Value(section, key) : null;
            void Number(string file, string section, string key, string label, string unit, double min, double max, double factor = 1)
            {
                var raw = Raw(file, section, key);
                if (raw is null) return;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n) || n < min || n > max)
                { notes.Add($"{file} [{section}] {key}: unsupported value left unknown."); return; }
                facts.Add(new(file, section, key, label, (n * factor).ToString("0.###", CultureInfo.InvariantCulture), unit));
            }
            void Text(string file, string section, string key, string label)
            {
                var raw = Raw(file, section, key);
                if (!string.IsNullOrWhiteSpace(raw) && raw.Length <= 100 && !raw.Any(char.IsControl)) facts.Add(new(file, section, key, label, raw, ""));
            }
            Number("car.ini", "BASIC", "TOTALMASS", "Base total mass", "kg", 100, 10000);
            Number("suspensions.ini", "BASIC", "WHEELBASE", "Wheelbase", "m", .5, 10);
            Number("suspensions.ini", "BASIC", "CG_LOCATION", "Base front weight distribution", "%", 0, 1, 100);
            foreach (var axle in new[] { "FRONT", "REAR" })
            {
                var label = axle == "FRONT" ? "Front" : "Rear";
                Text("suspensions.ini", axle, "TYPE", label + " suspension type");
                Number("suspensions.ini", axle, "TRACK", label + " track width", "m", .3, 5);
                Number("suspensions.ini", axle, "SPRING_RATE", label + " base spring rate", "N/m", 0, 1e8);
                Number("suspensions.ini", axle, "STATIC_CAMBER", label + " base camber", "deg", -30, 30);
                Number("suspensions.ini", axle, "TOE_OUT", label + " base toe-out", "m (physics value)", -.1, .1);
                foreach (var key in new[] { "DAMP_BUMP", "DAMP_REBOUND", "DAMP_FAST_BUMP", "DAMP_FAST_REBOUND" })
                    Number("suspensions.ini", axle, key, label + " " + key.ToLowerInvariant().Replace('_', ' '), "N·s/m", 0, 1e7);
                Number("suspensions.ini", "ARB", axle, label + " base anti-roll bar", "(physics value)", 0, 1e8);
            }
            var drive = Raw("drivetrain.ini", "TRACTION", "TYPE")?.ToUpperInvariant() ?? "";
            if (drive is "RWD" or "FWD" or "AWD" or "AWD2") Text("drivetrain.ini", "TRACTION", "TYPE", "Base driven wheels");
            else drive = "";
            foreach (var key in new[] { "POWER", "COAST" }) Number("drivetrain.ini", "DIFFERENTIAL", key, "Base differential " + key.ToLowerInvariant() + " lock", "%", 0, 1, 100);
            Number("drivetrain.ini", "DIFFERENTIAL", "PRELOAD", "Base differential preload", "Nm", 0, 10000);
            Number("drivetrain.ini", "GEARS", "FINAL", "Base final-drive ratio", ":1", .1, 30);
            Number("engine.ini", "ENGINE_DATA", "LIMITER", "Base engine limiter", "rpm", 1000, 30000);
            Number("brakes.ini", "DATA", "FRONT_SHARE", "Base front brake share", "%", 0, 1, 100);
            Number("brakes.ini", "DATA", "MAX_TORQUE", "Base brake maximum torque", "Nm", 0, 100000);
            var tyreSelection = baseline?.Where(p => p.Section.Equals("TYRES", StringComparison.OrdinalIgnoreCase)).ToList();
            int? compound = tyreSelection?.Count == 1 && tyreSelection[0].CurrentValue is double v && v >= 0 && v <= 255 && v == Math.Truncate(v) ? (int)v : null;
            notes.Insert(0, "Base physics describes the car. The loaded saved setup describes selected pit values; neither proves what is currently loaded in game. CSP extensions, geometry-derived caster, tyre curves and effective grip are not simulated.");
            if (compound is null) notes.Add("Tyre compound is unknown until a baseline with a valid TYRES selection is loaded; no active compound was guessed.");
            else
            {
                notes.Add($"Tyre data below uses saved compound index {compound}; confirm this setup is loaded in game.");
                foreach (var axle in new[] { "FRONT", "REAR" })
                {
                    var section = compound == 0 ? axle : axle + "_" + compound;
                    if (!ini.TryGetValue("tyres.ini", out var tyres) || !tyres.ContainsKey(section)) { notes.Add(section + " tyre data not found; no fallback compound was substituted."); continue; }
                    Text("tyres.ini", section, "NAME", axle + " selected tyre");
                    Number("tyres.ini", section, "WIDTH", axle + " tyre width", "mm", .05, 1, 1000);
                    Number("tyres.ini", section, "RADIUS", axle + " tyre radius", "m", .1, 1);
                    Number("tyres.ini", section, "PRESSURE_STATIC", axle + " base static pressure", "psi", 1, 100);
                    Number("tyres.ini", section, "PRESSURE_IDEAL", axle + " model ideal pressure", "psi (not a cold setup target)", 1, 100);
                }
            }
            var hasDefinition = ini.TryGetValue("setup.ini", out var definition);
            var decoded = new CarSetupDecoder().Decode(baseline ?? [], source.ReadText);
            notes.Add(source.Kind == "packed" ? "Ordinary data.acd decoded in a private, bounded memory cache. Installed files were not changed; cache is cleared when ADT exits." : "Read from the existing unpacked data folder.");
            notes.Add("File evidence covers supported INI/LUT/RTO/Lua data (the entire archive for packed cars), not external CSP overrides or proof of active in-game settings. Scripts are never executed.");
            if (source.FileNames.Any(x => x.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                notes.Add("This car includes Lua scripts. Decoded selections describe file mappings only; custom scripted effects are not evaluated.");
            return new() { CarPath = root, CarId = car.SourceFolderName ?? Path.GetFileName(root), Available = facts.Count > 0,
                SourceEvidence = source.Evidence, DecodedSettings = decoded,
                Fingerprint = source.Evidence.Fingerprint,
                Status = facts.Count > 0 ? $"Imported {facts.Count} base physics values from {source.Kind} car data. No car files were changed." : $"Read {source.Kind} car data; no supported base values found. Saved-setting decoding is shown below when available.",
                Facts = facts.AsReadOnly(), Notes = notes.AsReadOnly(), DriveType = drive, HasSetupDefinition = hasDefinition,
                AdjustableSections = Array.AsReadOnly(definition?.Keys.ToArray() ?? []), Fingerprints = source.Evidence.Fingerprints };
        }
        catch (InvalidDataException ex)
        { return new() { Status = "Car physics unavailable: " + ex.Message + " Saved-setup guidance remains available." }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new() { Status = "The car data could not be read safely. Check the selected car folder and permissions; existing setup guidance remains available." }; }
    }

    public static void EnsureUnchanged(CarPhysicsSnapshot? snapshot)
    {
        if (snapshot?.Available != true) return;
        try
        {
            if (snapshot.SourceEvidence is null) throw new IOException();
            CarDataSource.EnsureUnchanged(snapshot.SourceEvidence);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { throw new InvalidOperationException("Car physics or setup definitions changed or became unavailable. Reload the baseline and generate again before saving or staging.", ex); }
    }
    private static Ini Parse(byte[] bytes)
    {
        var result = new Ini(); Dictionary<string, string>? section = null;
        foreach (var raw in Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.Split(';')[0].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//")) continue;
            if (line.StartsWith('['))
            {
                var end = line.IndexOf(']'); if (end < 2 || result.Count >= 2048) throw new InvalidDataException();
                section = new(StringComparer.OrdinalIgnoreCase);
                if (!result.TryAdd(line[1..end].Trim(), section)) throw new InvalidDataException("Duplicate section.");
            }
            else
            {
                var equals = line.IndexOf('='); if (equals < 1 || section is null) throw new InvalidDataException();
                if (section.Count >= 2048 || !section.TryAdd(line[..equals].Trim(), line[(equals + 1)..].Trim())) throw new InvalidDataException("Duplicate field.");
            }
        }
        return result;
    }
}
