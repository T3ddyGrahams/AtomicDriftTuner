using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AppSettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public AppSettingsStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _json));
    }
}
