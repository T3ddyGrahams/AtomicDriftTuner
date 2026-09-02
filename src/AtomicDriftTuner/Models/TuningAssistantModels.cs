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
    public string JsonPath { get; set; } = "";
    public TelemetrySession Session { get; set; } = new();
    public TelemetryAnalysis Analysis { get; set; } = new();

    public DateTime SessionUtc =>
        Session.StartedUtc == default
            ? File.GetLastWriteTimeUtc(JsonPath)
            : Session.StartedUtc;

    public string DisplayName =>
        $"{SessionUtc.ToLocalTime():g} • {Session.CarName} • {Analysis.DriftTimeSeconds:0}s drift • {Analysis.TransitionCount} transitions";

    public override string ToString() => DisplayName;
}

public sealed class AssistantBehaviorAssessment
{
    public string Behavior { get; set; } = "";
    public string Desired { get; set; } = "";
    public string Observed { get; set; } = "";
    public string Status { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string Evidence { get; set; } = "";
}

public sealed class AssistantRecommendation
{
    public string Domain { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Change { get; set; } = "";
    public string Why { get; set; } = "";
    public string Confidence { get; set; } = "";
}

public sealed class AssistantComparisonRow
{
    public string Metric { get; set; } = "";
    public string Previous { get; set; } = "";
    public string Current { get; set; } = "";
    public string Change { get; set; } = "";
    public string Interpretation { get; set; } = "";
}

public sealed class TuningAssistantReport
{
    public string OverallAssessment { get; set; } = "";
    public AssistantConfidence OverallConfidence { get; set; }
    public string ConfidenceReason { get; set; } = "";
    public List<AssistantBehaviorAssessment> Assessments { get; set; } = [];
    public List<AssistantRecommendation> Recommendations { get; set; } = [];
    public List<AssistantComparisonRow> Comparison { get; set; } = [];
    public List<string> PreserveNotes { get; set; } = [];
    public TelemetryCalibrationSuggestion ProposedCalibration { get; set; } = new();
    public CarBehaviorTarget SuggestedBehaviorTarget { get; set; } = new();
    public bool HasSuggestedBehaviorChange { get; set; }
    public string SuggestedBehaviorSummary { get; set; } = "";
}
