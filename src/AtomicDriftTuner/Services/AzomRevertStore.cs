using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AzomRevertStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public AzomRevertStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "azom-last-apply-backup.json");
    }

    public void Save(AzomLiveSnapshot snapshot, IEnumerable<string> changedProperties) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(new AzomRevertRecord { Snapshot = snapshot, ChangedProperties = changedProperties.Distinct(StringComparer.OrdinalIgnoreCase).ToList() }, Json));

    public AzomRevertRecord? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<AzomRevertRecord>(File.ReadAllText(_path), Json);
        }
        catch { return null; }
    }
}
