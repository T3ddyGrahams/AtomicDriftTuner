using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class FeedbackChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
        GoalFeedback Form() { var (a, b) = FeedbackFixture.Pair(); return GoalFeedbackEngine.ForRun(a, b)!; }
        test("feedback uses the exact tested goal without default answers or tuning mutations", () =>
        {
            var (a, b) = FeedbackFixture.Pair(); var snapshot = JsonSerializer.Serialize(new[] { a, b });
            var f = GoalFeedbackEngine.ForRun(a, b)!;
            Check(GoalFeedbackEngine.Valid(f) && f.ExactTestTracked && f.Questions.Count == 3, f.Limitation);
            Check(!f.HasAnswers && f.Questions[0].Key == "transition" && f.Questions[0].IsTestGoal, "Question binding/default wrong");
            Check(f.Questions[0].Signal == GoalSignal.Closer, "Existing timing comparison not reused");
            FeedbackFixture.Answered(f);
            Check(GoalFeedbackEngine.Evaluate(f).Action == "Keep and verify", GoalFeedbackEngine.Evaluate(f).Message);
            Check(snapshot == JsonSerializer.Serialize(new[] { a, b }), "Review changed recorded facts or tune");
        });
        test("feedback preserves unsure and partial answers instead of treating them as success", () =>
        {
            var f = FeedbackFixture.Answered(Form()); f.Questions[0].Answer = FeedbackRating.CouldNotJudge;
            Check(GoalFeedbackEngine.Evaluate(f).Action == "Repeat the tested goal", "Unknown primary became positive");
            f.Questions[0].Answer = FeedbackRating.NotAnswered;
            Check(GoalFeedbackEngine.Evaluate(f).Action == "Finish the quick review", "Partial review inferred answer");
            f = FeedbackFixture.Answered(Form()); f.NewProblem = ProblemRating.CouldNotJudge;
            Check(GoalFeedbackEngine.Evaluate(f).Action != "Keep and verify", "Unknown new problem accepted");
        });
        test("feedback flags driver telemetry disagreements and mixed results", () =>
        {
            var f = FeedbackFixture.Answered(Form(), FeedbackRating.Worse);
            Check(GoalFeedbackEngine.Evaluate(f).Action.Contains("disagreement"), "Opposite feel hidden");
            f = FeedbackFixture.Answered(Form()); f.Questions[1].Answer = FeedbackRating.Worse;
            Check(GoalFeedbackEngine.Evaluate(f).Action == "Review tradeoff", "Mixed feedback ignored");
            f = FeedbackFixture.Answered(Form()); f.NewProblem = ProblemRating.Yes;
            Check(GoalFeedbackEngine.Evaluate(f).Action == "Review tradeoff", "New control problem ignored");
        });
        test("feedback never relaxes baseline goal driver quality or exact test gates", () =>
        {
            foreach (var mutate in new Action<SavedTelemetrySession>[] {
                b => b.Analysis.EffectiveSampleRateHz = 5,
                b => b.Session.Context!.Tune!.DesiredBehavior.TransitionSpeed = -2,
                b => { var id = Guid.NewGuid().ToString("N"); b.Session.Context!.DriverId = id; b.Session.Context.Tune!.DriverId = id; },
                b => b.Session.Context!.Conditions = "wet",
                b => b.Session.Context!.Tune!.Settings["ACSetup.DAMP_REBOUND_LR"] = 9,
                b => b.Session.Context!.TuneConfirmedInUse = false })
            {
                var (a, b) = FeedbackFixture.Pair(); mutate(b);
                var f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!);
                var result = GoalFeedbackEngine.Evaluate(f);
                Check(result.Action != "Keep and verify" && !result.CanReviewNextTest, "Comparison gate bypassed");
            }
        });
        test("feedback retains low confidence metrics as unknown", () =>
        {
            var (a, b) = FeedbackFixture.Pair(); b.Analysis.Diagnosis.Metric("transition")!.Confidence = "LOW";
            var f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!);
            Check(f.Questions[0].Signal == GoalSignal.Unknown && GoalFeedbackEngine.Evaluate(f).Action != "Keep and verify", "Sparse metric scored");
        });
        test("feedback recommends considering the baseline when feel and the tested metric worsen", () =>
        {
            var (a, b) = FeedbackFixture.Pair(); FeedbackFixture.Set(b, "transition", 2.0);
            var f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!, FeedbackRating.Worse);
            Check(f.Questions[0].Signal == GoalSignal.Farther && GoalFeedbackEngine.Evaluate(f).Action == "Consider the baseline", "Worse result did not route to baseline review");
        });
        test("positive feedback cannot conceal a measured regression outside the test goal", () =>
        {
            var (a, b) = FeedbackFixture.Pair(); FeedbackFixture.Set(b, "stability", 10);
            var f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!);
            Check(f.TelemetryTradeoff && GoalFeedbackEngine.Evaluate(f).Action == "Review measured tradeoff", "Secondary regression ignored");
        });
        test("feedback offers a supported next test only after an unchanged matched test", () =>
        {
            var (a, b) = FeedbackFixture.Pair(); FeedbackFixture.Set(b, "transition", 1.6);
            var f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!, FeedbackRating.Same);
            Check(GoalFeedbackEngine.Evaluate(f).CanReviewNextTest, GoalFeedbackEngine.Evaluate(f).Message);
            b.Session.Context!.Test = null;
            f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!, FeedbackRating.Same);
            Check(!GoalFeedbackEngine.Evaluate(f).CanReviewNextTest, "Untracked test offered next adjustment");
        });
        test("feedback restore requires exact context including baseline goals test and evidence", () =>
        {
            var (a, b) = FeedbackFixture.Pair(); var saved = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!);
            var fresh = GoalFeedbackEngine.ForRun(a, b)!; GoalFeedbackEngine.RestoreAnswers(fresh, saved);
            Check(fresh.HasAnswers, "Identical context not restored");
            b.Analysis.Diagnosis.Metric("transition")!.Confidence = "LOW";
            fresh = GoalFeedbackEngine.ForRun(a, b)!; GoalFeedbackEngine.RestoreAnswers(fresh, saved); Check(!fresh.HasAnswers, "Changed evidence restored answers");
            b.Session.Context!.Tune!.DesiredBehavior.TransitionSpeed = -2;
            fresh = GoalFeedbackEngine.ForRun(a, b)!; GoalFeedbackEngine.RestoreAnswers(fresh, saved); Check(!fresh.HasAnswers, "Changed goals restored answers");
            a.Session.Id = Guid.NewGuid().ToString("N");
            fresh = GoalFeedbackEngine.ForRun(a, b)!; GoalFeedbackEngine.RestoreAnswers(fresh, saved); Check(!fresh.HasAnswers, "Changed baseline restored answers");
        });
        test("feedback history is additive immutable survives reopening and rejects wrong review identity", () =>
        {
            var input = new TuneInput { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
            var (a, b) = FeedbackFixture.Pair(RunHistoryStore.ContextKey(input)); var f = FeedbackFixture.Answered(GoalFeedbackEngine.ForRun(a, b)!);
            var store = new RunHistoryStore(Path.Combine(root, "goal-feedback"));
            var review = new RunReview { SessionId = b.Session.Id, BaselineSessionId = a.Session.Id, DriverId = f.DriverId, ContextKey = f.ContextKey,
                GoalFeedback = f, Comparison = new RunComparisonEngine().Compare(a, b) };
            store.SaveReview(review); review.Id = Guid.NewGuid().ToString("N"); review.GoalFeedback!.NewProblem = ProblemRating.Yes; store.SaveReview(review);
            var history = new RunHistoryStore(store.RootDirectory).ListReviews(input, f.DriverId);
            Check(history.Count == 2 && history.Any(r => r.Conclusion.Contains("agree")) && history.Any(r => r.Conclusion.Contains("new problem")), "Revisions overwritten");
            review.Id = Guid.NewGuid().ToString("N"); review.BaselineSessionId = Guid.NewGuid().ToString("N");
            bool rejected = false; try { store.SaveReview(review); } catch (InvalidDataException) { rejected = true; } Check(rejected, "Mismatched feedback accepted");
            review.GoalFeedback = null; store.SaveReview(review);
            Check(store.ListReviews(input, f.DriverId).Any(r => r.GoalFeedback is null), "Legacy review broken");
        });
        test("section feedback binds exact passes and revisions without granting whole-run attribution", () =>
        {
            var (run, _) = FeedbackFixture.Pair(); var section = TrackSectionEngine.Mark(run.Session, 1, 7, "Tight corner", 30, 45, 2, 0, "Follow reference");
            var passes = TrackSectionEngine.Analyze(run.Session, section).Passes;
            Check(passes.Count == 3, "Fixture passes missing");
            var f = GoalFeedbackEngine.ForSection(section, passes[0], passes[1], run, run)!;
            foreach (var q in f.Questions) q.Answer = FeedbackRating.Better; f.NewProblem = ProblemRating.No;
            Check(GoalFeedbackEngine.Valid(f) && f.Comparable && !f.ExactTestTracked && !GoalFeedbackEngine.Evaluate(f).CanReviewNextTest && f.Questions.Count == 3, "Section gained setup attribution");
            var another = GoalFeedbackEngine.ForSection(section, passes[0], passes[2], run, run)!;
            GoalFeedbackEngine.RestoreAnswers(another, f); Check(!another.HasAnswers, "Different passes inherited answers");
            var same = GoalFeedbackEngine.ForSection(section, passes[0], passes[0], run, run)!;
            Check(!same.Comparable, "Same pass compared with itself");
            var store = new TrackSectionStore(Path.Combine(root, "section-feedback")); store.Save(section);
            var review = new SectionFeedbackReview { Feedback = f }; store.SaveFeedback(review);
            bool rejected = false; try { store.SaveFeedback(review); } catch (IOException) { rejected = true; } Check(rejected, "Section review overwritten");
            Check(store.LoadFeedback(f.ContextFingerprint, out _).Single().Feedback.Questions.All(q => q.Answer == FeedbackRating.Better), "Section answers lost");
            Check(store.LoadFeedback(another.ContextFingerprint, out _).Count == 0, "Section reviews mixed");
            var revision = RunHistoryStore.Clone(section); revision.Id = Guid.NewGuid().ToString("N"); revision.MinimumAngle = 32;
            var revisedPasses = TrackSectionEngine.Analyze(run.Session, revision).Passes;
            var revised = GoalFeedbackEngine.ForSection(revision, revisedPasses[0], revisedPasses[1], run, run)!;
            GoalFeedbackEngine.RestoreAnswers(revised, f); Check(!revised.HasAnswers, "New section goal revision inherited answers");
            File.WriteAllText(Path.Combine(root, "section-feedback", "reviews", "broken.json"), "{broken");
            Check(store.LoadFeedback(f.ContextFingerprint, out var issue).Count == 1 && issue.Length > 0, "Corrupt history hid valid review");
        });
        test("feedback rejects malformed states and leaves legacy sessions without invented goals", () =>
        {
            var f = Form(); f.Questions[0].Answer = (FeedbackRating)99; Check(!GoalFeedbackEngine.Valid(f), "Unknown rating accepted");
            f = Form(); f.Questions.Add(f.Questions[0]); Check(!GoalFeedbackEngine.Valid(f), "Duplicate question accepted");
            var (a, b) = FeedbackFixture.Pair(); b.Session.Context = null; Check(GoalFeedbackEngine.ForRun(a, b) is null, "Legacy goal invented");
            Check(GoalFeedbackEngine.ForRun(a, a) is null && GoalFeedbackEngine.ForRun(null, a) is null, "Baseline absent ignored");
        });
    }
}
