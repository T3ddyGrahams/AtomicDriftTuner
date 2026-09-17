namespace AtomicDriftTuner.Models;

public sealed class CompanionWorkflowState
{
    public int ProtocolVersion { get; set; } = 1;
    public string ControlVersion { get; set; } = "";
    public bool Available { get; set; }
    public string Message { get; set; } = "Prepare your workflow and open Telemetry Recorder in desktop ADT first.";
    public string Stage { get; set; } = "Welcome";
    public string ComingNext { get; set; } = "";
    public string Focus { get; set; } = "";
    public string Car { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Conditions { get; set; } = "";
    public string SetupName { get; set; } = "";
    public string ConfirmationText { get; set; } = "";
    public bool TuneConfirmed { get; set; }
    public bool CanPrepare { get; set; }
    public bool CanConfirm { get; set; }
    public bool CanReadFindings { get; set; }
    public bool CanSaveReview { get; set; }
    public string SelectedRecommendation { get; set; } = "";
    public string SavedSessionId { get; set; } = "";
    public string BaselineSessionId { get; set; } = "";
    public CompanionWorkflowReport? Report { get; set; }
}

public sealed class CompanionWorkflowReport
{
    public string SessionId { get; set; } = "";
    public string BaselineSessionId { get; set; } = "";
    public string Goal { get; set; } = "";
    public string Noticed { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string Why { get; set; } = "";
    public string Confidence { get; set; } = "";
    public List<CompanionWorkflowRecommendation> Recommendations { get; set; } = [];
    public CompanionWorkflowComparison Comparison { get; set; } = new();
    public bool ReviewSaved { get; set; }
}

public sealed class CompanionWorkflowRecommendation
{
    public string Id { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Change { get; set; } = "";
    public string Why { get; set; } = "";
    public string Confidence { get; set; } = "";
    public bool CanSelect { get; set; }
    public string DisabledReason { get; set; } = "";
}

public sealed class CompanionWorkflowComparison
{
    public string Verdict { get; set; } = "Inconclusive";
    public string Summary { get; set; } = "Record and save a comparison run after planning one change.";
    public bool Comparable { get; set; }
    public List<string> Limitations { get; set; } = [];
}

public sealed class CompanionWorkflowCommand
{
    public string Action { get; set; } = "";
    public string ControlVersion { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string RecommendationId { get; set; } = "";
    public bool TuneConfirmed { get; set; }
    public string DriverRating { get; set; } = "Not rated";
    public string NextAction { get; set; } = "Undecided";
    public string Notes { get; set; } = "";
}
