using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class ProfileStore
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void Save(SavedTune tune, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(tune, _options));

    public SavedTune Load(string path) =>
        JsonSerializer.Deserialize<SavedTune>(File.ReadAllText(path), _options)
        ?? throw new InvalidDataException("Invalid tune profile.");
}
