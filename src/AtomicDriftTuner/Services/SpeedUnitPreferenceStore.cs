using System.Text.Json;

namespace AtomicDriftTuner.Services;

/// <summary>One shared display default; car targets and all calculations retain canonical km/h.</summary>
public sealed class SpeedUnitPreferenceStore
{
    private static readonly object Gate = new();
    private readonly string _path;
    public SpeedUnitPreferenceStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner", "speed-units.json");
    public bool? Load()
    {
        lock (Gate)
        {
            if (!File.Exists(_path)) return null;
            if (new FileInfo(_path).Length > 1024) throw new InvalidDataException("The speed-unit preference is damaged; it was preserved.");
            using var json = JsonDocument.Parse(File.ReadAllText(_path));
            return json.RootElement.GetProperty("DisplayMph").GetBoolean();
        }
    }
    public void Save(bool mph)
    {
        lock (Gate)
        {
            _ = Load(); // Preserve damaged existing preferences for recovery.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, JsonSerializer.Serialize(new { DisplayMph = mph })); File.Move(temp, _path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
