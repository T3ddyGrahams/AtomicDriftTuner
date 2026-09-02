using System.Globalization;

namespace AtomicDriftTuner.Models;

public enum CarSetupCategory
{
    Tires,
    Alignment,
    Suspension,
    Dampers,
    Differential,
    Brakes,
    Gearing,
    Aero,
    Fuel,
    Electronics,
    Other
}

public enum SetupAggressiveness
{
    Conservative,
    Balanced,
    Aggressive
}


public sealed class BehaviorBlendNotice
{
    public string Parameter { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
}

public sealed class BehaviorBlendPreview
{
    public int ActiveBiasCount { get; set; }
    public int PotentialConflictGroups { get; set; }
    public int PotentialAlignedGroups { get; set; }
    public List<string> Details { get; set; } = [];

    public string Summary => ActiveBiasCount == 0
        ? "Neutral behavior target — no behavior blending is required."
        : PotentialConflictGroups == 0
            ? $"{ActiveBiasCount} active goal(s). No opposing behavior interactions are expected; {PotentialAlignedGroups} aligned interaction group(s) will be softly damped to avoid over-stacking."
            : $"{ActiveBiasCount} active goal(s). {PotentialConflictGroups} potential compromise group(s) detected; Atomic will reduce overlapping influence instead of blindly stacking the requests.";
}

public sealed class BehaviorBlendReport
{
    public int ActiveBiasCount { get; set; }
    public int ParametersAffected { get; set; }
    public int BehaviorConflictCount { get; set; }
    public int IntentConflictCount { get; set; }
    public int AlignedStackCount { get; set; }
    public List<BehaviorBlendNotice> Notices { get; set; } = [];

    public int ConflictCount => BehaviorConflictCount + IntentConflictCount;

    public string Summary
    {
        get
        {
            if (ActiveBiasCount == 0)
                return "Neutral behavior target — no behavior blend adjustments were applied.";

            if (ConflictCount == 0)
            {
                return $"{ActiveBiasCount} active behavior goal(s) influenced {ParametersAffected} recognized parameter(s). " +
                       $"{AlignedStackCount} aligned stack(s) were softly damped to avoid overcorrection.";
            }

            return $"{ActiveBiasCount} active behavior goal(s) influenced {ParametersAffected} recognized parameter(s). " +
                   $"Atomic resolved {BehaviorConflictCount} behavior-vs-behavior compromise(s) and " +
                   $"{IntentConflictCount} session-intent compromise(s); {AlignedStackCount} aligned stack(s) were also damped.";
        }
    }
}


public sealed class CarBehaviorTarget
{
    // -2..+2. Zero is neutral. These are handling goals, not direct AC values.
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    // Calm / progressive  < 0 >  sharp / aggressive front response
    public int FrontEndBite { get; set; }
    // Loose rear  < 0 >  planted / traction-oriented rear
    public int RearGrip { get; set; }
    // Slower self-steer  < 0 >  faster self-steer
    public int SelfSteerSpeed { get; set; }
    // Smooth / slow transition  < 0 >  quick transition
    public int TransitionSpeed { get; set; }
    // Responsive / lively at angle  < 0 >  stable / forgiving at angle
    public int AngleStability { get; set; }
    // Less rotation on throttle  < 0 >  more rotation on throttle
    public int ThrottleSteering { get; set; }
    // Smooth initiation  < 0 >  sharp initiation
    public int InitiationSharpness { get; set; }

    public bool IsNeutral =>
        FrontEndBite == 0 && RearGrip == 0 && SelfSteerSpeed == 0 &&
        TransitionSpeed == 0 && AngleStability == 0 &&
        ThrottleSteering == 0 && InitiationSharpness == 0;

    public int ActiveBiasCount => new[]
    {
        FrontEndBite, RearGrip, SelfSteerSpeed, TransitionSpeed,
        AngleStability, ThrottleSteering, InitiationSharpness
    }.Count(x => x != 0);

    public void Normalize()
    {
        FrontEndBite = Math.Clamp(FrontEndBite, -2, 2);
        RearGrip = Math.Clamp(RearGrip, -2, 2);
        SelfSteerSpeed = Math.Clamp(SelfSteerSpeed, -2, 2);
        TransitionSpeed = Math.Clamp(TransitionSpeed, -2, 2);
        AngleStability = Math.Clamp(AngleStability, -2, 2);
        ThrottleSteering = Math.Clamp(ThrottleSteering, -2, 2);
        InitiationSharpness = Math.Clamp(InitiationSharpness, -2, 2);
    }
}

public sealed class SetupRangeDefinition
{
    public string Section { get; set; } = "";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public string? Name { get; set; }
    public string? Units { get; set; }
    public bool ShowClicks { get; set; }
    public string Source { get; set; } = "None";
}

public sealed class CarSetupParameter
{
    public string Section { get; set; } = "";
    public CarSetupCategory Category { get; set; }
    public string CurrentRaw { get; set; } = "";
    public double? CurrentValue { get; set; }
    public double? RecommendedValue { get; set; }
    public string RecommendedRaw => RecommendedValue is null ? CurrentRaw : Format(RecommendedValue.Value, CurrentRaw);
    public double Delta => CurrentValue is null || RecommendedValue is null ? 0 : RecommendedValue.Value - CurrentValue.Value;
    public string DeltaText => Delta == 0 ? "—" : Delta > 0 ? $"+{Delta:0.###}" : Delta.ToString("0.###", CultureInfo.InvariantCulture);
    public SetupRangeDefinition? Range { get; set; }
    public string Reason { get; set; } = "No automatic change.";
    public string BlendStatus { get; set; } = "—";
    public bool Changed => CurrentValue is not null && RecommendedValue is not null && Math.Abs(RecommendedValue.Value - CurrentValue.Value) > 0.000001;
    public string RangeText => Range switch
    {
        null => "Unknown",
        { ShowClicks: true } => "setup.ini / clicks",
        _ when Range.Min is null && Range.Max is null => "setup.ini",
        _ => $"{Range.Min?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?"} .. {Range.Max?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?"}"
    };

    private static string Format(double value, string baseline)
    {
        if (!baseline.Contains('.') && Math.Abs(value - Math.Round(value)) < 0.000001)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

public sealed class CarSetupAnalysis
{
    public string BaselinePath { get; set; } = "";
    public string CarFolderName { get; set; } = "";
    public string? SetupDefinitionPath { get; set; }
    public bool HasSetupDefinition => !string.IsNullOrWhiteSpace(SetupDefinitionPath);
    public List<CarSetupParameter> Parameters { get; set; } = [];
    public BehaviorBlendReport BehaviorBlend { get; set; } = new();
    public int ChangedCount => Parameters.Count(x => x.Changed);
    public string RangeSummary => HasSetupDefinition
        ? $"Legal ranges loaded from {Path.GetFileName(SetupDefinitionPath)}"
        : "No unpacked data/setup.ini found; recommendations use conservative baseline-relative changes.";
}
