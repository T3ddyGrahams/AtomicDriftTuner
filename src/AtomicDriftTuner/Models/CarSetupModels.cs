using System.Globalization;
using System.Text.Json.Serialization;

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
    public string Parameter { get; set; } =
        string.Empty;

    public string Kind { get; set; } =
        string.Empty;

    public string Summary { get; set; } =
        string.Empty;
}

public sealed class BehaviorBlendPreview
{
    private List<string> _details =
        [];

    public int ActiveBiasCount { get; set; }

    public int PotentialConflictGroups { get; set; }

    public int PotentialAlignedGroups { get; set; }

    public List<string> Details
    {
        get => _details;

        set =>
            _details =
                value ??
                [];
    }

    [JsonIgnore]
    public string Summary =>
        ActiveBiasCount == 0
            ? "Neutral behavior target — no behavior blending is required."
            : PotentialConflictGroups == 0
                ? $"{ActiveBiasCount} active goal(s). No opposing behavior interactions are expected; " +
                  $"{PotentialAlignedGroups} aligned interaction group(s) will be softly damped to avoid over-stacking."
                : $"{ActiveBiasCount} active goal(s). {PotentialConflictGroups} potential compromise group(s) detected; " +
                  "ADT will reduce overlapping influence instead of blindly stacking the requests.";
}

public sealed class BehaviorBlendReport
{
    private List<BehaviorBlendNotice> _notices =
        [];

    public int ActiveBiasCount { get; set; }

    public int ParametersAffected { get; set; }

    public int BehaviorConflictCount { get; set; }

    public int IntentConflictCount { get; set; }

    public int AlignedStackCount { get; set; }

    public List<BehaviorBlendNotice> Notices
    {
        get => _notices;

        set =>
            _notices =
                value ??
                [];
    }

    [JsonIgnore]
    public int ConflictCount =>
        Math.Max(
            0,
            BehaviorConflictCount) +
        Math.Max(
            0,
            IntentConflictCount);

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (ActiveBiasCount <= 0)
            {
                return "Neutral behavior target — no behavior blend adjustments were applied.";
            }

            if (ConflictCount == 0)
            {
                return
                    $"{ActiveBiasCount} active behavior goal(s) influenced " +
                    $"{Math.Max(0, ParametersAffected)} recognized parameter(s). " +
                    $"{Math.Max(0, AlignedStackCount)} aligned stack(s) were softly damped to avoid overcorrection.";
            }

            return
                $"{ActiveBiasCount} active behavior goal(s) influenced " +
                $"{Math.Max(0, ParametersAffected)} recognized parameter(s). " +
                $"ADT resolved {Math.Max(0, BehaviorConflictCount)} behavior-vs-behavior compromise(s) and " +
                $"{Math.Max(0, IntentConflictCount)} session-intent compromise(s); " +
                $"{Math.Max(0, AlignedStackCount)} aligned stack(s) were also damped.";
        }
    }
}

public sealed class CarBehaviorTarget
{
    private string _key =
        string.Empty;

    private string _displayName =
        string.Empty;

    // -2..+2. Zero is neutral. These are handling goals, not direct AC values.

    public string Key
    {
        get => _key;

        set =>
            _key =
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

    public DateTime UpdatedUtc { get; set; } =
        DateTime.UtcNow;

    // Calm / progressive < 0 > sharp / aggressive front response
    public int FrontEndBite { get; set; }

    // Loose rear < 0 > planted / traction-oriented rear
    public int RearGrip { get; set; }

    // Slower self-steer < 0 > faster self-steer
    public int SelfSteerSpeed { get; set; }

    // Smooth / slow transition < 0 > quick transition
    public int TransitionSpeed { get; set; }

    // Responsive / lively at angle < 0 > stable / forgiving at angle
    public int AngleStability { get; set; }

    // Less rotation on throttle < 0 > more rotation on throttle
    public int ThrottleSteering { get; set; }

    // Smooth initiation < 0 > sharp initiation
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

    [JsonIgnore]
    public int ActiveBiasCount =>
        new[]
        {
            FrontEndBite,
            RearGrip,
            SelfSteerSpeed,
            TransitionSpeed,
            AngleStability,
            ThrottleSteering,
            InitiationSharpness
        }.Count(
            value =>
                value != 0);

    public void Normalize()
    {
        FrontEndBite =
            Math.Clamp(
                FrontEndBite,
                -2,
                2);

        RearGrip =
            Math.Clamp(
                RearGrip,
                -2,
                2);

        SelfSteerSpeed =
            Math.Clamp(
                SelfSteerSpeed,
                -2,
                2);

        TransitionSpeed =
            Math.Clamp(
                TransitionSpeed,
                -2,
                2);

        AngleStability =
            Math.Clamp(
                AngleStability,
                -2,
                2);

        ThrottleSteering =
            Math.Clamp(
                ThrottleSteering,
                -2,
                2);

        InitiationSharpness =
            Math.Clamp(
                InitiationSharpness,
                -2,
                2);
    }
}

public sealed class SetupRangeDefinition
{
    public string Section { get; set; } =
        string.Empty;

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Step { get; set; }

    public string? Name { get; set; }

    public string? Units { get; set; }

    public bool ShowClicks { get; set; }

    public string Source { get; set; } =
        "None";
}

public sealed class CarSetupParameter
{
    public string Section { get; set; } =
        string.Empty;

    public CarSetupCategory Category { get; set; }

    public string CurrentRaw { get; set; } =
        string.Empty;

    public double? CurrentValue { get; set; }

    public double? RecommendedValue { get; set; }

    public SetupRangeDefinition? Range { get; set; }

    public string Reason { get; set; } =
        "No automatic change.";

    public string BlendStatus { get; set; } =
        "—";

    [JsonIgnore]
    public string RecommendedRaw
    {
        get
        {
            if (
                RecommendedValue is null ||
                !double.IsFinite(
                    RecommendedValue.Value))
            {
                return CurrentRaw;
            }

            return Format(
                RecommendedValue.Value,
                CurrentRaw);
        }
    }

    [JsonIgnore]
    public double Delta
    {
        get
        {
            if (
                CurrentValue is null ||
                RecommendedValue is null ||
                !double.IsFinite(
                    CurrentValue.Value) ||
                !double.IsFinite(
                    RecommendedValue.Value))
            {
                return 0;
            }

            var delta =
                RecommendedValue.Value -
                CurrentValue.Value;

            return double.IsFinite(
                    delta)
                ? delta
                : 0;
        }
    }

    [JsonIgnore]
    public string DeltaText
    {
        get
        {
            var delta =
                Delta;

            if (Math.Abs(
                    delta) <=
                0.000001)
            {
                return "—";
            }

            return delta > 0
                ? $"+{delta.ToString("0.###", CultureInfo.InvariantCulture)}"
                : delta.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);
        }
    }

    [JsonIgnore]
    public bool Changed =>
        CurrentValue is not null &&
        RecommendedValue is not null &&
        double.IsFinite(
            CurrentValue.Value) &&
        double.IsFinite(
            RecommendedValue.Value) &&
        Math.Abs(
            RecommendedValue.Value -
            CurrentValue.Value) >
        0.000001;

    [JsonIgnore]
    public string RangeText
    {
        get
        {
            if (Range is null)
            {
                return "Unknown";
            }

            if (Range.ShowClicks)
            {
                return "setup.ini / clicks";
            }

            var minimum =
                NormalizeFinite(
                    Range.Min);

            var maximum =
                NormalizeFinite(
                    Range.Max);

            if (
                minimum is null &&
                maximum is null)
            {
                return "setup.ini";
            }

            return
                $"{FormatNullable(minimum)} .. {FormatNullable(maximum)}";
        }
    }

    private static string Format(
        double value,
        string? baseline)
    {
        if (!double.IsFinite(
                value))
        {
            return baseline ??
                   string.Empty;
        }

        var baselineText =
            baseline ??
            string.Empty;

        if (
            !baselineText.Contains(
                '.') &&
            Math.Abs(
                value -
                Math.Round(
                    value)) <
            0.000001)
        {
            return Math.Round(
                    value,
                    MidpointRounding.AwayFromZero)
                .ToString(
                    CultureInfo.InvariantCulture);
        }

        return value.ToString(
            "0.####",
            CultureInfo.InvariantCulture);
    }

    private static double? NormalizeFinite(
        double? value)
    {
        return
            value is not null &&
            double.IsFinite(
                value.Value)
                ? value
                : null;
    }

    private static string FormatNullable(
        double? value)
    {
        return value?.ToString(
                   "0.###",
                   CultureInfo.InvariantCulture) ??
               "?";
    }
}

public sealed class CarSetupAnalysis
{
    private List<CarSetupParameter> _parameters =
        [];

    private BehaviorBlendReport _behaviorBlend =
        new();

    public string BaselinePath { get; set; } =
        string.Empty;

    public string CarFolderName { get; set; } =
        string.Empty;

    public string? SetupDefinitionPath { get; set; }

    public List<CarSetupParameter> Parameters
    {
        get => _parameters;

        set =>
            _parameters =
                value ??
                [];
    }

    public BehaviorBlendReport BehaviorBlend
    {
        get => _behaviorBlend;

        set =>
            _behaviorBlend =
                value ??
                new BehaviorBlendReport();
    }

    [JsonIgnore]
    public bool HasSetupDefinition =>
        !string.IsNullOrWhiteSpace(
            SetupDefinitionPath);

    [JsonIgnore]
    public int ChangedCount =>
        Parameters.Count(
            parameter =>
                parameter is not null &&
                parameter.Changed);

    [JsonIgnore]
    public string RangeSummary
    {
        get
        {
            if (!HasSetupDefinition)
            {
                return
                    "No unpacked data/setup.ini found; recommendations use conservative baseline-relative changes.";
            }

            string fileName;

            try
            {
                fileName =
                    Path.GetFileName(
                        SetupDefinitionPath);
            }
            catch
            {
                fileName =
                    "setup.ini";
            }

            if (string.IsNullOrWhiteSpace(
                    fileName))
            {
                fileName =
                    "setup.ini";
            }

            return
                $"Legal ranges loaded from {fileName}";
        }
    }
}