namespace AtomicDriftTuner.Models;

/// <summary>Local evidence only. Never serialize installed paths into shared run history.</summary>
public sealed record CarDataEvidence
{
    public string CarPath { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public IReadOnlyDictionary<string, string> Fingerprints { get; init; } = new Dictionary<string, string>();
}
