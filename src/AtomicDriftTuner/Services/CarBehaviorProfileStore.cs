using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class CarBehaviorProfileStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public CarBehaviorProfileStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AtomicDriftTuner");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "car-behavior-targets.json");
    }

    public CarBehaviorTarget Load(TuneInput input)
    {
        var key = BuildKey(input);
        var all = LoadAll();

        if (all.TryGetValue(key, out var saved))
        {
            saved.Normalize();
            saved.Key = key;
            if (string.IsNullOrWhiteSpace(saved.DisplayName))
                saved.DisplayName = BuildDisplayName(input);
            return saved;
        }

        return new CarBehaviorTarget
        {
            Key = key,
            DisplayName = BuildDisplayName(input)
        };
    }

    public void Save(TuneInput input, CarBehaviorTarget target)
    {
        target.Normalize();
        target.Key = BuildKey(input);
        target.DisplayName = BuildDisplayName(input);
        target.UpdatedUtc = DateTime.UtcNow;

        var all = LoadAll();
        all[target.Key] = target;

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all, _json));
        File.Move(temp, _path, true);
    }

    public static string BuildKey(TuneInput input)
    {
        var pack = string.IsNullOrWhiteSpace(input.DriftPack.Id)
            ? "custom-pack"
            : input.DriftPack.Id.Trim();

        var car = !string.IsNullOrWhiteSpace(input.Car.SourceFolderName)
            ? input.Car.SourceFolderName!.Trim()
            : !string.IsNullOrWhiteSpace(input.Car.Id)
                ? input.Car.Id.Trim()
                : input.Car.DisplayName.Trim();

        return $"{pack}|{car}".ToLowerInvariant();
    }

    private Dictionary<string, CarBehaviorTarget> LoadAll()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, CarBehaviorTarget>(StringComparer.OrdinalIgnoreCase);

            var loaded = JsonSerializer.Deserialize<Dictionary<string, CarBehaviorTarget>>(
                File.ReadAllText(_path), _json);

            return loaded is null
                ? new Dictionary<string, CarBehaviorTarget>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, CarBehaviorTarget>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A damaged behavior-profile file must never stop the tuner from opening.
            return new Dictionary<string, CarBehaviorTarget>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string BuildDisplayName(TuneInput input) =>
        $"{input.DriftPack.Name} • {input.Car.DisplayName}";
}
