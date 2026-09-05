using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

public enum GripLevel
{
    Low,
    Medium,
    High
}

public enum DriftStyleKind
{
    Training,
    Realistic,
    FastSelfSteer,
    Tandem,
    Competition
}

public enum DataConfidence
{
    Unknown,
    Low,
    Medium,
    High
}

public sealed class HardwareProfile
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString("N");

    public string Manufacturer { get; set; } =
        "MOZA";

    public string Model { get; set; } =
        "Custom Direct Drive Base";

    public double PeakTorqueNm { get; set; } =
        9;

    public int MaxRotationDeg { get; set; } =
        2700;

    public bool IsCustom { get; set; }

    public override string ToString()
    {
        var manufacturer =
            string.IsNullOrWhiteSpace(Manufacturer)
                ? "Unknown"
                : Manufacturer.Trim();

        var model =
            string.IsNullOrWhiteSpace(Model)
                ? "Wheelbase"
                : Model.Trim();

        return $"{manufacturer} {model}";
    }
}

public sealed class SteeringWheelProfile
{
    public string Id { get; set; } =
        Guid.NewGuid().ToString("N");

    public string Manufacturer { get; set; } =
        "MOZA";

    public string Model { get; set; } =
        "Custom / Other";

    public double DiameterMm { get; set; } =
        330;

    public double InertiaFactor { get; set; } =
        1.0;

    public bool IsRound { get; set; } =
        true;

    public bool IsCustom { get; set; }

    public override string ToString()
    {
        var manufacturer =
            string.IsNullOrWhiteSpace(Manufacturer)
                ? "Unknown"
                : Manufacturer.Trim();

        var model =
            string.IsNullOrWhiteSpace(Model)
                ? "Steering Wheel"
                : Model.Trim();

        return $"{manufacturer} {model}";
    }
}

public sealed class DriftPackProfile
{
    public string Id { get; set; } =
        "custom-pack";

    public string Name { get; set; } =
        "Custom / Other Pack";

    public string Category { get; set; } =
        "Custom";

    public double GripBias { get; set; }

    public double SelfSteerBias { get; set; }

    public double DampingBias { get; set; }

    public double DetailBias { get; set; }

    public bool IsCustom { get; set; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name)
            ? "Unnamed Drift Pack"
            : Name.Trim();
    }
}

public sealed class CarDataConfidence
{
    public DataConfidence Mass { get; set; } =
        DataConfidence.Low;

    public DataConfidence Power { get; set; } =
        DataConfidence.Low;

    public DataConfidence Caster { get; set; } =
        DataConfidence.Low;

    public DataConfidence SteeringLock { get; set; } =
        DataConfidence.Low;

    public DataConfidence FrontTireWidth { get; set; } =
        DataConfidence.Low;

    public DataConfidence Grip { get; set; } =
        DataConfidence.Low;

    [JsonIgnore]
    public int Score
    {
        get
        {
            var values =
                new[]
                {
                    Mass,
                    Power,
                    Caster,
                    SteeringLock,
                    FrontTireWidth,
                    Grip
                };

            return (int)Math.Round(
                values.Average(
                    ConfidenceScore),
                MidpointRounding.AwayFromZero);
        }
    }

    private static int ConfidenceScore(
        DataConfidence confidence)
    {
        return confidence switch
        {
            DataConfidence.High => 100,
            DataConfidence.Medium => 65,
            DataConfidence.Low => 30,
            _ => 0
        };
    }
}

public sealed class CarProfile
{
    private CarDataConfidence _confidence =
        new();

    public string Id { get; set; } =
        Guid.NewGuid().ToString("N");

    public string PackId { get; set; } =
        "custom-pack";

    public string DisplayName { get; set; } =
        "Custom Assetto Corsa Car";

    public double MassKg { get; set; } =
        1300;

    public double PowerHp { get; set; } =
        400;

    public double TorqueNm { get; set; } =
        450;

    public string Drivetrain { get; set; } =
        "RWD";

    public double SteeringLockPerSideDeg { get; set; } =
        60;

    public double CasterDeg { get; set; } =
        7;

    public double FrontTireWidthMm { get; set; } =
        265;

    public double RearTireWidthMm { get; set; } =
        265;

    public GripLevel Grip { get; set; } =
        GripLevel.Medium;

    public bool IsCustom { get; set; }

    public bool IsInstalled { get; set; }

    public string? SourceFolderName { get; set; }

    public string? SourceFolderPath { get; set; }

    public string? Author { get; set; }

    public string? DataSourceSummary { get; set; }

    public CarDataConfidence Confidence
    {
        get => _confidence;

        set =>
            _confidence =
                value ??
                new CarDataConfidence();
    }

    public override string ToString()
    {
        var name =
            string.IsNullOrWhiteSpace(DisplayName)
                ? "Unnamed Assetto Corsa Car"
                : DisplayName.Trim();

        return IsInstalled
            ? $"{name}  [Installed]"
            : name;
    }
}

public sealed class DriftIntent
{
    public DriftStyleKind Kind { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public double SelfSteer { get; set; }

    public double Stability { get; set; }

    public double Detail { get; set; }

    public double Weight { get; set; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Name)
            ? Kind.ToString()
            : Name.Trim();
    }
}

public sealed class TuneInput
{
    private HardwareProfile _hardware =
        new();

    private SteeringWheelProfile _wheel =
        new();

    private DriftPackProfile _driftPack =
        new();

    private CarProfile _car =
        new();

    private DriftIntent _intent =
        new();

    public HardwareProfile Hardware
    {
        get => _hardware;

        set =>
            _hardware =
                value ??
                new HardwareProfile();
    }

    public SteeringWheelProfile Wheel
    {
        get => _wheel;

        set =>
            _wheel =
                value ??
                new SteeringWheelProfile();
    }

    public DriftPackProfile DriftPack
    {
        get => _driftPack;

        set =>
            _driftPack =
                value ??
                new DriftPackProfile();
    }

    public CarProfile Car
    {
        get => _car;

        set =>
            _car =
                value ??
                new CarProfile();
    }

    public DriftIntent Intent
    {
        get => _intent;

        set =>
            _intent =
                value ??
                new DriftIntent();
    }
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
    private AzomSettings _azom =
        new();

    private AssettoCorsaSettings _ac =
        new();

    private List<string> _notes =
        [];

    public AzomSettings Azom
    {
        get => _azom;

        set =>
            _azom =
                value ??
                new AzomSettings();
    }

    public AssettoCorsaSettings Ac
    {
        get => _ac;

        set =>
            _ac =
                value ??
                new AssettoCorsaSettings();
    }

    public double EstimatedPeakWheelTorqueNm { get; set; }

    public int SelfSteerScore { get; set; }

    public int StabilityScore { get; set; }

    public int DetailScore { get; set; }

    public string CalibrationSummary { get; set; } =
        "No saved calibration";

    public List<string> Notes
    {
        get => _notes;

        set =>
            _notes =
                value ??
                [];
    }
}

public sealed class CalibrationFeedback
{
    // -2 = too slow / weak / light / smooth.
    // +2 = too fast / strong / heavy / noisy.
    public int SelfSteer { get; set; }

    public int FfbStrength { get; set; }

    public int SteeringWeight { get; set; }

    public int DetailNoise { get; set; }

    // 0 = none, 4 = severe.
    public int Oscillation { get; set; }
}

public sealed class CalibrationProfile
{
    public string Key { get; set; } =
        string.Empty;

    public int Samples { get; set; }

    public DateTime UpdatedUtc { get; set; } =
        DateTime.UtcNow;

    public int TorqueLimitDelta { get; set; }

    public int WheelSpeedDelta { get; set; }

    public int DampingDelta { get; set; }

    public int FrictionDelta { get; set; }

    public int SpeedDampingDelta { get; set; }

    public int InterpolationDelta { get; set; }

    public int AcGainDelta { get; set; }

    [JsonIgnore]
    public bool IsNeutral =>
        TorqueLimitDelta == 0 &&
        WheelSpeedDelta == 0 &&
        DampingDelta == 0 &&
        FrictionDelta == 0 &&
        SpeedDampingDelta == 0 &&
        InterpolationDelta == 0 &&
        AcGainDelta == 0;
}

public sealed class SavedTune
{
    public const string CurrentSchema =
        "atomic-drift-tuner/v0.5.0";

    private string _schema =
        CurrentSchema;

    private TuneInput _input =
        new();

    private TuneResult _result =
        new();

    public string Schema
    {
        get => _schema;

        set =>
            _schema =
                string.IsNullOrWhiteSpace(value)
                    ? CurrentSchema
                    : value;
    }

    public string Name { get; set; } =
        string.Empty;

    public DateTime SavedUtc { get; set; } =
        DateTime.UtcNow;

    public TuneInput Input
    {
        get => _input;

        set =>
            _input =
                value ??
                new TuneInput();
    }

    public TuneResult Result
    {
        get => _result;

        set =>
            _result =
                value ??
                new TuneResult();
    }

    public CalibrationProfile? Calibration { get; set; }
}

public sealed class AppSettings
{
    private AzomUserPreferences _azomPreferences =
        new();

    private ThemeSettings _theme =
        new();

    private AzomLiveConnectionSettings _azomLive =
        new();

    public bool FirstRunCompleted { get; set; }

    public string? AssettoCorsaRoot { get; set; }

    public string? AssettoCorsaDocumentsRoot { get; set; }

    public string? SimHubRoot { get; set; }

    public bool AutoScanInstalledCars { get; set; } =
        true;

    public bool AutoSelectActiveCar { get; set; } =
        true;

    public AzomUserPreferences AzomPreferences
    {
        get => _azomPreferences;

        set =>
            _azomPreferences =
                value ??
                new AzomUserPreferences();
    }

    public ThemeSettings Theme
    {
        get => _theme;

        set =>
            _theme =
                value ??
                new ThemeSettings();
    }

    public AzomLiveConnectionSettings AzomLive
    {
        get => _azomLive;

        set =>
            _azomLive =
                value ??
                new AzomLiveConnectionSettings();
    }
}

public sealed class AssettoCorsaScanResult
{
    private List<CarProfile> _cars =
        [];

    private List<DriftPackProfile> _discoveredPacks =
        [];

    private List<string> _warnings =
        [];

    public string RootPath { get; set; } =
        string.Empty;

    public List<CarProfile> Cars
    {
        get => _cars;

        set =>
            _cars =
                value ??
                [];
    }

    public List<DriftPackProfile> DiscoveredPacks
    {
        get => _discoveredPacks;

        set =>
            _discoveredPacks =
                value ??
                [];
    }

    public List<string> Warnings
    {
        get => _warnings;

        set =>
            _warnings =
                value ??
                [];
    }
}