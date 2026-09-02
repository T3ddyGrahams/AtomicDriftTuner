namespace AtomicDriftTuner.Models;

public enum GripLevel { Low, Medium, High }
public enum DriftStyleKind { Training, Realistic, FastSelfSteer, Tandem, Competition }
public enum DataConfidence { Unknown, Low, Medium, High }

public sealed class HardwareProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Manufacturer { get; set; } = "MOZA";
    public string Model { get; set; } = "Custom Direct Drive Base";
    public double PeakTorqueNm { get; set; } = 9;
    public int MaxRotationDeg { get; set; } = 2700;
    public bool IsCustom { get; set; }
    public override string ToString() => $"{Manufacturer} {Model}";
}

public sealed class SteeringWheelProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Manufacturer { get; set; } = "MOZA";
    public string Model { get; set; } = "Custom / Other";
    public double DiameterMm { get; set; } = 330;
    public double InertiaFactor { get; set; } = 1.0;
    public bool IsRound { get; set; } = true;
    public bool IsCustom { get; set; }
    public override string ToString() => $"{Manufacturer} {Model}";
}

public sealed class DriftPackProfile
{
    public string Id { get; set; } = "custom-pack";
    public string Name { get; set; } = "Custom / Other Pack";
    public string Category { get; set; } = "Custom";
    public double GripBias { get; set; }
    public double SelfSteerBias { get; set; }
    public double DampingBias { get; set; }
    public double DetailBias { get; set; }
    public bool IsCustom { get; set; }
    public override string ToString() => Name;
}

public sealed class CarDataConfidence
{
    public DataConfidence Mass { get; set; } = DataConfidence.Low;
    public DataConfidence Power { get; set; } = DataConfidence.Low;
    public DataConfidence Caster { get; set; } = DataConfidence.Low;
    public DataConfidence SteeringLock { get; set; } = DataConfidence.Low;
    public DataConfidence FrontTireWidth { get; set; } = DataConfidence.Low;
    public DataConfidence Grip { get; set; } = DataConfidence.Low;

    public int Score => (int)Math.Round(new[] { Mass, Power, Caster, SteeringLock, FrontTireWidth, Grip }
        .Average(x => x switch { DataConfidence.High => 100, DataConfidence.Medium => 65, DataConfidence.Low => 30, _ => 0 }));
}

public sealed class CarProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PackId { get; set; } = "custom-pack";
    public string DisplayName { get; set; } = "Custom Assetto Corsa Car";
    public double MassKg { get; set; } = 1300;
    public double PowerHp { get; set; } = 400;
    public double TorqueNm { get; set; } = 450;
    public string Drivetrain { get; set; } = "RWD";
    public double SteeringLockPerSideDeg { get; set; } = 60;
    public double CasterDeg { get; set; } = 7;
    public double FrontTireWidthMm { get; set; } = 265;
    public double RearTireWidthMm { get; set; } = 265;
    public GripLevel Grip { get; set; } = GripLevel.Medium;
    public bool IsCustom { get; set; }

    public bool IsInstalled { get; set; }
    public string? SourceFolderName { get; set; }
    public string? SourceFolderPath { get; set; }
    public string? Author { get; set; }
    public string? DataSourceSummary { get; set; }
    public CarDataConfidence Confidence { get; set; } = new();

    public override string ToString() => IsInstalled ? $"{DisplayName}  [Installed]" : DisplayName;
}

public sealed class DriftIntent
{
    public DriftStyleKind Kind { get; set; }
    public string Name { get; set; } = "";
    public double SelfSteer { get; set; }
    public double Stability { get; set; }
    public double Detail { get; set; }
    public double Weight { get; set; }
    public override string ToString() => Name;
}

public sealed class TuneInput
{
    public HardwareProfile Hardware { get; set; } = new();
    public SteeringWheelProfile Wheel { get; set; } = new();
    public DriftPackProfile DriftPack { get; set; } = new();
    public CarProfile Car { get; set; } = new();
    public DriftIntent Intent { get; set; } = new();
}

public sealed class AssettoCorsaSettings
{
    public int GainPct { get; set; }
    public int FilterPct { get; set; }
    public int MinimumForcePct { get; set; }
    public int KerbPct { get; set; }
    public int RoadPct { get; set; }
    public int SlipPct { get; set; }
    public int AbsPct { get; set; }
}

public sealed class TuneResult
{
    public AzomSettings Azom { get; set; } = new();
    public AssettoCorsaSettings Ac { get; set; } = new();
    public double EstimatedPeakWheelTorqueNm { get; set; }
    public int SelfSteerScore { get; set; }
    public int StabilityScore { get; set; }
    public int DetailScore { get; set; }
    public string CalibrationSummary { get; set; } = "No saved calibration";
    public List<string> Notes { get; set; } = [];
}

public sealed class CalibrationFeedback
{
    // -2 = too slow / weak / light / smooth. +2 = too fast / strong / heavy / noisy.
    public int SelfSteer { get; set; }
    public int FfbStrength { get; set; }
    public int SteeringWeight { get; set; }
    public int DetailNoise { get; set; }
    // 0 = none, 4 = severe.
    public int Oscillation { get; set; }
}

public sealed class CalibrationProfile
{
    public string Key { get; set; } = "";
    public int Samples { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public int TorqueLimitDelta { get; set; }
    public int WheelSpeedDelta { get; set; }
    public int DampingDelta { get; set; }
    public int FrictionDelta { get; set; }
    public int SpeedDampingDelta { get; set; }
    public int InterpolationDelta { get; set; }
    public int AcGainDelta { get; set; }

    public bool IsNeutral => TorqueLimitDelta == 0 && WheelSpeedDelta == 0 && DampingDelta == 0 &&
                             FrictionDelta == 0 && SpeedDampingDelta == 0 && InterpolationDelta == 0 && AcGainDelta == 0;
}

public sealed class SavedTune
{
    public string Schema { get; set; } = "atomic-drift-tuner/v0.5.0";
    public string Name { get; set; } = "";
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public TuneInput Input { get; set; } = new();
    public TuneResult Result { get; set; } = new();
    public CalibrationProfile? Calibration { get; set; }
}

public sealed class AppSettings
{
    public bool FirstRunCompleted { get; set; }
    public string? AssettoCorsaRoot { get; set; }
    public string? AssettoCorsaDocumentsRoot { get; set; }
    public string? SimHubRoot { get; set; }
    public AzomUserPreferences AzomPreferences { get; set; } = new();
    public ThemeSettings Theme { get; set; } = new();
    public AzomLiveConnectionSettings AzomLive { get; set; } = new();
}

public sealed class AssettoCorsaScanResult
{
    public string RootPath { get; set; } = "";
    public List<CarProfile> Cars { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
