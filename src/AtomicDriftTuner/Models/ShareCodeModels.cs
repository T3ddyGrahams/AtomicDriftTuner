namespace AtomicDriftTuner.Models;

/// <summary>
/// Portable, privacy-limited tune payload used by Atomic Share Codes.
/// This DTO intentionally excludes local paths, telemetry, calibration,
/// remote credentials, tokens, and preference-style AZOM settings.
/// </summary>
public sealed class AtomicSharePayload
{
    public string Schema { get; set; } = "atomic-share/v1";
    public string AtomicVersion { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public AtomicShareInput Input { get; set; } = new();
    public AtomicShareBehavior Behavior { get; set; } = new();
    public AtomicShareRecommendation Recommendation { get; set; } = new();
}

public sealed class AtomicShareInput
{
    public AtomicShareHardware Hardware { get; set; } = new();
    public AtomicShareWheel Wheel { get; set; } = new();
    public AtomicSharePack Pack { get; set; } = new();
    public AtomicShareCar Car { get; set; } = new();
    public AtomicShareIntent Intent { get; set; } = new();
}

public sealed class AtomicShareHardware
{
    public string Id { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public double PeakTorqueNm { get; set; }
    public int MaxRotationDeg { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class AtomicShareWheel
{
    public string Id { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public double DiameterMm { get; set; }
    public double InertiaFactor { get; set; }
    public bool IsRound { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class AtomicSharePack
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public double GripBias { get; set; }
    public double SelfSteerBias { get; set; }
    public double DampingBias { get; set; }
    public double DetailBias { get; set; }
    public bool IsCustom { get; set; }
}

public sealed class AtomicShareCar
{
    public string Id { get; set; } = "";
    public string PackId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public double MassKg { get; set; }
    public double PowerHp { get; set; }
    public double TorqueNm { get; set; }
    public string Drivetrain { get; set; } = "RWD";
    public double SteeringLockPerSideDeg { get; set; }
    public double CasterDeg { get; set; }
    public double FrontTireWidthMm { get; set; }
    public double RearTireWidthMm { get; set; }
    public GripLevel Grip { get; set; }
    public bool IsCustom { get; set; }

    // Folder NAME is portable and lets another Atomic install match the same
    // locally installed AC car. The machine-specific full folder path is never shared.
    public string? SourceFolderName { get; set; }
}

public sealed class AtomicShareIntent
{
    public DriftStyleKind Kind { get; set; }
    public string Name { get; set; } = "";
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

    public bool IsNeutral =>
        FrontEndBite == 0 && RearGrip == 0 && SelfSteerSpeed == 0 &&
        TransitionSpeed == 0 && AngleStability == 0 &&
        ThrottleSteering == 0 && InitiationSharpness == 0;

    public CarBehaviorTarget ToTarget()
    {
        var target = new CarBehaviorTarget
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
    public AtomicShareAzomRecommendation Azom { get; set; } = new();
    public AssettoCorsaSettings AssettoCorsa { get; set; } = new();
    public double EstimatedPeakWheelTorqueNm { get; set; }
    public int SelfSteerScore { get; set; }
    public int StabilityScore { get; set; }
    public int DetailScore { get; set; }
    public List<string> Notes { get; set; } = [];
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

    // Snapshot-only values for review/Discord display. These are not directly
    // applied when a share code is imported.
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
