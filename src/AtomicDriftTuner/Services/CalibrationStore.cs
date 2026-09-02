using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class CalibrationStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public CalibrationStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "calibrations.json");
    }

    public CalibrationProfile? Get(string key) => LoadAll().FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public void Upsert(CalibrationProfile calibration)
    {
        var all = LoadAll();
        var index = all.FindIndex(x => x.Key.Equals(calibration.Key, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) all[index] = calibration; else all.Add(calibration);
        File.WriteAllText(_path, JsonSerializer.Serialize(all, _json));
    }

    public void Delete(string key)
    {
        var all = LoadAll();
        all.RemoveAll(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(_path, JsonSerializer.Serialize(all, _json));
    }

    private List<CalibrationProfile> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<CalibrationProfile>>(File.ReadAllText(_path), _json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
