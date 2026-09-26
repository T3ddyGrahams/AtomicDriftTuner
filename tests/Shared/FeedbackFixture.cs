using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class FeedbackFixture
{
    public static (SavedTelemetrySession Before, SavedTelemetrySession After) Pair(string contextKey = "fixture-key")
    {
        var session = TrackFixture.Run(); var driver = Guid.NewGuid().ToString("N");
        session.Wheelbase = "Fixture base"; session.SteeringWheel = "Fixture rim"; session.DriftTarget = "Tandem";
        session.Context = new RunContext { DriverId = driver, DriverName = "Test driver", TrackId = "test_track/layout_a", Conditions = "Dry repeated corner",
            CarIdentityVerified = true, TuneConfirmedInUse = true, Tune = new TuneVersion { DriverId = driver, ContextKey = contextKey,
                DesiredBehavior = new() { TransitionSpeed = 2 }, SetupSha256 = "fixture-snapshot", SetupSource = "manual-file",
                Settings = new() { ["ACSetup.DAMP_REBOUND_LR"] = 7 } } };
        var before = new SavedTelemetrySession { Session = session, Analysis = new TelemetryAnalyzer().Analyze(session) };
        foreach (var (key, value) in new[] { ("transition", 1.6), ("initiation", .9), ("stability", 5d), ("throttle", .6) }) Set(before, key, value);
        var after = RunHistoryStore.Clone(before);
        after.Session.Id = Guid.NewGuid().ToString("N"); after.Session.StartedUtc = session.StartedUtc.AddMinutes(5);
        var c = after.Session.Context!; c.Tune!.Id = Guid.NewGuid().ToString("N"); c.Tune.Settings["ACSetup.DAMP_REBOUND_LR"] = 8;
        c.RecommendationSessionId = session.Id; c.TestedRecommendations = ["Test a quicker transition"];
        c.Test = RecommendationTestService.Create(before, c.TestedRecommendations[0], "transition", [new("ACSetup.DAMP_REBOUND_LR", 7, 8)]);
        Set(after, "transition", .55);
        return (before, after);
    }
    public static void Set(SavedTelemetrySession run, string key, double value)
    {
        var m = run.Analysis.Diagnosis.Metric(key);
        if (m is null) { m = new RunMetric { Key = key, Name = key }; run.Analysis.Diagnosis.Metrics.Add(m); }
        m.Value = value; m.Confidence = "HIGH";
    }
    public static GoalFeedback Answered(GoalFeedback f, FeedbackRating rating = FeedbackRating.Better)
    { foreach (var q in f.Questions) q.Answer = q.IsTestGoal ? rating : FeedbackRating.Same; f.NewProblem = ProblemRating.No; return f; }
}
