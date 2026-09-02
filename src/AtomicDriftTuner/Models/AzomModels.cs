namespace AtomicDriftTuner.Models;

public enum AzomCurvePreset { Linear, SCurve, Exponential, Parabolic }

public sealed class AzomUserPreferences
{
    public int ShiftIntensity { get; set; } = 0;
    public bool VibrateOnNeutral { get; set; } = true;
    public int ShiftDebounceMs { get; set; } = 0;
    public bool HandsOffProtection { get; set; } = true;
    public bool RetainGameFfb { get; set; } = true;
    public bool ForceFeedbackReversal { get; set; } = false;
    public bool StandbyMode { get; set; } = false;
    public string StandbyAfter { get; set; } = "Disabled";
    public bool BaseStatusLed { get; set; } = false;
    public bool Bluetooth { get; set; } = true;
}

public sealed class AzomCoreSettings
{
    public int WheelRotationAngleDeg { get; set; } // 60..2700
    public int GameFfbStrengthPct { get; set; }    // 0..100
    public int BaseTorqueOutputPct { get; set; }   // 50..100
    public int MaximumWheelSpeedPct { get; set; }  // 0..200
    public int Interpolation { get; set; }          // 0..10
}

public sealed class AzomGearshiftVibrationSettings
{
    public int ShiftIntensity { get; set; }         // 0..5
    public bool VibrateOnNeutral { get; set; }
    public int ShiftDebounceMs { get; set; }        // 0..1000
}

public sealed class AzomWheelbaseEffectsSettings
{
    public int WheelDamperPct { get; set; }         // 0..100
    public int WheelFrictionPct { get; set; }       // 0..100
    public int NaturalInertia { get; set; }         // 100..500
    public int WheelSpringPct { get; set; }         // 0..100
}

public sealed class AzomGameEffectsSettings
{
    public int GameDamperPct { get; set; }          // 0..100
    public int GameFrictionPct { get; set; }        // 0..100
    public int GameInertiaPct { get; set; }         // 0..100
    public int GameSpringPct { get; set; }          // 0..100
}

public sealed class AzomProtectionSettings
{
    public bool HandsOffProtection { get; set; }
    public int SteeringWheelInertia { get; set; }   // 100..4000
}

public sealed class AzomSoftLimitSettings
{
    public int Stiffness { get; set; }              // 1..10
    public bool RetainGameFfb { get; set; }
}

public sealed class AzomFfbEqualizerSettings
{
    // AZOM UI graph observed range: 0..400; 100 = neutral, 400 = max boost.
    public int Hz10 { get; set; } = 100;
    public int Hz15 { get; set; } = 100;
    public int Hz25 { get; set; } = 100;
    public int Hz40 { get; set; } = 100;
    public int Hz60 { get; set; } = 100;
    public int Hz100 { get; set; } = 100;
    public int Sensitivity { get; set; } = 5;        // 0..10
}

public sealed class AzomFfbOutputCurveSettings
{
    public AzomCurvePreset Preset { get; set; } = AzomCurvePreset.Linear;
    public int Node20 { get; set; } = 20;            // output 0..100 at input 20
    public int Node40 { get; set; } = 40;
    public int Node60 { get; set; } = 60;
    public int Node80 { get; set; } = 80;
    public int Node100 { get; set; } = 100;
}

public sealed class AzomHighSpeedDampingSettings
{
    public int DampingLevelPct { get; set; }         // 0..100
    public int TriggerSpeedKph { get; set; }         // 0..400
}

public sealed class AzomMiscellaneousSettings
{
    public bool ForceFeedbackReversal { get; set; }
    public bool StandbyMode { get; set; }
    public string StandbyAfter { get; set; } = "Disabled";
    public bool BaseStatusLed { get; set; }
    public bool Bluetooth { get; set; }
}

public sealed class AzomSettings
{
    public AzomCoreSettings Core { get; set; } = new();
    public AzomGearshiftVibrationSettings GearshiftVibration { get; set; } = new();
    public AzomWheelbaseEffectsSettings WheelbaseEffects { get; set; } = new();
    public AzomGameEffectsSettings GameEffects { get; set; } = new();
    public AzomProtectionSettings Protection { get; set; } = new();
    public AzomSoftLimitSettings SoftLimit { get; set; } = new();
    public AzomFfbEqualizerSettings FfbEqualizer { get; set; } = new();
    public AzomFfbOutputCurveSettings FfbOutputCurve { get; set; } = new();
    public AzomHighSpeedDampingSettings HighSpeedDamping { get; set; } = new();
    public AzomMiscellaneousSettings Miscellaneous { get; set; } = new();
}
