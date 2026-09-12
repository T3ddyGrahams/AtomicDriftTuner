using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class GearingTargetStore
{
    private static readonly object Gate = new();
    private readonly string _root;
    public GearingTargetStore(string? root = null) => _root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner", "gearing-targets");
    private string FilePath(TuneInput input) => Path.Combine(_root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CarBehaviorProfileStore.BuildKey(input)))) + ".json");

    public GearingTarget? Load(TuneInput input)
    {
        lock (Gate)
        {
            var path = FilePath(input);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("The saved gearing target is unexpectedly large.");
            var target = JsonSerializer.Deserialize<GearingTarget>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The saved gearing target is empty.");
            target.Validate();
            return target;
        }
    }

    public void Save(TuneInput input, GearingTarget target)
    {
        target.Validate();
        lock (Gate)
        {
            // Never silently discard a damaged existing profile.
            _ = Load(input);
            Directory.CreateDirectory(_root);
            var path = FilePath(input);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(target, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
