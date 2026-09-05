using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

/// <summary>
/// Portable, privacy-limited tune payload used by ADT Share Codes.
/// This DTO intentionally excludes local paths, telemetry, calibration,
/// remote credentials, tokens, and preference-style AZOM settings.
/// </summary>
public sealed class AtomicSharePayload
{
    public const string CurrentSchema =
        "atomic-share/v1";

    private string _schema =
        CurrentSchema;

    private string _atomicVersion =
        string.Empty;

    private AtomicShareInput _input =
        new();

    private AtomicShareBehavior _behavior =
        new();

    private AtomicShareRecommendation _recommendation =
        new();

    public string Schema
    {
        get => _schema;

        set =>
            _schema =
                string.IsNullOrWhiteSpace(value)
                    ? CurrentSchema
                    : value.Trim();
    }

    public string AtomicVersion
    {
        get => _atomicVersion;

        set =>
            _atomicVersion =
                value?.Trim() ??
                string.Empty;
    }

    public DateTime CreatedUtc { get; set; } =
        DateTime.UtcNow;

    public AtomicShareInput Input
    {
        get => _input;

        set =>
            _input =
                value ??
                new AtomicShareInput();
    }

    public AtomicShareBehavior Behavior
    {
        get => _behavior;

        set =>
            _behavior =
                value ??
                new AtomicShareBehavior();
    }

    public AtomicShareRecommendation Recommendation
    {
        get => _recommendation;

        set =>
            _recommendation =
                value ??
                new AtomicShareRecommendation();
    }
}

public sealed class AtomicShareInput
{
    private AtomicShareHardware _hardware =
        new();

    private AtomicShareWheel _wheel =
        new();

    private AtomicSharePack _pack =
        new();

    private AtomicShareCar _car =
        new();

    private AtomicShareIntent _intent =
        new();

    public AtomicShareHardware Hardware
    {
        get => _hardware;

        set =>
            _hardware =
                value ??
                new AtomicShareHardware();
    }

    public AtomicShareWheel Wheel
    {
        get => _wheel;

        set =>
            _wheel =
                value ??
                new AtomicShareWheel();
    }

    public AtomicSharePack Pack
    {
        get => _pack;

        set =>
            _pack =
                value ??
                new AtomicSharePack();
    }

    public AtomicShareCar Car
    {
        get => _car;

        set =>
            _car =
                value ??
                new AtomicShareCar();
    }

    public AtomicShareIntent Intent
    {
        get => _intent;

        set =>
            _intent =
                value ??
                new AtomicShareIntent();
    }
}

public sealed class AtomicShareHardware
{
    private string _id =
        string.Empty;

    private string _manufacturer =
        string.Empty;

    private string _model =
        string.Empty;

    public string Id
    {
        get => _id;

        set =>
            _id =
                value?.Trim() ??
                string.Empty;
    }

    public string Manufacturer
    {
        get => _manufacturer;

        set =>
            _manufacturer =
                value?.Trim() ??
                string.Empty;
    }

    public string Model
    {
        get => _model;

        set =>
            _model =
                value?.Trim() ??
                string.Empty;
    }

    public double PeakTorqueNm { get; set; }

    public int MaxRotationDeg { get; set; }

    public bool IsCustom { get; set; }
}

public sealed class AtomicShareWheel
{
    private string _id =
        string.Empty;

    private string _manufacturer =
        string.Empty;

    private string _model =
        string.Empty;

    public string Id
    {
        get => _id;

        set =>
            _id =
                value?.Trim() ??
                string.Empty;
    }

    public string Manufacturer
    {
        get => _manufacturer;

        set =>
            _manufacturer =
                value?.Trim() ??
                string.Empty;
    }

    public string Model
    {
        get => _model;

        set =>
            _model =
                value?.Trim() ??
                string.Empty;
    }

    public double DiameterMm { get; set; }

    public double InertiaFactor { get; set; }

    public bool IsRound { get; set; }

    public bool IsCustom { get; set; }
}

public sealed class AtomicSharePack
{
    private string _id =
        string.Empty;

    private string _name =
        string.Empty;

    private string _category =
        string.Empty;

    public string Id
    {
        get => _id;

        set =>
            _id =
                value?.Trim() ??
                string.Empty;
    }

    public string Name
    {
        get => _name;

        set =>
            _name =
                value?.Trim() ??
                string.Empty;
    }

    public string Category
    {
        get => _category;

        set =>
            _category =
                value?.Trim() ??
                string.Empty;
    }

    public double GripBias { get; set; }

    public double SelfSteerBias { get; set; }

    public double DampingBias { get; set; }

    public double DetailBias { get; set; }

    public bool IsCustom { get; set; }
}

public sealed class AtomicShareCar
{
    private string _id =
        string.Empty;

    private string _packId =
        string.Empty;

    private string _displayName =
        string.Empty;

    private string _drivetrain =
        "RWD";

    private string? _sourceFolderName;

    public string Id
    {
        get => _id;

        set =>
            _id =
                value?.Trim() ??
                string.Empty;
    }

    public string PackId
    {
        get => _packId;

        set =>
            _packId =
                value?.Trim() ??
                string.Empty;
    }

    public string DisplayName
    {
        get => _displayName;

        set =>
            _displayName =
                value?.Trim() ??
                string.Empty;
    }

    public double MassKg { get; set; }

    public double PowerHp { get; set; }

    public double TorqueNm { get; set; }

    public string Drivetrain
    {
        get => _drivetrain;

        set =>
            _drivetrain =
                string.IsNullOrWhiteSpace(value)
                    ? "RWD"
                    : value.Trim();
    }

    public double SteeringLockPerSideDeg { get; set; }

    public double CasterDeg { get; set; }

    public double FrontTireWidthMm { get; set; }

    public double RearTireWidthMm { get; set; }

    public GripLevel Grip { get; set; }

    public bool IsCustom { get; set; }

    // Only the AC car folder name is portable.
    // ADT never includes the machine-specific full folder path in a share code.
    public string? SourceFolderName
    {
        get => _sourceFolderName;

        set =>
            _sourceFolderName =
                string.IsNullOrWhiteSpace(value)
                    ? null
                    : value.Trim();
    }
}

public sealed class AtomicShareIntent
{
    private string _name =
        string.Empty;

    public DriftStyleKind Kind { get; set; }

    public string Name
    {
        get => _name;

        set =>
            _name =
                value?.Trim() ??
                string.Empty;
    }
}

public sealed class AtomicShareBehavior
{
    public int FrontEndBite { get; set; }

    public int RearGrip { get; set; }

    public int SelfSteerSpeed { get; set; }

    public int TransitionSpeed { get; set; }

    public int AngleStability { get; set; }

    public int ThrottleSteering { get; set; }

    public int InitiationSharpness { get; set; }

    [JsonIgnore]
    public bool IsNeutral =>
        FrontEndBite == 0 &&
        RearGrip == 0 &&
        SelfSteerSpeed == 0 &&
        TransitionSpeed == 0 &&
        AngleStability == 0 &&
        ThrottleSteering == 0 &&
        InitiationSharpness == 0;

    public CarBehaviorTarget ToTarget()
    {
        var target =
            new CarBehaviorTarget
            {
                FrontEndBite = FrontEndBite,
                RearGrip = RearGrip,
                SelfSteerSpeed = SelfSteerSpeed,
                TransitionSpeed = TransitionSpeed,
                AngleStability = AngleStability,
                ThrottleSteering = ThrottleSteering,
                InitiationSharpness = InitiationSharpness
            };

        target.Normalize();

        return target;
    }
}

public sealed class AtomicShareRecommendation
{
    private AtomicShareAzomRecommendation _azom =
        new();

    private AssettoCorsaSettings _assettoCorsa =
        new();

    private List<string> _notes =
        [];

    public AtomicShareAzomRecommendation Azom
    {
        get => _azom;

        set =>
            _azom =
                value ??
                new AtomicShareAzomRecommendation();
    }

    public AssettoCorsaSettings AssettoCorsa
    {
        get => _assettoCorsa;

        set =>
            _assettoCorsa =
                value ??
                new AssettoCorsaSettings();
    }

    public double EstimatedPeakWheelTorqueNm { get; set; }

    public int SelfSteerScore { get; set; }

    public int StabilityScore { get; set; }

    public int DetailScore { get; set; }

    public List<string> Notes
    {
        get => _notes;

        set =>
            _notes =
                value ??
                [];
    }
}

public sealed class AtomicShareAzomRecommendation
{
    public int WheelRotationAngleDeg { get; set; }

    public int GameFfbStrengthPct { get; set; }

    public int BaseTorqueOutputPct { get; set; }

    public int MaximumWheelSpeedPct { get; set; }

    public int Interpolation { get; set; }

    public int WheelDamperPct { get; set; }

    public int WheelFrictionPct { get; set; }

    public int NaturalInertia { get; set; }

    public int HighSpeedDampingPct { get; set; }

    public int HighSpeedTriggerKph { get; set; }

    // Snapshot-only values for review and Discord display.
    // These values are not directly applied when a share code is imported.
    public int EqHz10 { get; set; }

    public int EqHz15 { get; set; }

    public int EqHz25 { get; set; }

    public int EqHz40 { get; set; }

    public int EqHz60 { get; set; }

    public int EqHz100 { get; set; }

    public int EqSensitivity { get; set; }

    public AzomCurvePreset OutputCurvePreset { get; set; }

    public int CurveNode20 { get; set; }

    public int CurveNode40 { get; set; }

    public int CurveNode60 { get; set; }

    public int CurveNode80 { get; set; }

    public int CurveNode100 { get; set; }
}