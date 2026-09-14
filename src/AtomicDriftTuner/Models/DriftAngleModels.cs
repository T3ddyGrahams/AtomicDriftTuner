using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

public enum SustainedAnglePreference { Current = 0, MoreAngle = 1, ExtremeAngle = 2 }

public sealed class DriftAngleDiagnosis
{
    public bool Enabled { get; set; }
    public double TargetMinDeg { get; set; }
    public double TargetMaxDeg { get; set; }
    public double TargetSeconds { get; set; }
    public double LongestHoldSeconds { get; set; }
    public int CompletedAttempts { get; set; }
    public int IncompleteAttempts { get; set; }
    public int RecoveredAttempts { get; set; }
    public int ControlConcerns { get; set; }
    public double? SpeedRetentionPct { get; set; }
    public List<AngleHoldAttempt> Attempts { get; set; } = [];
    public string Summary { get; set; } = "";
}

public sealed class AngleHoldAttempt
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double InTargetSeconds { get; set; }
    public double LongestHoldSeconds { get; set; }
    public bool Complete { get; set; }
    public bool RecoveryObserved { get; set; }
    public bool ControlConcern { get; set; }
    public double? SpeedRetentionPct { get; set; }
    public string Evidence { get; set; } = "";
}

public sealed class AssistantNextStep
{
    public string Goal { get; set; } = "Choose a saved run.";
    public string Noticed { get; set; } = "Record and save a run to get a next step.";
    public string Confidence { get; set; } = "Waiting for a run";
    public string Instruction { get; set; } = "Use the dashboard walkthrough to prepare your recording.";
    public string Why { get; set; } = "";
    public string Action { get; set; } = "Record";
    public string ActionLabel { get; set; } = "Back to dashboard";
    [JsonIgnore] public AssistantRecommendation? Recommendation { get; set; }
}
