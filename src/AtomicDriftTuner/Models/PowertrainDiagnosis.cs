namespace AtomicDriftTuner.Models;

public sealed record EngineMapPoint(double Rpm, double Multiplier);
public sealed record EngineMapDefinition(int Index, string Name, IReadOnlyList<EngineMapPoint> Points);
public sealed record RecordedGearRatio(int Gear, double Ratio, string Source);

/// <summary>File-derived context captured with a run, never a claim about effective live physics.</summary>
public sealed class PowertrainContext
{
    public string Version { get; set; } = "powertrain-context/1";
    public string PhysicsFingerprint { get; set; } = "";
    public double? FinalDrive { get; set; }
    public string FinalDriveSource { get; set; } = "";
    public List<RecordedGearRatio> Gears { get; set; } = [];
    public double? BaseLimiterRpm { get; set; }
    public bool AdjustableLimiter { get; set; }
    public List<string> Limitations { get; set; } = [];
}

public sealed class PowertrainDiagnosis
{
    public string AnalyzerVersion { get; set; } = "powertrain-diagnosis/1";
    public string Summary { get; set; } = "No usable forward-gear RPM evidence.";
    public string SetupContext { get; set; } = "No gearing or engine-map snapshot was recorded.";
    public string TargetContext { get; set; } = "No RPM target was saved for this recording. Observed RPM is not a detected power band.";
    public string EcuContext { get; set; } = "No verified ECU mapping was recorded.";
    public string NextTest { get; set; } = "Record comparable runs before choosing a gearing or ECU test.";
    public string Limitations { get; set; } = "";
    public List<PowertrainGearEvidence> Gears { get; set; } = [];
    public List<PowertrainPhaseEvidence> Phases { get; set; } = [];
    public List<PowertrainEvent> Events { get; set; } = [];
    public List<EngineMapPoint> MapPoints { get; set; } = [];
}

public sealed class PowertrainGearEvidence
{
    public int Gear { get; set; }
    public double Seconds { get; set; }
    public double LowRpm { get; set; }
    public double MedianRpm { get; set; }
    public double HighRpm { get; set; }
    public double LowSpeedKmh { get; set; }
    public double HighSpeedKmh { get; set; }
    public double HighThrottleSeconds { get; set; }
    public double? BelowTargetSeconds { get; set; }
    public double? AboveTargetSeconds { get; set; }
    public double? TargetSpeedSeconds { get; set; }
    public double? NearBaseLimiterSeconds { get; set; }
    public double? RecordedRatio { get; set; }
    public string Confidence { get; set; } = "LOW";
    public string RpmBand => $"{LowRpm:0}–{HighRpm:0}";
    public string SpeedBand => $"{LowSpeedKmh:0.0}–{HighSpeedKmh:0.0}";
    public string TargetExposure => BelowTargetSeconds is double below && AboveTargetSeconds is double above ? $"{below:0.0}s below / {above:0.0}s above ({TargetSpeedSeconds:0.0}s in target speed range)" : "No recorded target for this gear";
    public string LimiterExposure => NearBaseLimiterSeconds is double seconds ? $"{seconds:0.0}s" : "Unknown";
}

public sealed record PowertrainPhaseEvidence(int Gear, string Phase, double Seconds, double MedianRpm, double LowRpm, double HighRpm)
{
    public string RpmBand => $"{LowRpm:0}–{HighRpm:0}";
}

public sealed class PowertrainEvent
{
    public string Kind { get; set; } = "";
    public int Gear { get; set; }
    public string Phase { get; set; } = "";
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);
    public double StartRpm { get; set; }
    public double EndRpm { get; set; }
    public string Evidence { get; set; } = "";
}
