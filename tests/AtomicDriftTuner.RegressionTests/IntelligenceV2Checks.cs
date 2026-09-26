using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using System.IO;
using System.Reflection;
using System.Text.Json;

internal static class IntelligenceV2Checks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test, string root)
    {
        test("intelligence 2.0 does not credit unrelated changes to free-text recommendations", () => {
            var (a, b) = IntelligenceChecks.Pair();
            a.Session.Context!.Tune!.Settings["ACSetup.FINAL_RATIO"] = 1;
            b.Session.Context!.Tune!.Settings["ACSetup.DAMP_REBOUND_LR"] = 7;
            b.Session.Context.Tune.Settings["ACSetup.FINAL_RATIO"] = 2;
            Check(!new RunComparisonEngine().Compare(a, b).RecommendationTestTracked, "Unrelated gearing was credited to the rebound test.");
        });
        test("intelligence 2.0 metadata-only setup changes cannot qualify as a tune test", () => {
            var (a, b) = IntelligenceChecks.Pair();
            b.Session.Context!.Tune!.Settings = RunHistoryStore.Clone(a.Session.Context!.Tune!.Settings);
            b.Session.Context.Tune.SetupSha256 = "different-file-with-identical-values";
            var outcome = new RunComparisonEngine().Compare(a, b);
            Check(outcome.Comparable && !outcome.RecommendationTestTracked, "File-only change credited as a tune change.");
            Check(outcome.TuneChanges.Any(x => x.Metric.Contains("fingerprint")), "Lost useful provenance display.");
        });
        test("intelligence 2.0 manual coverage changes remain descriptive", () => {
            foreach (var added in new[] { false, true }) {
                var (a, b) = IntelligenceChecks.Pair();
                (added ? b : a).Session.Context!.Tune!.Settings["ACSetup.PRESSURE_LF"] = 28;
                var outcome = new RunComparisonEngine().Compare(a, b);
                Check(!outcome.Comparable && !outcome.RecommendationTestTracked && outcome.TuneChanges.Count > 0, "Coverage mismatch counted as a change in game.");
            }
        });
        test("intelligence 2.0 known backward travel never supplies drift phase pedal or gain evidence", () => {
            foreach (var goal in Enum.GetValues<SustainedAnglePreference>()) {
                var s = IntelligenceChecks.Session(); s.Context!.Tune!.DesiredBehavior.SustainedAngle = goal;
                foreach (var f in s.Samples) { f.LongitudinalVelocityMs = -14; f.FinalFfb = .99; }
                var a = new TelemetryAnalyzer().Analyze(s);
                Check(a.DriftTimeSeconds == 0 && a.Diagnosis.Pedals.DriftSeconds == 0 && a.CalibrationSuggestion.IsNeutral &&
                    a.DriftEntries == 0 && a.TransitionCount == 0, "Known backward motion counted as clean driving under " + goal);
            }
        });
        test("intelligence 2.0 matches an exact ADT test and its intended goal", () => {
            var (a, b) = PlannedPair(); var original = JsonSerializer.Serialize(a);
            var outcome = new RunComparisonEngine().Compare(a, b);
            Check(outcome.RecommendationTestTracked && outcome.TestGoalImproved && outcome.TestId == b.Session.Context!.Test!.Id && outcome.ActualSettingChanges == 1,
                "Exact test not tracked: " + string.Join("; ", outcome.Limitations));
            Check(new RunReview { DriverRating = "Better", Comparison = outcome }.Conclusion.Contains("support improvement"), "Correct test lost positive association.");
            Check(original == JsonSerializer.Serialize(a), "Comparison mutated its baseline.");
            b.Analysis.Diagnosis.Metric("transition")!.Value = 1.6;
            b.Analysis.Diagnosis.Metric("stability")!.Value = 1;
            a.Analysis.Diagnosis.Metric("stability")!.Value = 10;
            outcome = new RunComparisonEngine().Compare(a, b);
            Check(outcome.Verdict == "Closer to goals" && !outcome.TestGoalImproved &&
                !new RunReview { DriverRating = "Better", Comparison = outcome }.Conclusion.Contains("support improvement"), "An unrelated improvement validated the intended goal.");
        });
        test("intelligence 2.0 rejects wrong direction amount baseline and extra changes", () => {
            foreach (var fault in new[] { "control", "direction", "amount", "extra", "session", "tune", "fingerprint", "driver", "goal", "no-change", "malformed", "unknown-source", "duplicate" }) {
                var (a, b) = PlannedPair(); var testPlan = b.Session.Context!.Test!; var settings = b.Session.Context.Tune!.Settings;
                switch (fault) {
                    case "control": a.Session.Context!.Tune!.Settings["ACSetup.FINAL_RATIO"] = 1; settings["ACSetup.FINAL_RATIO"] = 2; settings["ACSetup.DAMP_REBOUND_LR"] = 7; break;
                    case "direction": settings["ACSetup.DAMP_REBOUND_LR"] = 6; break;
                    case "amount": settings["ACSetup.DAMP_REBOUND_LR"] = 9; break;
                    case "extra": a.Session.Context!.Tune!.Settings["ACSetup.FUEL"] = 30; settings["ACSetup.FUEL"] = 20; testPlan.BaselineSettingsFingerprint = RecommendationTestService.Fingerprint(a.Session.Context.Tune); break;
                    case "session": testPlan.BaselineSessionId = Guid.NewGuid().ToString("N"); break;
                    case "tune": testPlan.BaselineTuneId = Guid.NewGuid().ToString("N"); break;
                    case "fingerprint": testPlan.BaselineSettingsFingerprint = new string('0', 64); break;
                    case "driver": testPlan.DriverId = Guid.NewGuid().ToString("N"); break;
                    case "goal": testPlan.GoalSignature = "different"; break;
                    case "no-change": settings["ACSetup.DAMP_REBOUND_LR"] = 7; break;
                    case "malformed": testPlan.Changes = null!; break;
                    case "unknown-source": a.Session.Context!.Tune!.SetupSource = b.Session.Context.Tune.SetupSource = ""; break;
                    case "duplicate": a.Session.Context!.Tune!.Settings["ACSetup.damp_rebound_lr"] = 7; settings["ACSetup.damp_rebound_lr"] = 7; testPlan.BaselineSettingsFingerprint = RecommendationTestService.Fingerprint(a.Session.Context.Tune); break;
                }
                var outcome = new RunComparisonEngine().Compare(a, b);
                Check(!outcome.RecommendationTestTracked && !outcome.DriverTestTracked && outcome.Metrics.Count > 0, fault + " retained credit or erased measurements.");
            }
        });
        test("intelligence 2.0 manual and CSP matching ignore metadata but preserve coverage gates", () => {
            foreach (var source in new[] { "manual-file", "csp-current-setup" }) {
                var (a, b) = PlannedPair(); a.Session.Context!.Tune!.SetupSource = b.Session.Context!.Tune!.SetupSource = source;
                b.Session.Context.Tune.SetupSha256 = "metadata-changed-as-well-as-real-value";
                var outcome = new RunComparisonEngine().Compare(a, b);
                Check(outcome.RecommendationTestTracked && outcome.ActualSettingChanges == 1, "A metadata change erased a real test.");
                b.Session.Context.Tune.SetupSource = source == "manual-file" ? "csp-current-setup" : "manual-file";
                Check(!new RunComparisonEngine().Compare(a, b).RecommendationTestTracked, "Mixed capture methods credited.");
            }
        });
        test("intelligence 2.0 custom car tests are explicit and never ADT recommendation credit", () => {
            var (a, b) = PlannedPair(); var current = b.Session.Context!.Tune!;
            var own = RecommendationTestService.ForRecording(null, a, current, "Try my own rebound setting", true)!;
            b.Session.Context.Test = own;
            var outcome = new RunComparisonEngine().Compare(a, b);
            Check(outcome.DriverTestTracked && !outcome.RecommendationTestTracked && outcome.TestOrigin == RecommendationTestService.Driver,
                "Custom test was either lost or credited to ADT.");
            Check(new RunReview { DriverRating = "Better", Comparison = outcome }.Conclusion.Contains("driver-defined"), "Custom origin disappeared from the review.");
            Check(RecommendationTestService.ForRecording(null, a, current, own.Description, false) is null, "Free text silently became a contract.");
            current.Settings["Generated.ACFFB.Gain"] = 80;
            bool rejected = false; try { RecommendationTestService.CreateDriverTest(a, current, "Mixed changes"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Custom car test admitted changed/unknown FFB coverage.");
        });
        test("intelligence 2.0 recording plan cannot follow a different note or baseline", () => {
            var (a, b) = PlannedPair(); var plan = b.Session.Context!.Test!;
            var captured = RecommendationTestService.ForRecording(plan, a, b.Session.Context.Tune!, plan.Description, false)!;
            Check(captured.Id == plan.Id && !ReferenceEquals(captured, plan), "Recorder did not freeze the plan.");
            captured.Changes.Clear(); Check(plan.Changes.Count == 1, "Recorder changed the prepared plan.");
            Check(RecommendationTestService.ForRecording(plan, a, b.Session.Context.Tune!, "Test gearing instead", false) is null &&
                RecommendationTestService.ForRecording(plan, b, b.Session.Context.Tune!, plan.Description, false) is null,
                "Edited plan retained an unrelated recommendation identity.");
        });
        test("intelligence 2.0 focused export records the same exact controls as the saved file", () => {
            var f = new CarTestFixture(root); var choice = f.Build().Single(); var changed = RunHistoryStore.Clone(f.Run.Session.Context!.Tune!);
            var output = f.Service.Save(f.Run, choice, Path.Combine(f.DirectoryPath, "Structured.ini"));
            foreach (var p in new AssettoCorsaSetupService().LoadBaseline(output, f.Input.Car).Parameters)
                changed.Settings["ACSetup." + p.Section] = p.CurrentValue!.Value;
            Check(choice.Test.Changes.Count == 2 && choice.Test.MetricKey == "rear-slip-share" &&
                RecommendationTestService.Mismatch(choice.Test, f.Run, changed) == "", "Export and exact plan diverged.");
        });
        test("intelligence 2.0 plans reviews and analysis versions survive reopening", () => {
            var (a, b) = PlannedPair(); var input = new CarTestFixture(root).Input;
            var store = new TelemetrySessionStore();
            typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, Path.Combine(root, "v2-sessions"));
            var saved = store.Save(b.Session, b.Analysis); var loaded = store.TryLoad(saved.JsonPath)!;
            Check(loaded.Session.Context!.Test!.Id == b.Session.Context!.Test!.Id && loaded.Analysis.Diagnosis.AnalyzerVersion == "drift-diagnosis/4", "Reload lost test or used obsolete analyzer.");
            var history = new RunHistoryStore(Path.Combine(root, "v2-reviews"));
            var review = new RunReview { SessionId = b.Session.Id, BaselineSessionId = a.Session.Id, DriverId = b.Session.Context.DriverId,
                ContextKey = RunHistoryStore.ContextKey(input), DriverRating = "Better", Comparison = new RunComparisonEngine().Compare(a, b) };
            history.SaveReview(review);
            var reloaded = history.ListReviews(input, review.DriverId).Single();
            Check(reloaded.Comparison.ComparisonVersion == "run-comparison/2" && reloaded.Comparison.TestId == b.Session.Context.Test.Id &&
                reloaded.Comparison.BeforeAnalyzerVersion == "drift-diagnosis/4", "Saved review lost its provenance.");
            var workflow = new GuidedWorkflowStore(Path.Combine(root, "v2-guide"));
            workflow.Update(input, review.DriverId, j => j.Test = b.Session.Context.Test);
            Check(workflow.Journey(input, review.DriverId).Test!.Id == b.Session.Context.Test.Id, "Journey lost plan on reopen.");
            workflow.Reset(input, review.DriverId); Check(workflow.Journey(input, review.DriverId).Test is null, "Fresh baseline retained old test.");
        });
        test("intelligence 2.0 forward and unknown direction observations stay stable", () => {
            var legacy = IntelligenceChecks.Session(); var forward = RunHistoryStore.Clone(legacy);
            foreach (var f in forward.Samples) f.LongitudinalVelocityMs = 14;
            var a = new TelemetryAnalyzer().Analyze(legacy); var b = new TelemetryAnalyzer().Analyze(forward);
            Check(a.DriftTimeSeconds == b.DriftTimeSeconds && a.TransitionCount == b.TransitionCount && a.DriftEntries == b.DriftEntries &&
                a.Diagnosis.Pedals.DriftSeconds == b.Diagnosis.Pedals.DriftSeconds, "Forward or legacy observations unexpectedly changed.");
            Check(a.Diagnosis.QualityNotes.Any(n => n.Contains("direction is unknown")) && !b.Diagnosis.QualityNotes.Any(n => n.Contains("direction is unknown")), "Unknown direction was invented.");
        });
        test("intelligence 2.0 brief backwards segments break phases and never authorize a gain correction", () => {
            var s = IntelligenceChecks.Session();
            foreach (var f in s.Samples) { f.LongitudinalVelocityMs = f.TimeSeconds % 25 is >= 5 and <= 6 ? -14 : 14; f.FinalFfb = f.LongitudinalVelocityMs < 0 ? 1 : .2; }
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.DriftEntries == 0 && a.Diagnosis.BackwardTravelSeconds > 3.9 && a.CalibrationSuggestion.IsNeutral && a.FfbClippingPctWhileDrifting == 0,
                "An entry or clipping evidence crossed known backward travel.");
        });
    }
    static (SavedTelemetrySession, SavedTelemetrySession) PlannedPair()
    {
        var (a, b) = IntelligenceChecks.Pair();
        b.Session.Context!.Test = RecommendationTestService.Create(a, "Test a quicker transition", "transition", [new("ACSetup.DAMP_REBOUND_LR", 7, 8)]);
        a.Analysis.Diagnosis.Metric("transition")!.Value = 1.6; b.Analysis.Diagnosis.Metric("transition")!.Value = .55;
        return (a, b);
    }
}
