using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed partial class AssettoCorsaScanner
{
    public string? TryFindInstall()
    {
        var candidates = new List<string>();

        AddIfPresent(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "assettocorsa"));

        AddIfPresent(candidates, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Steam", "steamapps", "common", "assettocorsa"));

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath")?.ToString();
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                AddIfPresent(candidates, Path.Combine(steamPath, "steamapps", "common", "assettocorsa"));
                AddSteamLibraries(candidates, steamPath);
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 244210");
            var install = key?.GetValue("InstallLocation")?.ToString();
            AddIfPresent(candidates, install);
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 244210");
            var install = key?.GetValue("InstallLocation")?.ToString();
            AddIfPresent(candidates, install);
        }
        catch { }

        return candidates.FirstOrDefault(IsAssettoCorsaRoot);
    }

    public AssettoCorsaScanResult Scan(string rootOrCarsPath)
    {
        if (string.IsNullOrWhiteSpace(rootOrCarsPath))
            throw new ArgumentException("Choose an Assetto Corsa folder first.");

        string root = NormalizeRoot(rootOrCarsPath);
        string carsPath = Path.Combine(root, "content", "cars");
        if (!Directory.Exists(carsPath))
            throw new DirectoryNotFoundException($"Could not find content\\cars under: {root}");

        var result = new AssettoCorsaScanResult { RootPath = root };

        foreach (var dir in Directory.EnumerateDirectories(carsPath).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                result.Cars.Add(ReadCar(dir));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{Path.GetFileName(dir)}: {ex.Message}");
            }
        }

        DiscoverFolderPrefixPacks(result);
        return result;
    }

    private static void DiscoverFolderPrefixPacks(AssettoCorsaScanResult result)
    {
        // Known signatures always win. Only cars that would otherwise land in
        // Custom / Other participate in automatic pack discovery.
        var customCars = result.Cars
            .Where(x => x.PackId == "custom-pack" && !string.IsNullOrWhiteSpace(x.SourceFolderName))
            .ToList();

        if (customCars.Count < 2)
            return;

        var groups = customCars
            .Select(car => new
            {
                Car = car,
                Tokens = FolderTokens(car.SourceFolderName!)
            })
            .Where(x => x.Tokens.Count >= 3)
            .Select(x => new
            {
                x.Car,
                Prefix = x.Tokens.Take(2).ToArray()
            })
            .Where(x => IsUsefulPackPrefix(x.Prefix))
            .GroupBy(x => string.Join("_", x.Prefix), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var prefix = group.First().Prefix;
            string slug = string.Join("-", prefix.Select(SlugToken));
            string packId = $"auto-pack-{slug}";
            string packName = string.Join(" ", prefix.Select(DisplayToken));

            // Protect against a future built-in ID or another inferred group
            // accidentally colliding with this generated ID.
            if (result.DiscoveredPacks.Any(x => x.Id.Equals(packId, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.DiscoveredPacks.Add(new DriftPackProfile
            {
                Id = packId,
                Name = packName,
                Category = "Auto-Detected",
                GripBias = 0,
                SelfSteerBias = 0,
                DampingBias = 0,
                DetailBias = 0,
                IsCustom = true
            });

            foreach (var item in group)
            {
                item.Car.PackId = packId;
                item.Car.DataSourceSummary = string.IsNullOrWhiteSpace(item.Car.DataSourceSummary)
                    ? $"auto-pack: {packName}"
                    : $"{item.Car.DataSourceSummary}, auto-pack: {packName}";
            }
        }
    }

    private static List<string> FolderTokens(string folder) =>
        Regex.Matches(folder, @"[A-Za-z0-9]+")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

    private static bool IsUsefulPackPrefix(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2)
            return false;

        // Avoid creating nonsense packs such as "Nissan Silvia" just because
        // several unrelated cars share a manufacturer/chassis naming scheme.
        var genericFirstTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ac", "assetto", "assettocorsa", "car", "cars", "mod", "mods",
            "nissan", "toyota", "bmw", "mazda", "ford", "honda", "lexus",
            "mercedes", "benz", "porsche", "audi", "volkswagen", "vw",
            "chevrolet", "chevy", "dodge", "subaru", "mitsubishi", "ferrari",
            "lamborghini", "hyundai", "kia", "volvo"
        };

        if (genericFirstTokens.Contains(tokens[0]))
            return false;

        return tokens.All(x => x.Length >= 2 && !x.All(char.IsDigit));
    }

    private static string SlugToken(string token) =>
        new(token.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string DisplayToken(string token)
    {
        var acronyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "vdc", "vdm", "adl", "wdt", "wdts", "dwg", "bdc"
        };

        if (acronyms.Contains(token))
            return token.ToUpperInvariant();

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant());
    }

    private CarProfile ReadCar(string dir)
    {
        string folder = Path.GetFileName(dir);
        string displayName = folder.Replace('_', ' ').Trim();
        string author = "";
        string brand = "";
        string tags = "";
        double? mass = null;
        double? power = null;
        double? torque = null;
        var sources = new List<string>();

        string uiJson = Path.Combine(dir, "ui", "ui_car.json");
        if (File.Exists(uiJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(uiJson), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
                var root = doc.RootElement;
                displayName = GetString(root, "name") ?? displayName;
                author = GetString(root, "author") ?? "";
                brand = GetString(root, "brand") ?? "";

                if (root.TryGetProperty("tags", out var tagElement))
                    tags = tagElement.ToString();

                if (root.TryGetProperty("specs", out var specs) && specs.ValueKind == JsonValueKind.Object)
                {
                    power = ParseNumber(GetString(specs, "bhp") ?? GetString(specs, "power"));
                    mass = ParseNumber(GetString(specs, "weight"));
                    torque = ParseNumber(GetString(specs, "torque"));
                }
                sources.Add("ui_car.json");
            }
            catch
            {
                sources.Add("folder name (ui_car.json unreadable)");
            }
        }

        string searchable = $"{folder} {displayName} {author} {brand} {tags}".ToLowerInvariant();
        string packId = InferPack(searchable);
        var car = DefaultsForPack(packId);
        car.Id = $"installed:{folder}";
        car.PackId = packId;
        car.DisplayName = displayName;
        car.IsInstalled = true;
        car.SourceFolderName = folder;
        car.SourceFolderPath = dir;
        car.Author = string.IsNullOrWhiteSpace(author) ? null : author;

        if (mass is >= 500 and <= 3000) { car.MassKg = mass.Value; car.Confidence.Mass = DataConfidence.High; }
        if (power is >= 50 and <= 2500) { car.PowerHp = power.Value; car.Confidence.Power = DataConfidence.High; }
        if (torque is >= 50 and <= 3000) car.TorqueNm = torque.Value;
        car.Confidence.Grip = packId == "custom-pack" ? DataConfidence.Low : DataConfidence.Medium;
        car.Confidence.Caster = DataConfidence.Low;

        string carIni = Path.Combine(dir, "data", "car.ini");
        if (File.Exists(carIni))
        {
            var ini = ReadIni(carIni);
            var totalMass = IniNumber(ini, "BASIC", "TOTALMASS");
            if (totalMass is >= 500 and <= 3000) { car.MassKg = totalMass.Value; car.Confidence.Mass = DataConfidence.High; }

            // Some mods expose a road-wheel lock here, others expose a steering-wheel value.
            // Only accept physically plausible per-side road-wheel values and ignore the rest.
            var steerLock = IniNumber(ini, "CONTROLS", "STEER_LOCK");
            if (steerLock is >= 20 and <= 80) { car.SteeringLockPerSideDeg = steerLock.Value; car.Confidence.SteeringLock = DataConfidence.High; }
            sources.Add("data/car.ini");
        }

        string tyresIni = Path.Combine(dir, "data", "tyres.ini");
        if (File.Exists(tyresIni))
        {
            var ini = ReadIni(tyresIni);
            var width = IniNumber(ini, "FRONT", "WIDTH");
            if (width is > 0 and < 1) width *= 1000;
            if (width is >= 150 and <= 400) { car.FrontTireWidthMm = width.Value; car.Confidence.FrontTireWidth = DataConfidence.High; }
            sources.Add("data/tyres.ini");
        }

        if (File.Exists(Path.Combine(dir, "data.acd")) && !Directory.Exists(Path.Combine(dir, "data")))
            sources.Add("data.acd present; packed physics not read");

        car.DataSourceSummary = sources.Count == 0 ? "folder-name fallback" : string.Join(", ", sources.Distinct());
        return car;
    }

    private static CarProfile DefaultsForPack(string packId) => packId switch
    {
        "vdc" => new CarProfile { MassKg=1350, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        "adl" => new CarProfile { MassKg=1400, PowerHp=900, TorqueNm=950, SteeringLockPerSideDeg=65, CasterDeg=8.5, FrontTireWidthMm=275, RearTireWidthMm=285, Grip=GripLevel.High },
        "gravy" => new CarProfile { MassKg=1250, PowerHp=350, TorqueNm=480, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },
        "swarm" => new CarProfile { MassKg=1300, PowerHp=450, TorqueNm=520, SteeringLockPerSideDeg=60, CasterDeg=7.5, FrontTireWidthMm=245, RearTireWidthMm=265, Grip=GripLevel.Medium },
        "wdts" => new CarProfile { MassKg=1250, PowerHp=400, TorqueNm=480, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=235, RearTireWidthMm=245, Grip=GripLevel.Medium },
        "dwg" => new CarProfile { MassKg=1300, PowerHp=430, TorqueNm=510, SteeringLockPerSideDeg=60, CasterDeg=7.3, FrontTireWidthMm=245, RearTireWidthMm=255, Grip=GripLevel.Medium },
        _ => new CarProfile { MassKg=1300, PowerHp=400, TorqueNm=450, SteeringLockPerSideDeg=60, CasterDeg=7.0, FrontTireWidthMm=265, RearTireWidthMm=265, Grip=GripLevel.Medium, IsCustom=true }
    };

    private static string InferPack(string text)
    {
        if (ContainsAny(text, "virtual drift championship", "vdc")) return "vdc";
        if (ContainsAny(text, "gravy garage", "gravygarage", "gravy")) return "gravy";
        if (ContainsAny(text, "team swarm", "teamswarm", "swarm")) return "swarm";
        if (ContainsAny(text, "assetto drift league", "adl")) return "adl";
        if (ContainsAny(text, "world drift tour", "wdts", "wdt")) return "wdts";
        if (ContainsAny(text, "deathwish garage", "deathwish", "dwg")) return "dwg";
        return "custom-pack";
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeRoot(string path)
    {
        string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
        if (Directory.Exists(Path.Combine(full, "content", "cars"))) return full;

        var di = new DirectoryInfo(full);
        if (di.Name.Equals("cars", StringComparison.OrdinalIgnoreCase) &&
            di.Parent?.Name.Equals("content", StringComparison.OrdinalIgnoreCase) == true &&
            di.Parent.Parent is not null)
            return di.Parent.Parent.FullName;

        return full;
    }

    private static bool IsAssettoCorsaRoot(string path) => Directory.Exists(Path.Combine(path, "content", "cars"));

    private static void AddIfPresent(List<string> list, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && !list.Contains(path, StringComparer.OrdinalIgnoreCase))
            list.Add(path);
    }

    private static void AddSteamLibraries(List<string> candidates, string steamPath)
    {
        string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return;
        foreach (string line in File.ReadLines(vdf))
        {
            if (!line.Contains("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(line, "\\\"path\\\"\\s+\\\"(?<p>[^\\\"]+)\\\"");
            if (!match.Success) continue;
            string library = match.Groups["p"].Value.Replace("\\\\", "\\");
            AddIfPresent(candidates, Path.Combine(library, "steamapps", "common", "assettocorsa"));
        }
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = NumberRegex().Match(text.Replace(',', '.'));
        if (!match.Success) return null;
        return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                if (!result.ContainsKey(section))
                    result[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals <= 0 || string.IsNullOrEmpty(section)) continue;
            result[section][line[..equals].Trim()] = line[(equals + 1)..].Split(';')[0].Trim();
        }
        return result;
    }

    private static double? IniNumber(Dictionary<string, Dictionary<string, string>> ini, string section, string key)
    {
        if (!ini.TryGetValue(section, out var values) || !values.TryGetValue(key, out var raw)) return null;
        return ParseNumber(raw);
    }

    [GeneratedRegex(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled)]
    private static partial Regex NumberRegex();
}
