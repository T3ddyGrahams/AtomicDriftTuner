using System.Security.Cryptography;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Engine;

/// <summary>Combines driver observations with existing evidence gates. Does not generate tuning values.</summary>
public static class GoalFeedbackEngine
{
    public static GoalFeedback? ForRun(SavedTelemetrySession? before, SavedTelemetrySession? after)
    {
        if (before is null || after is null || before.Session.Id == after.Session.Id ||
            !RunHistoryStore.ValidContext(before.Session.Context) || !RunHistoryStore.ValidContext(after.Session.Context)) return null;
        var c = after.Session.Context!;
        var comparison = new RunComparisonEngine().Compare(before, after);
        var f = Context(before, after);
        f.Comparable = comparison.Comparable;
        f.ExactTestTracked = comparison.RecommendationTestTracked || comparison.DriverTestTracked;
        f.TelemetryTradeoff = comparison.GoalEvidence.Any(m => m.Signal == GoalSignal.Farther);
        f.Limitation = !comparison.Comparable ? string.Join(" ", comparison.Limitations.Take(2)) : !f.ExactTestTracked ? comparison.TestMatchSummary : "";
        f.ContextDescription = $"Whole run · {after.Session.CarName}\nSaved goal: {AssistantNextStepBuilder.DescribeGoal(c.Tune!.DesiredBehavior)}\n" +
            (comparison.TuneChanges.Count == 0 ? "No captured setting change." : "Captured changes: " + string.Join("; ", comparison.TuneChanges.Take(4).Select(r => $"{r.Metric}: {r.Previous} → {r.Current}"))) +
            "\n" + comparison.TestMatchSummary;
        string tested = RecommendationTestService.Valid(c.Test) ? c.Test!.MetricKey : "";
        var b = c.Tune!.DesiredBehavior;
        var keys = new List<string>();
        if (tested.Length > 0) keys.Add(tested);
        if (TuningFocusOptions.IncludesCar(c.Focus))
        {
            if (b.HasAngleGoal) keys.Add("angle-hold");
            if (b.InitiationSharpness != 0) keys.Add("initiation");
            if (b.TransitionSpeed != 0) keys.Add("transition");
            if (b.FrontEndBite != 0) keys.Add("front-response");
            if (b.RearGrip != 0) keys.Add("rear-slip-share");
            keys.AddRange(["initiation", "transition", "stability"]);
        }
        else keys.AddRange(["self-steer", "oscillation", "clipping"]);
        foreach (var key in keys)
        {
            var (group, prompt) = Question(key);
            if (f.Questions.Any(q => q.Key == group)) continue;
            var evidence = comparison.GoalEvidence.FirstOrDefault(m => m.Key == key);
            f.Questions.Add(new GoalFeedbackQuestion { Key = group, Prompt = prompt, IsTestGoal = key == tested,
                Signal = evidence?.Signal ?? GoalSignal.Unknown, Evidence = evidence?.Explanation ?? "This goal has no scored telemetry comparison yet." });
            if (f.Questions.Count == 3) break;
        }
        Fingerprint(f, before, after, null);
        return f;
    }

    public static GoalFeedback? ForSection(TrackSection section, SectionPass a, SectionPass b, SavedTelemetrySession before, SavedTelemetrySession after)
    {
        if (!RunHistoryStore.ValidContext(before.Session.Context) || !RunHistoryStore.ValidContext(after.Session.Context)) return null;
        var f = Context(before, after);
        f.Scope = "Section"; f.SectionId = section.Id;
        f.BeforeStart = a.Start; f.BeforeEnd = a.End; f.AfterStart = b.Start; f.AfterEnd = b.End;
        var comparison = TrackSectionEngine.Compare(a, b, section, before, after);
        f.Comparable = comparison.Comparable;
        f.ExactTestTracked = false; // Section observations never certify a setup change or rewrite run-level evidence.
        f.Limitation = comparison.Findings;
        f.ContextDescription = $"Section only · {section.Name} · {section.Track}/{section.Layout}\n" +
            $"Before {a.Start:0.00}–{a.End:0.00}s → after {b.Start:0.00}–{b.End:0.00}s. " +
            (before.Session.Id == after.Session.Id ? "Two passes in the same recording: a consistency check." : "Passes from the selected baseline and after-run.") +
            $"\nSaved section revision {section.Id[..6]}: {section.LineAim}, {Math.Abs(section.TargetOffsetM):0.0} m. " +
            "Section goals may have been chosen after driving; they do not replace the run's recorded Desired Behavior.";
        void Add(string key, string prompt, string evidence) => f.Questions.Add(new() { Key = key, Prompt = prompt, Signal = GoalSignal.Context, Evidence = evidence });
        Add("line", "Was it easier to follow your chosen line through this section?", $"Distance from selected reference aim: {a.LineError:0.00} → {b.LineError:0.00} m. Descriptive; road width and a meaningful improvement threshold are unknown.");
        if (section.MinimumAngle.HasValue)
            Add("angle", $"Was it easier to hold {section.MinimumAngle:0}–{section.MaximumAngle:0}° with control here?", $"Time in section angle band: {a.AngleInGoalPct:0.0} → {b.AngleInGoalPct:0.0}%. Descriptive pass measurements.");
        if (section.PreferredGear.HasValue)
            Add("gear", $"Did gear {section.PreferredGear} work better through this section?", $"Time in preferred gear: {a.PreferredGearPct:0.0} → {b.PreferredGearPct:0.0}%. Gear use alone does not establish a better ratio.");
        Fingerprint(f, before, after, section);
        return f;
    }

    private static GoalFeedback Context(SavedTelemetrySession before, SavedTelemetrySession after) => new()
    {
        SessionId = after.Session.Id, BaselineSessionId = before.Session.Id,
        DriverId = after.Session.Context!.DriverId, ContextKey = after.Session.Context.Tune!.ContextKey,
        TestId = RecommendationTestService.Valid(after.Session.Context.Test) ? after.Session.Context.Test!.Id : "", GoalSignature = GuidedWorkflowStore.GoalSignature(after.Session.Context.Tune.DesiredBehavior)
    };

    private static void Fingerprint(GoalFeedback f, SavedTelemetrySession before, SavedTelemetrySession after, TrackSection? section)
    {
        // Includes both contexts, immutable section geometry/goals, evidence and exact pass identity.
        f.ContextFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Feedback = f, Before = before.Session.Context, After = after.Session.Context, Section = section,
            BeforeVersion = before.Analysis.Diagnosis.AnalyzerVersion, AfterVersion = after.Analysis.Diagnosis.AnalyzerVersion
        })));
    }

    private static (string Key, string Prompt) Question(string key) => key switch
    {
        "initiation" => (key, "Did starting the drift feel closer to your saved initiation goal?"),
        "transition" => (key, "Did switching drift direction feel closer to your saved transition goal?"),
        "front-response" => (key, "Did the front respond closer to the amount of bite you wanted?"),
        "rear-slip-share" => (key, "Did rear grip feel closer to the balance you wanted?"),
        "self-steer" => (key, "Did the wheel return closer to the speed and feel you wanted?"),
        "oscillation" => (key, "Was unwanted steering wobble better or worse?"),
        "clipping" => (key, "Was force-feedback detail better or worse under load?"),
        "throttle-rotation" => (key, "Was controlling rotation with the throttle closer to your goal?"),
        "stability" or "extreme-angle" => ("stability", "Was the car easier to control and settle after the drift?"),
        "angle-time" or "angle-hold" or "angle-speed" or "angle-recovery" => ("angle", "Was it easier to sustain your requested angle with control?"),
        _ => ("tested-goal", "Did the recorded test get closer to the goal you were testing?")
    };

    public static void RestoreAnswers(GoalFeedback target, GoalFeedback? saved)
    {
        if (saved is null || !Valid(saved) || target.ContextFingerprint != saved.ContextFingerprint) return;
        foreach (var q in target.Questions) q.Answer = saved.Questions.FirstOrDefault(x => x.Key == q.Key)?.Answer ?? FeedbackRating.NotAnswered;
        target.NewProblem = saved.NewProblem; target.ProblemNotes = saved.ProblemNotes;
    }

    public static FeedbackGuidance Evaluate(GoalFeedback f)
    {
        var rated = f.Questions.Where(q => q.Answer is FeedbackRating.Better or FeedbackRating.Same or FeedbackRating.Worse).ToArray();
        bool better = rated.Any(q => q.Answer == FeedbackRating.Better), worse = rated.Any(q => q.Answer == FeedbackRating.Worse);
        if (f.NewProblem == ProblemRating.Yes || better && worse)
            return new("Review tradeoff", "You reported a new problem or mixed results. Review what improved and what became worse before changing anything else. If control feels worse, consider manually returning to the baseline and repeat the same driving task. " + (f.Comparable ? "" : "These measurements are not comparable yet."));
        if (!f.Comparable)
            return new("Repeat the comparison", "Keep this feedback as an observation. ADT needs a fair comparison before judging the change. " + f.Limitation);
        if (f.Scope == "Section")
            return new("Repeat this section", "Your answers describe these two passes and this section goal only. Repeat the section with similar speed and inputs. Use whole-run Before / After to assess a recorded setup test; map position and gear use do not establish a tuning cause.");
        if (rated.Length == 0 || f.NewProblem is ProblemRating.NotAnswered or ProblemRating.CouldNotJudge || f.Questions.Any(q => q.Answer == FeedbackRating.NotAnswered))
            return new("Finish the quick review", "Answer the questions and the new-problem check. Choose Couldn't judge whenever you are unsure; ADT will retain that uncertainty. You can save a partial review.");
        if (rated.Any(q => q.Answer == FeedbackRating.Better && q.Signal == GoalSignal.Farther || q.Answer == FeedbackRating.Worse && q.Signal == GoalSignal.Closer))
            return new("Repeat and check the disagreement", "Your feel and the measured goal point in different directions. Repeat the same test before calling it an improvement. The telemetry is a proxy and may not explain the feel you noticed.");
        if (!f.ExactTestTracked)
            return new("Confirm the tested change", "Your feedback is saved as an observation. ADT could not verify an exact test between these runs. " + f.Limitation + " Prepare one tracked change and confirm the loaded setup for the next comparison.");
        if (worse)
            return new("Consider the baseline", "You preferred the baseline in at least one area. Consider restoring it manually, then record another comparable run before choosing a different supported adjustment.");
        if (f.TelemetryTradeoff)
            return new("Review measured tradeoff", "At least one measured goal became worse. Inspect Before / After and repeat the test before keeping it, even if another area felt better.");
        var tested = f.Questions.FirstOrDefault(q => q.IsTestGoal);
        if (tested is null || tested.Answer == FeedbackRating.CouldNotJudge || tested.Signal is GoalSignal.Unknown or GoalSignal.Context)
            return new("Repeat the tested goal", "There is not enough driver and measured evidence for the goal of this test. Keep the question open and repeat that driving task; an improvement elsewhere does not validate this change.");
        if (tested.Answer == FeedbackRating.Better && tested.Signal == GoalSignal.Closer)
            return new("Keep and verify", "Your feel and telemetry agree on the recorded test goal, with no reported or scored regression. Keep this as a candidate improvement and repeat the same test to verify it for this car and driver. One comparison does not prove the change caused it.");
        if (rated.All(q => q.Answer == FeedbackRating.Same) && tested.Signal == GoalSignal.Same)
            return new("Review another supported test", "No clear benefit was reported or measured for the tested goal. You can review another adjustment supported by this run, or return to the baseline and test again. Review exact values before saving or staging anything.", true);
        return new("Repeat to confirm", "The result is uncertain: the reported benefit is not yet supported by the tested metric, or the feel stayed the same while measurements changed. Repeat the same task before making another adjustment.");
    }

    public static string Summary(GoalFeedback f) => string.Join("\n", f.Questions.Select(q => $"{q.Prompt} {Label(q.Answer)}")) +
        $"\nNew problem: {f.NewProblem}. {f.ProblemNotes}\n{Evaluate(f).Message}";
    public static string Label(FeedbackRating rating) => rating switch { FeedbackRating.NotAnswered => "Not answered", FeedbackRating.CouldNotJudge => "Couldn't judge", _ => rating.ToString() };

    public static bool Valid(GoalFeedback? f) => f is not null && f.Schema == "adt/goal-feedback/1" && f.Scope is "Run" or "Section" &&
        Guid.TryParseExact(f.SessionId, "N", out _) && Guid.TryParseExact(f.BaselineSessionId, "N", out _) && Guid.TryParseExact(f.DriverId, "N", out _) &&
        f.ContextFingerprint is { Length: 64 } && f.ContextFingerprint.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(f.ContextKey) && !string.IsNullOrWhiteSpace(f.GoalSignature) &&
        f.TestId is not null && (f.TestId.Length == 0 || Guid.TryParseExact(f.TestId, "N", out _)) &&
        f.ContextDescription is { Length: <= 16000 } && f.Limitation is { Length: <= 16000 } && f.ProblemNotes is { Length: <= 2000 } && Enum.IsDefined(f.NewProblem) &&
        f.Questions is { Count: >= 1 and <= 3 } && f.Questions.All(q => q is not null && q.Key is { Length: > 0 and <= 80 } &&
            q.Prompt is { Length: > 0 and <= 500 } && q.Evidence is { Length: <= 2000 } && Enum.IsDefined(q.Answer) && Enum.IsDefined(q.Signal)) &&
        f.Questions.Select(q => q.Key).Distinct(StringComparer.Ordinal).Count() == f.Questions.Count && f.Questions.Count(q => q.IsTestGoal) <= 1 &&
        (f.Scope == "Run" ? f.SectionId == "" && f.BeforeStart is null && f.BeforeEnd is null && f.AfterStart is null && f.AfterEnd is null && f.SessionId != f.BaselineSessionId :
            Guid.TryParseExact(f.SectionId, "N", out _) && !f.ExactTestTracked && ValidPass(f.BeforeStart, f.BeforeEnd) && ValidPass(f.AfterStart, f.AfterEnd));
    private static bool ValidPass(double? start, double? end) => start is double a && end is double b && double.IsFinite(a) && double.IsFinite(b) && a >= 0 && b > a;
}
