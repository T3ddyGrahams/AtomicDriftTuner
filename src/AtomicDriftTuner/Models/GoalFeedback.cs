using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

public enum FeedbackRating { NotAnswered, Better, Same, Worse, CouldNotJudge }
public enum ProblemRating { NotAnswered, No, Yes, CouldNotJudge }
public enum GoalSignal { Unknown, Closer, Same, Farther, Mixed, Context }

// Structured results from the existing comparison rules, independent of display prose.
public sealed class GoalMetricEvidence
{
    public string Key { get; set; } = "";
    public GoalSignal Signal { get; set; }
    public string Explanation { get; set; } = "";
}

public sealed class GoalFeedbackQuestion
{
    public string Key { get; set; } = "";
    public string Prompt { get; set; } = "";
    public bool IsTestGoal { get; set; }
    public GoalSignal Signal { get; set; }
    public string Evidence { get; set; } = "";
    public FeedbackRating Answer { get; set; }
}

// Frozen review context. Answers are restored only into an identical context fingerprint.
public sealed class GoalFeedback
{
    public string Schema { get; set; } = "adt/goal-feedback/1";
    public string Scope { get; set; } = "Run";
    public string ContextFingerprint { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string BaselineSessionId { get; set; } = "";
    public string DriverId { get; set; } = "";
    public string ContextKey { get; set; } = "";
    public string TestId { get; set; } = "";
    public string GoalSignature { get; set; } = "";
    public string SectionId { get; set; } = "";
    public double? BeforeStart { get; set; }
    public double? BeforeEnd { get; set; }
    public double? AfterStart { get; set; }
    public double? AfterEnd { get; set; }
    public string ContextDescription { get; set; } = "";
    public bool Comparable { get; set; }
    public bool ExactTestTracked { get; set; }
    public bool TelemetryTradeoff { get; set; }
    public string Limitation { get; set; } = "";
    public List<GoalFeedbackQuestion> Questions { get; set; } = [];
    public ProblemRating NewProblem { get; set; }
    public string ProblemNotes { get; set; } = "";
    [JsonIgnore] public bool HasAnswers => NewProblem != ProblemRating.NotAnswered || ProblemNotes.Length > 0 || Questions.Any(q => q.Answer != FeedbackRating.NotAnswered);
}

public sealed class SectionFeedbackReview
{
    public string Schema { get; set; } = "adt/section-feedback-review/1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime ReviewedUtc { get; set; } = DateTime.UtcNow;
    public GoalFeedback Feedback { get; set; } = new();
    public override string ToString() => $"{ReviewedUtc.ToLocalTime():g} · {Feedback.Questions.Count(q => q.Answer != FeedbackRating.NotAnswered)} answered";
}

public sealed record FeedbackGuidance(string Action, string Message, bool CanReviewNextTest = false);
