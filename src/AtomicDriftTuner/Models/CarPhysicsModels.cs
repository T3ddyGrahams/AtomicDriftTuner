namespace AtomicDriftTuner.Models;

public sealed record CarPhysicsFact(string File, string Section, string Key, string Label, string Value, string Unit)
{
    public string Display => $"{Label}: {Value}{(Unit.Length > 0 ? " " + Unit : "")} — {File} [{Section}] {Key}";
}

public sealed record CarPhysicsSnapshot
{
    public string CarPath { get; init; } = "";
    public string CarId { get; init; } = "";
    public string Status { get; init; } = "Car physics not imported.";
    public bool Available { get; init; }
    public string Fingerprint { get; init; } = "";
    public string DriveType { get; init; } = "";
    public bool HasSetupDefinition { get; init; }
    public IReadOnlyList<string> AdjustableSections { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CarPhysicsFact> Facts { get; init; } = Array.Empty<CarPhysicsFact>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Fingerprints { get; init; } = new Dictionary<string, string>();
    public string Details => string.Join("\n", Notes.Concat(Facts.Select(f => f.Display)));
    public CarPhysicsFact? Find(string file, string section, string key) => Facts.FirstOrDefault(f => f.File == file && f.Section == section && f.Key == key);
}
