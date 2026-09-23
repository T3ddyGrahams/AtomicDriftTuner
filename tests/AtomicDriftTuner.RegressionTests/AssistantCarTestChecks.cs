using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class AssistantCarTestChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("assistant car test isolates one axle adjustment and preserves baseline, goals and diagnosis", () =>
        {
            var f = new CarTestFixture(root); var original = File.ReadAllText(f.Baseline);
            var run = JsonSerializer.Serialize(f.Run); var report = JsonSerializer.Serialize(f.Report); var input = JsonSerializer.Serialize(f.Input);
            Check(f.Report.NextStep.Recommendation?.MetricKey == "rear-slip-share", "Stable metric identity missing");
            var choice = f.Build().Single();
            Check(choice.Name == "Rear tyre pressures" && choice.Changes.Contains("rear left") && choice.Changes.Contains("28 psi → 26 psi"), "Exact human-readable values missing: " + choice.Changes);
            PitSetupPlan? staged = null;
            f.Service.Stage(f.Run, choice, (a, label) => { staged = new PitSetupPlanService().Create(a, f.Input.Car.SourceFolderName!, label); return "ready"; });
            Check(staged!.Changes.Count == 2 && staged.Changes.All(c => c.Section is "PRESSURE_LR" or "PRESSURE_RR" && c.Before == 28 && c.After == 26), "Full generated tune leaked into focused test");
            Check(staged.BaselineValues.Count == 6 && staged.BaselineValues["ECU_MAP"] == 2, "Unchanged values absent");
            var saved = f.Service.Save(f.Run, choice, Path.Combine(f.DirectoryPath, "Test.ini"));
            var values = new AssettoCorsaSetupService().LoadBaseline(saved, f.Input.Car).Parameters.ToDictionary(p => p.Section, p => p.CurrentValue);
            Check(values["PRESSURE_LR"] == 26 && values["PRESSURE_RR"] == 26 && values["PRESSURE_LF"] == 28 && values["PRESSURE_RF"] == 28 && values["FUEL"] == 30 && values["ECU_MAP"] == 2, "Saved file changed unrelated values");
            Check(File.ReadAllText(f.Baseline) == original && File.ReadAllText(saved).Contains("preserve driver metadata"), "Baseline or metadata lost");
            Check(run == JsonSerializer.Serialize(f.Run) && report == JsonSerializer.Serialize(f.Report) && input == JsonSerializer.Serialize(f.Input), "Test changed the captured intelligence or goals");
            Check(choice.TestDescription.Contains("PRESSURE_LR 28 → 26") && choice.TestDescription.Contains("PRESSURE_RR 28 → 26"), "History is not exact");
            Reject(() => f.Service.Save(f.Run, choice, f.Baseline));
        });
        test("assistant car test refuses unsupported display mappings, missing paired controls and no-op limits", () =>
        {
            foreach (var mode in new[] { "1", "2", "garbage" }) Reject(() => new CarTestFixture(root, mode).Build());
            Reject(() => new CarTestFixture(root, includePair: false).Build());
            Reject(() => new CarTestFixture(root, pressure: 10).Build());
        });
        test("assistant car test rejects mismatched files and stale baseline or car definitions before save and stage", () =>
        {
            var f = new CarTestFixture(root); var choice = f.Build().Single();
            var wrong = Path.Combine(f.DirectoryPath, "Wrong.ini");
            File.WriteAllText(wrong, File.ReadAllText(f.Baseline).Replace("VALUE=30", "VALUE=31"));
            Reject(() => f.Service.Build(f.Input, f.Run, f.Report, wrong));
            File.WriteAllText(wrong, File.ReadAllText(f.Baseline).Replace("MODEL=isolated_car_test", "MODEL=other_car"));
            Reject(() => f.Service.Build(f.Input, f.Run, f.Report, wrong));
            foreach (var physics in new[] { false, true })
            {
                var x = new CarTestFixture(root); var candidate = x.Build().Single(); var called = false;
                if (physics) File.AppendAllText(x.Definitions, "\n; changed car definition");
                else File.WriteAllText(x.Baseline, File.ReadAllText(x.Baseline).Replace("VALUE=30", "VALUE=31"));
                Reject(() => x.Service.Stage(x.Run, candidate, (a, l) => { called = true; return "bad"; }));
                var output = Path.Combine(x.DirectoryPath, "MustNotExist.ini"); Reject(() => x.Service.Save(x.Run, candidate, output));
                Check(!called && !File.Exists(output), "Stale test reached mutation");
            }
        });
        test("assistant car test honors next-step priority, confidence and recorded context", () =>
        {
            var f = new CarTestFixture(root);
            foreach (var mutate in new Action<SavedTelemetrySession>[] {
                s => s.Session.Context!.TuneConfirmedInUse = false, s => s.Session.Context!.CarIdentityVerified = false,
                s => s.Session.Context!.Interrupted = true, s => s.Session.Context!.SetupCaptureIssue = "lost",
                s => s.Session.Context!.Tune!.HasUnassignedSetupValues = true, s => s.Session.Context!.Tune!.BasePhysicsFingerprint = "",
                s => s.Session.Context!.Tune!.ContextKey = "another", s => s.Session.Context = null })
            {
                var run = RunHistoryStore.Clone(f.Run); mutate(run);
                Check(AssistantCarTestService.UnavailableReason(f.Input, run, f.Report).Length > 0, "Unverified run accepted");
            }
            foreach (var action in new[] { "Compare", "Review", "Evidence", "" })
            { var r = Snapshot(f.Report); r.NextStep.Action = action; Check(AssistantCarTestService.UnavailableReason(f.Input, f.Run, r).Length > 0, "Next step bypassed"); }
            foreach (var area in new[] { RecommendationArea.General, RecommendationArea.Ffb })
            { var r = Snapshot(f.Report); r.NextStep.Recommendation!.Area = area; Check(AssistantCarTestService.UnavailableReason(f.Input, f.Run, r).Length > 0, "Non-car recommendation converted"); }
            var low = Snapshot(f.Report); low.OverallConfidence = AssistantConfidence.Low;
            Check(AssistantCarTestService.UnavailableReason(f.Input, f.Run, low).Length > 0, "Low-quality report allowed");
            var changedGoal = new DriftAssistantReportBuilder().Build(f.Input, new CarBehaviorTarget { RearGrip = -2 }, f.Run, null);
            Check(AssistantCarTestService.UnavailableReason(f.Input, f.Run, changedGoal).Length > 0, "Changed goals reused old run");
        });
        test("assistant car test maps front response, initiation, transitions and camber without widening an experiment", () =>
        {
            foreach (var key in new[] { "front-slip-share", "initiation", "transition", "rear-slip-share" })
            {
                var f = new CarTestFixture(root);
                var sections = new[] { "TOE_OUT_LF", "TOE_OUT_RF", "DAMP_REBOUND_LR", "DAMP_REBOUND_RR", "CAMBER_LR", "CAMBER_RR" };
                foreach (var section in sections)
                {
                    var camber = section.StartsWith("CAMBER");
                    File.AppendAllText(f.Definitions, $"\n[{section}]\nMIN={(camber ? -10 : 0)}\nMAX={(camber ? -2 : 30)}\nSTEP=1\nSHOW_CLICKS={(camber ? 1 : 0)}\n");
                    File.AppendAllText(f.Baseline, $"\n[{section}]\nVALUE={(camber ? -55 : 10)}\n");
                }
                var c = f.Run.Session.Context!; var goal = new CarBehaviorTarget { RearGrip = 2, FrontEndBite = 2, InitiationSharpness = 2, TransitionSpeed = 2 };
                c.Tune = new RunHistoryStore(Path.Combine(f.DirectoryPath, "history")).CaptureTune(f.Input,
                    new DriverIdentity { Id = c.DriverId, Name = c.DriverName }, "Mapped controls", goal, null, f.Baseline,
                    focus: c.Focus, gearingTargets: new GearingTargetStore(Path.Combine(f.DirectoryPath, "gearing")));
                var metric = f.Run.Analysis.Diagnosis.Metrics.Single(); metric.Key = key; metric.Value = key.Contains("share") ? 75 : 2;
                var report = new DriftAssistantReportBuilder().Build(f.Input, goal, f.Run, null);
                Check(report.NextStep.Action == "Plan" && report.NextStep.Recommendation?.Area == RecommendationArea.CarSetup,
                    key + " fixture has no car test: " + report.NextStep.Noticed + " / " + report.NextStep.Instruction + " / spin=" + f.Run.Analysis.SpinEvents);
                var choices = f.Service.Build(f.Input, f.Run, report, f.Baseline);
                var expected = key switch { "front-slip-share" or "initiation" => "Front toe", "transition" => "Rear rebound damping", _ => "Rear camber" };
                var selected = choices.Single(x => x.Name == expected);
                f.Service.Stage(f.Run, selected, (analysis, label) =>
                {
                    var plan = new PitSetupPlanService().Create(analysis, f.Input.Car.SourceFolderName!, label);
                    Check(plan.Changes.Count == 2 && plan.Changes.All(p => expected switch {
                        "Front toe" => p.Section.StartsWith("TOE_OUT"), "Rear rebound damping" => p.Section.StartsWith("DAMP_REBOUND"), _ => p.Section.StartsWith("CAMBER") }), "An unrelated group leaked into the test");
                    return "ready";
                });
                if (key == "transition") Check(selected.Explanation.Contains("quicker"), "At-limit goal incorrectly described a slow-down");
                if (key == "initiation") Check(selected.Explanation.Contains("sharper"), "At-limit goal incorrectly described a slow-down");
                if (key == "rear-slip-share") Check(selected.Changes.Contains("-5.5 setup units") && selected.Details.Contains("VALUE -55"), "Camber display and raw saved values conflated");
            }
        });
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static TuningAssistantReport Snapshot(TuningAssistantReport report)
    { var copy = RunHistoryStore.Clone(report); copy.NextStep.Recommendation = RunHistoryStore.Clone(report.NextStep.Recommendation); return copy; }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { return; }
        throw new Exception("Unsafe or unsupported test was accepted");
    }
}
