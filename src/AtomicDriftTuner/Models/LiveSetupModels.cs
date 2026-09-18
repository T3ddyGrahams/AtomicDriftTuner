namespace AtomicDriftTuner.Models;

/// <summary>Transient CSP response. The raw INI and transport envelope are never part of a saved snapshot.</summary>
public sealed class LiveSetupCaptureRequest
{
    public bool Available { get; set; }
    public int ProtocolVersion { get; set; }
    public string Source { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string WindowId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string SetupIni { get; set; } = "";
    public string CarId { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string TrackLayout { get; set; } = "";
    public int SessionIndex { get; set; } = -1;
    public int SessionType { get; set; }
    public long SessionGeneration { get; set; } = -1;
    public long SetupRevision { get; set; } = -1;
    public long CaptureSequence { get; set; }
    public double SimTimeMs { get; set; } = -1;
    public long Frame { get; set; } = -1;
}

/// <summary>Read-only numeric evidence captured from CSP, not an instruction to apply a setup.</summary>
public sealed record CapturedCarSetup
{
    public string Source { get; init; } = "csp-current-setup";
    public IReadOnlyDictionary<string, double> Values { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, double>(new Dictionary<string, double>());
    public string Sha256 { get; init; } = "";
    public string CarId { get; init; } = "";
    public string TrackId { get; init; } = "";
    public string TrackLayout { get; init; } = "";
    public int SessionIndex { get; init; }
    public int SessionType { get; init; }
    public long SessionGeneration { get; init; }
    public long SetupRevision { get; init; }
    public long CaptureSequence { get; init; }
    public double SimTimeMs { get; init; }
    public long Frame { get; init; }
    public DateTime ReceivedUtc { get; init; }
}
