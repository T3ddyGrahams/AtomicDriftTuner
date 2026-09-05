using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

public enum AssistantConfidence
{
    Low,
    Medium,
    High
}

public enum AssistantFindingStatus
{
    OnTarget,
    NearTarget,
    NeedsWork,
    InsufficientData,
    TargetOnly
}

public sealed class SavedTelemetrySession
{
    private string _jsonPath =
        string.Empty;

    private TelemetrySession _session =
        new();

    private TelemetryAnalysis _analysis =
        new();

    public string JsonPath
    {
        get => _jsonPath;

        set =>
            _jsonPath =
                value?.Trim() ??
                string.Empty;
    }

    public TelemetrySession Session
    {
        get => _session;

        set =>
            _session =
                value ??
                new TelemetrySession();
    }

    public TelemetryAnalysis Analysis
    {
        get => _analysis;

        set =>
            _analysis =
                value ??
                new TelemetryAnalysis();
    }

    [JsonIgnore]
    public DateTime SessionUtc
    {
        get
        {
            if (Session.StartedUtc != default)
            {
                return Session.StartedUtc;
            }

            return TryGetFileTimestampUtc(
                JsonPath);
        }
    }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var timestamp =
                FormatSessionTime(
                    SessionUtc);

            var carName =
                string.IsNullOrWhiteSpace(
                    Session.CarName)
                    ? "Unknown car"
                    : Session.CarName.Trim();

            return
                $"{timestamp} • " +
                $"{carName} • " +
                $"{Math.Max(0, Analysis.DriftTimeSeconds):0}s drift • " +
                $"{Math.Max(0, Analysis.TransitionCount)} transitions";
        }
    }

    public override string ToString()
    {
        return DisplayName;
    }

    private static DateTime TryGetFileTimestampUtc(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return default;
        }

        try
        {
            var fullPath =
                Path.GetFullPath(
                    path);

            if (!File.Exists(
                    fullPath))
            {
                return default;
            }

            return File.GetLastWriteTimeUtc(
                fullPath);
        }
        catch (Exception ex)
            when (
                ex is ArgumentException or
                NotSupportedException or
                PathTooLongException or
                IOException or
                UnauthorizedAccessException)
        {
            return default;
        }
    }

    private static string FormatSessionTime(
        DateTime timestampUtc)
    {
        if (timestampUtc == default)
        {
            return "Unknown time";
        }

        try
        {
            return timestampUtc
                .ToLocalTime()
                .ToString("g");
        }
        catch (ArgumentException)
        {
            return "Unknown time";
        }
    }
}

public sealed class AssistantBehaviorAssessment
{
    private string _behavior =
        string.Empty;

    private string _desired =
        string.Empty;

    private string _observed =
        string.Empty;

    private string _status =
        string.Empty;

    private string _confidence =
        string.Empty;

    private string _evidence =
        string.Empty;

    public string Behavior
    {
        get => _behavior;

        set =>
            _behavior =
                NormalizeText(
                    value);
    }

    public string Desired
    {
        get => _desired;

        set =>
            _desired =
                NormalizeText(
                    value);
    }

    public string Observed
    {
        get => _observed;

        set =>
            _observed =
                NormalizeText(
                    value);
    }

    public string Status
    {
        get => _status;

        set =>
            _status =
                NormalizeText(
                    value);
    }

    public string Confidence
    {
        get => _confidence;

        set =>
            _confidence =
                NormalizeText(
                    value);
    }

    public string Evidence
    {
        get => _evidence;

        set =>
            _evidence =
                NormalizeText(
                    value,
                    trim: false);
    }

    private static string NormalizeText(
        string? value,
        bool trim = true)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        return trim
            ? value.Trim()
            : value;
    }
}

public sealed class AssistantRecommendation
{
    private string _domain =
        string.Empty;

    private string _priority =
        string.Empty;

    private string _change =
        string.Empty;

    private string _why =
        string.Empty;

    private string _confidence =
        string.Empty;

    public string Domain
    {
        get => _domain;

        set =>
            _domain =
                NormalizeText(
                    value);
    }

    public string Priority
    {
        get => _priority;

        set =>
            _priority =
                NormalizeText(
                    value);
    }

    public string Change
    {
        get => _change;

        set =>
            _change =
                NormalizeText(
                    value);
    }

    public string Why
    {
        get => _why;

        set =>
            _why =
                NormalizeText(
                    value,
                    trim: false);
    }

    public string Confidence
    {
        get => _confidence;

        set =>
            _confidence =
                NormalizeText(
                    value);
    }

    private static string NormalizeText(
        string? value,
        bool trim = true)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        return trim
            ? value.Trim()
            : value;
    }
}

public sealed class AssistantComparisonRow
{
    private string _metric =
        string.Empty;

    private string _previous =
        string.Empty;

    private string _current =
        string.Empty;

    private string _change =
        string.Empty;

    private string _interpretation =
        string.Empty;

    public string Metric
    {
        get => _metric;

        set =>
            _metric =
                NormalizeText(
                    value);
    }

    public string Previous
    {
        get => _previous;

        set =>
            _previous =
                NormalizeText(
                    value);
    }

    public string Current
    {
        get => _current;

        set =>
            _current =
                NormalizeText(
                    value);
    }

    public string Change
    {
        get => _change;

        set =>
            _change =
                NormalizeText(
                    value);
    }

    public string Interpretation
    {
        get => _interpretation;

        set =>
            _interpretation =
                NormalizeText(
                    value,
                    trim: false);
    }

    private static string NormalizeText(
        string? value,
        bool trim = true)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return string.Empty;
        }

        return trim
            ? value.Trim()
            : value;
    }
}

public sealed class TuningAssistantReport
{
    private string _overallAssessment =
        string.Empty;

    private string _confidenceReason =
        string.Empty;

    private List<AssistantBehaviorAssessment> _assessments =
        [];

    private List<AssistantRecommendation> _recommendations =
        [];

    private List<AssistantComparisonRow> _comparison =
        [];

    private List<string> _preserveNotes =
        [];

    private TelemetryCalibrationSuggestion _proposedCalibration =
        new();

    private CarBehaviorTarget _suggestedBehaviorTarget =
        new();

    private string _suggestedBehaviorSummary =
        string.Empty;

    public string OverallAssessment
    {
        get => _overallAssessment;

        set =>
            _overallAssessment =
                value ??
                string.Empty;
    }

    public AssistantConfidence OverallConfidence { get; set; }

    public string ConfidenceReason
    {
        get => _confidenceReason;

        set =>
            _confidenceReason =
                value ??
                string.Empty;
    }

    public List<AssistantBehaviorAssessment> Assessments
    {
        get => _assessments;

        set =>
            _assessments =
                value ??
                [];
    }

    public List<AssistantRecommendation> Recommendations
    {
        get => _recommendations;

        set =>
            _recommendations =
                value ??
                [];
    }

    public List<AssistantComparisonRow> Comparison
    {
        get => _comparison;

        set =>
            _comparison =
                value ??
                [];
    }

    public List<string> PreserveNotes
    {
        get => _preserveNotes;

        set =>
            _preserveNotes =
                value ??
                [];
    }

    public TelemetryCalibrationSuggestion ProposedCalibration
    {
        get => _proposedCalibration;

        set =>
            _proposedCalibration =
                value ??
                new TelemetryCalibrationSuggestion();
    }

    public CarBehaviorTarget SuggestedBehaviorTarget
    {
        get => _suggestedBehaviorTarget;

        set =>
            _suggestedBehaviorTarget =
                value ??
                new CarBehaviorTarget();
    }

    public bool HasSuggestedBehaviorChange { get; set; }

    public string SuggestedBehaviorSummary
    {
        get => _suggestedBehaviorSummary;

        set =>
            _suggestedBehaviorSummary =
                value ??
                string.Empty;
    }
}