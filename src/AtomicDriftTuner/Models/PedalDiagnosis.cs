namespace AtomicDriftTuner.Models;

public sealed class PedalDiagnosis
{
    public string AnalyzerVersion { get; set; } = "pedal-diagnosis/1";
    public double DriftSeconds { get; set; }
    public double KnownSignalSeconds { get; set; }
    public List<RunMetric> ContextMetrics { get; set; } = [];
    public List<PedalEvent> Events { get; set; } = [];
    public string Summary { get; set; } = "No usable pedal evidence.";
    public string Limitations { get; set; } = "";
    public RunMetric? Metric(string key) => ContextMetrics.FirstOrDefault(m => m.Key == key);
}

public sealed class PedalEvent
{
    public string Kind { get; set; } = "";
    public string Phase { get; set; } = "";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    public string Input { get; set; } = "";
    public bool InDrift { get; set; }
    public bool ResponseComplete { get; set; }
    public bool GearChanged { get; set; }
    public double? AngleChangeDeg { get; set; }
    public double? YawChangeDegPerSec { get; set; }
    public double? RearSlipChange { get; set; }
    public double? RpmPeakChange { get; set; }
    public string Confidence { get; set; } = "LOW";
    public string Evidence { get; set; } = "";
}
