namespace AtomicDriftTuner.Models;

public enum AzomCurvePreset
{
    Linear,
    SCurve,
    Exponential,
    Parabolic
}

public sealed class AzomUserPreferences
{
    private string _standbyAfter =
        "Disabled";

    public int ShiftIntensity { get; set; } =
        0;

    public bool VibrateOnNeutral { get; set; } =
        true;

    public int ShiftDebounceMs { get; set; } =
        0;

    public bool HandsOffProtection { get; set; } =
        true;

    public bool RetainGameFfb { get; set; } =
        true;

    public bool ForceFeedbackReversal { get; set; } =
        false;

    public bool StandbyMode { get; set; } =
        false;

    public string StandbyAfter
    {
        get => _standbyAfter;

        set =>
            _standbyAfter =
                string.IsNullOrWhiteSpace(value)
                    ? "Disabled"
                    : value.Trim();
    }

    public bool BaseStatusLed { get; set; } =
        false;

    public bool Bluetooth { get; set; } =
        true;
}

public sealed class AzomCoreSettings
{
    // 60..2700
    public int WheelRotationAngleDeg { get; set; }

    // 0..100
    public int GameFfbStrengthPct { get; set; }

    // 50..100
    public int BaseTorqueOutputPct { get; set; }

    // 0..200
    public int MaximumWheelSpeedPct { get; set; }

    // 0..10
    public int Interpolation { get; set; }
}

public sealed class AzomGearshiftVibrationSettings
{
    // 0..5
    public int ShiftIntensity { get; set; }

    public bool VibrateOnNeutral { get; set; }

    // 0..1000
    public int ShiftDebounceMs { get; set; }
}

public sealed class AzomWheelbaseEffectsSettings
{
    // 0..100
    public int WheelDamperPct { get; set; }

    // 0..100
    public int WheelFrictionPct { get; set; }

    // 100..500
    public int NaturalInertia { get; set; }

    // 0..100
    public int WheelSpringPct { get; set; }
}

public sealed class AzomGameEffectsSettings
{
    // 0..100
    public int GameDamperPct { get; set; }

    // 0..100
    public int GameFrictionPct { get; set; }

    // 0..100
    public int GameInertiaPct { get; set; }

    // 0..100
    public int GameSpringPct { get; set; }
}

public sealed class AzomProtectionSettings
{
    public bool HandsOffProtection { get; set; }

    // 100..4000
    public int SteeringWheelInertia { get; set; }
}

public sealed class AzomSoftLimitSettings
{
    // 1..10
    public int Stiffness { get; set; }

    public bool RetainGameFfb { get; set; }
}

public sealed class AzomFfbEqualizerSettings
{
    // AZOM UI graph observed range: 0..400.
    // 100 = neutral, 400 = maximum boost.

    public int Hz10 { get; set; } =
        100;

    public int Hz15 { get; set; } =
        100;

    public int Hz25 { get; set; } =
        100;

    public int Hz40 { get; set; } =
        100;

    public int Hz60 { get; set; } =
        100;

    public int Hz100 { get; set; } =
        100;

    // 0..10
    public int Sensitivity { get; set; } =
        5;
}

public sealed class AzomFfbOutputCurveSettings
{
    public AzomCurvePreset Preset { get; set; } =
        AzomCurvePreset.Linear;

    // Output 0..100 at input 20.
    public int Node20 { get; set; } =
        20;

    public int Node40 { get; set; } =
        40;

    public int Node60 { get; set; } =
        60;

    public int Node80 { get; set; } =
        80;

    public int Node100 { get; set; } =
        100;
}

public sealed class AzomHighSpeedDampingSettings
{
    // 0..100
    public int DampingLevelPct { get; set; }

    // 0..400
    public int TriggerSpeedKph { get; set; }
}

public sealed class AzomMiscellaneousSettings
{
    private string _standbyAfter =
        "Disabled";

    public bool ForceFeedbackReversal { get; set; }

    public bool StandbyMode { get; set; }

    public string StandbyAfter
    {
        get => _standbyAfter;

        set =>
            _standbyAfter =
                string.IsNullOrWhiteSpace(value)
                    ? "Disabled"
                    : value.Trim();
    }

    public bool BaseStatusLed { get; set; }

    public bool Bluetooth { get; set; }
}

public sealed class AzomSettings
{
    private AzomCoreSettings _core =
        new();

    private AzomGearshiftVibrationSettings _gearshiftVibration =
        new();

    private AzomWheelbaseEffectsSettings _wheelbaseEffects =
        new();

    private AzomGameEffectsSettings _gameEffects =
        new();

    private AzomProtectionSettings _protection =
        new();

    private AzomSoftLimitSettings _softLimit =
        new();

    private AzomFfbEqualizerSettings _ffbEqualizer =
        new();

    private AzomFfbOutputCurveSettings _ffbOutputCurve =
        new();

    private AzomHighSpeedDampingSettings _highSpeedDamping =
        new();

    private AzomMiscellaneousSettings _miscellaneous =
        new();

    public AzomCoreSettings Core
    {
        get => _core;

        set =>
            _core =
                value ??
                new AzomCoreSettings();
    }

    public AzomGearshiftVibrationSettings GearshiftVibration
    {
        get => _gearshiftVibration;

        set =>
            _gearshiftVibration =
                value ??
                new AzomGearshiftVibrationSettings();
    }

    public AzomWheelbaseEffectsSettings WheelbaseEffects
    {
        get => _wheelbaseEffects;

        set =>
            _wheelbaseEffects =
                value ??
                new AzomWheelbaseEffectsSettings();
    }

    public AzomGameEffectsSettings GameEffects
    {
        get => _gameEffects;

        set =>
            _gameEffects =
                value ??
                new AzomGameEffectsSettings();
    }

    public AzomProtectionSettings Protection
    {
        get => _protection;

        set =>
            _protection =
                value ??
                new AzomProtectionSettings();
    }

    public AzomSoftLimitSettings SoftLimit
    {
        get => _softLimit;

        set =>
            _softLimit =
                value ??
                new AzomSoftLimitSettings();
    }

    public AzomFfbEqualizerSettings FfbEqualizer
    {
        get => _ffbEqualizer;

        set =>
            _ffbEqualizer =
                value ??
                new AzomFfbEqualizerSettings();
    }

    public AzomFfbOutputCurveSettings FfbOutputCurve
    {
        get => _ffbOutputCurve;

        set =>
            _ffbOutputCurve =
                value ??
                new AzomFfbOutputCurveSettings();
    }

    public AzomHighSpeedDampingSettings HighSpeedDamping
    {
        get => _highSpeedDamping;

        set =>
            _highSpeedDamping =
                value ??
                new AzomHighSpeedDampingSettings();
    }

    public AzomMiscellaneousSettings Miscellaneous
    {
        get => _miscellaneous;

        set =>
            _miscellaneous =
                value ??
                new AzomMiscellaneousSettings();
    }
}