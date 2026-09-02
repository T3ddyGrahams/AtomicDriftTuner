using System.Globalization;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AssettoCorsaSetupService
{
    private readonly AppSettingsStore _settingsStore = new();

    public string GetDefaultSetupsRoot()
    {
        var configured =
            _settingsStore.Load().AssettoCorsaDocumentsRoot;

        var userRoot =
            !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Assetto Corsa");

        return Path.Combine(userRoot, "setups");
    }

    public List<string> FindSavedSetups(CarProfile car, string? setupsRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(setupsRoot) ? GetDefaultSetupsRoot() : setupsRoot!;
        if (!Directory.Exists(root) || string.IsNullOrWhiteSpace(car.SourceFolderName)) return [];

        var carDir = Path.Combine(root, car.SourceFolderName);
        if (!Directory.Exists(carDir)) return [];

        return Directory.EnumerateFiles(carDir, "*.ini", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public CarSetupAnalysis LoadBaseline(string path, CarProfile car)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Baseline setup not found.", path);

        var definitions = LoadDefinitions(car);
        var parameters = new List<CarSetupParameter>();
        string section = "";

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            if (!line.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(section))
                continue;

            var raw = line[(line.IndexOf('=') + 1)..].Trim();
            double? numeric = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
            definitions.TryGetValue(section, out var range);

            parameters.Add(new CarSetupParameter
            {
                Section = section,
                Category = Classify(section),
                CurrentRaw = raw,
                CurrentValue = numeric,
                RecommendedValue = numeric,
                Range = range
            });
        }

        if (parameters.Count == 0)
            throw new InvalidDataException("This file does not contain Assetto Corsa setup sections with VALUE= entries.");

        return new CarSetupAnalysis
        {
            BaselinePath = path,
            CarFolderName = car.SourceFolderName ?? "unknown-car",
            SetupDefinitionPath = FindSetupDefinition(car),
            Parameters = parameters
        };
    }

    public string WriteGenerated(CarSetupAnalysis analysis, string outputPath)
    {
        var sourceFull = Path.GetFullPath(analysis.BaselinePath);
        var outputFull = Path.GetFullPath(outputPath);
        if (sourceFull.Equals(outputFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a new filename. Atomic Drift Tuner will not overwrite the baseline setup.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputFull) ?? throw new InvalidOperationException("Invalid output folder."));

        var bySection = analysis.Parameters
            .Where(x => x.Changed)
            .ToDictionary(x => x.Section, x => x.RecommendedRaw, StringComparer.OrdinalIgnoreCase);

        var output = new List<string>();
        string section = "";
        foreach (var rawLine in File.ReadLines(sourceFull))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                section = trimmed[1..^1].Trim();

            if (trimmed.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase) && bySection.TryGetValue(section, out var replacement))
                output.Add($"VALUE={replacement}");
            else
                output.Add(rawLine);
        }

        File.WriteAllLines(outputFull, output);
        return outputFull;
    }

    private Dictionary<string, SetupRangeDefinition> LoadDefinitions(CarProfile car)
    {
        var path = FindSetupDefinition(car);
        var result = new Dictionary<string, SetupRangeDefinition>(StringComparer.OrdinalIgnoreCase);
        if (path is null) return result;

        string section = "";
        var raw = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                if (!raw.ContainsKey(section)) raw[section] = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || string.IsNullOrWhiteSpace(section)) continue;
            raw[section][line[..eq].Trim()] = line[(eq + 1)..].Split(';')[0].Trim();
        }

        bool globalClicks = raw.TryGetValue("DISPLAY_METHOD", out var display) &&
                            display.TryGetValue("SHOW_CLICKS", out var clicks) && clicks == "1";

        foreach (var (name, values) in raw)
        {
            bool sectionClicks = globalClicks;
            if (values.TryGetValue("SHOW_CLICKS", out var sectionClickValue)) sectionClicks = sectionClickValue == "1";
            var d = new SetupRangeDefinition { Section = name, ShowClicks = sectionClicks, Source = "data/setup.ini" };
            if (values.TryGetValue("MIN", out var min) && TryNum(min, out var mn)) d.Min = mn;
            if (values.TryGetValue("MAX", out var max) && TryNum(max, out var mx)) d.Max = mx;
            if (values.TryGetValue("STEP", out var step) && TryNum(step, out var st)) d.Step = st;
            if (values.TryGetValue("NAME", out var friendly)) d.Name = friendly;
            if (values.TryGetValue("UNITS", out var units)) d.Units = units;
            if (d.Min is not null || d.Max is not null || d.Step is not null || d.Name is not null)
                result[name] = d;
        }
        return result;
    }

    private static bool TryNum(string raw, out double value) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string? FindSetupDefinition(CarProfile car)
    {
        if (string.IsNullOrWhiteSpace(car.SourceFolderPath)) return null;
        var candidate = Path.Combine(car.SourceFolderPath, "data", "setup.ini");
        return File.Exists(candidate) ? candidate : null;
    }

    private static CarSetupCategory Classify(string section)
    {
        var s = section.ToUpperInvariant();
        if (s.StartsWith("PRESSURE") || s == "TYRES") return CarSetupCategory.Tires;
        if (s.StartsWith("CAMBER") || s.StartsWith("TOE")) return CarSetupCategory.Alignment;
        if (s.Contains("DAMP") || s.Contains("REBOUND")) return CarSetupCategory.Dampers;
        if (s.Contains("SPRING") || s.Contains("ARB") || s.Contains("ROD_LENGTH") || s.Contains("PACKER") || s.Contains("BUMPSTOP")) return CarSetupCategory.Suspension;
        if (s.StartsWith("DIFF")) return CarSetupCategory.Differential;
        if (s.Contains("BRAKE") || s.Contains("BIAS")) return CarSetupCategory.Brakes;
        if (s.Contains("RATIO") || s.StartsWith("GEAR")) return CarSetupCategory.Gearing;
        if (s.Contains("WING") || s.Contains("AERO")) return CarSetupCategory.Aero;
        if (s == "FUEL") return CarSetupCategory.Fuel;
        if (s is "ABS" or "TC" || s.Contains("TRACTION")) return CarSetupCategory.Electronics;
        return CarSetupCategory.Other;
    }
}
