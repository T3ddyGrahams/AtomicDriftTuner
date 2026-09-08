namespace AtomicDriftTuner.Models;

public sealed class DriverIdentity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Local driver";
    public override string ToString() => Name;
}

public sealed class TuneVersion
{
    public string Schema { get; set; } = "adt/tune-version/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string DriverId { get; set; } = "";
    public string ContextKey { get; set; } = "";
    public string Label { get; set; } = "Baseline";
    public CarBehaviorTarget DesiredBehavior { get; set; } = new();
    public SortedDictionary<string, double> Settings { get; set; } = new(StringComparer.Ordinal);
    public string SetupFileName { get; set; } = "";
    public string SetupSha256 { get; set; } = "";
    public string Source { get; set; } = "Generated ADT recommendation; live hardware values are not read";
    public string DisplayName => $"{CreatedUtc.ToLocalTime():g} • {Label} • {Id[..Math.Min(8, Id.Length)]}";
    public override string ToString() => DisplayName;
}

public sealed class RunContext
{
    public string Schema { get; set; } = "adt/run-context/1";
    public string DriverId { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string TrackId { get; set; } = "";
    public string Conditions { get; set; } = "";
    public bool CarIdentityVerified { get; set; }
    public bool TuneConfirmedInUse { get; set; }
    public bool Interrupted { get; set; }
    public TuneVersion? Tune { get; set; }
    public string RecommendationSessionId { get; set; } = "";
    public List<string> TestedRecommendations { get; set; } = [];
}

public sealed class DriftEvent
{
    public string Phase { get; set; } = "";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    public double SpeedKmh { get; set; }
    public string Evidence { get; set; } = "";
}

public sealed class RunMetric
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public double? Value { get; set; }
    public string Unit { get; set; } = "";
    public int Events { get; set; }
    public double EvidenceSeconds { get; set; }
    public string Confidence { get; set; } = "LOW";
    public string Evidence { get; set; } = "";
    public string DisplayValue => Value is double v ? $"{v:0.###} {Unit}" : "Insufficient data";
}

public sealed class DriftDiagnosis
{
    public string AnalyzerVersion { get; set; } = "drift-diagnosis/1";
    public int InvalidSamples { get; set; }
    public int Discontinuities { get; set; }
    public bool TimelineReset { get; set; }
    public double ExcludedSeconds { get; set; }
    public double UsableDriftSeconds { get; set; }
    public double LeftDriftSeconds { get; set; }
    public double RightDriftSeconds { get; set; }
    public double LowSpeedSeconds { get; set; }
    public double MediumSpeedSeconds { get; set; }
    public double HighSpeedSeconds { get; set; }
    public List<DriftEvent> Events { get; set; } = [];
    public List<RunMetric> Metrics { get; set; } = [];
    public List<string> QualityNotes { get; set; } = [];
    public RunMetric? Metric(string key) => Metrics.FirstOrDefault(x => x.Key == key);
}

public sealed class RunComparison
{
    public bool RecommendationTestTracked { get; set; }
    public string Verdict { get; set; } = "Inconclusive";
    public string Summary { get; set; } = "Select a baseline run.";
    public bool Comparable { get; set; }
    public List<string> Limitations { get; set; } = [];
    public List<AssistantComparisonRow> Metrics { get; set; } = [];
    public List<AssistantComparisonRow> TuneChanges { get; set; } = [];
}

public sealed class RunReview
{
    public string Schema { get; set; } = "adt/run-review/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime ReviewedUtc { get; set; } = DateTime.UtcNow;
    public string SessionId { get; set; } = "";
    public string BaselineSessionId { get; set; } = "";
    public string DriverId { get; set; } = "";
    public string ContextKey { get; set; } = "";
    public string DriverRating { get; set; } = "Not rated";
    public string Notes { get; set; } = "";
    public RunComparison Comparison { get; set; } = new();
    public string DisplayName => $"{ReviewedUtc.ToLocalTime():g} • {DriverRating} • {Comparison.Verdict}";
    public string Conclusion => !Comparison.Comparable ? "Inconclusive comparison; driver feedback is retained." :
        DriverRating == "Better" && Comparison.Verdict == "Closer to goals" && Comparison.RecommendationTestTracked ? "Driver and telemetry support improvement in this recorded recommendation test." :
        DriverRating == "Worse" && Comparison.Verdict == "Closer to goals" || DriverRating == "Better" && Comparison.Verdict == "Farther from goals" ? "Driver feedback and telemetry disagree; improvement is not confirmed." :
        DriverRating == "Tradeoff" || Comparison.Verdict == "Tradeoff" ? "Mixed outcome: review the improvements and tradeoffs before keeping this change." :
        "No confirmed recommendation improvement; retain the observation and compare another run.";
}
