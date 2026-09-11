using System.IO;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class GuidedChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("guided workflow follows the full baseline test review sequence", () =>
        {
            var p = new GuidedPreferences(); var j = new GuidedJourney(); var goal = "0/0/0/0/0/0/0";
            void At(GuidedStage stage) => Check(GuidedWorkflowEngine.Next(p, j, goal).Stage == stage, "Unexpected stage: " + GuidedWorkflowEngine.Next(p, j, goal).Title);
            At(GuidedStage.Welcome); p.Completed = true; At(GuidedStage.Car);
            j.CarConfirmed = true; At(GuidedStage.Goals); j.GoalSignature = goal; At(GuidedStage.Prepare);
            j.TuneGenerated = true; At(GuidedStage.Prepare); // Generated must not imply applied/ready.
            j.TuneReady = true; At(GuidedStage.Baseline); j.BaselineId = Guid.NewGuid().ToString("N"); At(GuidedStage.Findings);
            j.Recommendation = "One controlled change"; At(GuidedStage.Test);
            j.AfterId = Guid.NewGuid().ToString("N"); At(GuidedStage.Compare); j.Reviewed = true; At(GuidedStage.Complete);
            Check(GuidedWorkflowEngine.Next(p, j, goal).Instructions.Contains("does not mean the tune improved"), "Navigation completion was treated as tuning success");
        });
        test("changed desired behavior requires a new baseline even after a completed review", () =>
        {
            var j = new GuidedJourney { CarConfirmed = true, GoalSignature = "old", TuneReady = true, BaselineId = "before", AfterId = "after", Reviewed = true };
            Check(GuidedWorkflowEngine.Next(new() { Completed = true }, j, "new").Stage == GuidedStage.GoalsChanged, "Changed goals reused completed progress");
        });
        test("manual workflow avoids requiring SimHub or AZOM", () =>
        {
            foreach (var p in new[] { new GuidedPreferences(), new() { SimHub = "No", WantLiveConnection = true }, new() { Azom = "No", WantLiveConnection = true } })
            {
                var text = GuidedWorkflowEngine.Instructions(p, new(false, false, false, false, false, false));
                Check(text.Contains("optional") && !text.Contains("Install / Repair"), "Optional software became a required blocker");
            }
        });
        test("installed but closed SimHub leaves AZOM availability unknown", () =>
        {
            var text = GuidedWorkflowEngine.Instructions(new() { SimHub = "Yes", Azom = "Yes", WantLiveConnection = true }, new(true, false, true, false, false, false));
            Check(text.Contains("installed but is not running") && text.Contains("unknown"), "Offline was mistaken for uninstalled");
        });
        test("live guidance distinguishes missing bridge AZOM and unreadable settings", () =>
        {
            var p = new GuidedPreferences { WantLiveConnection = true };
            Check(GuidedWorkflowEngine.Instructions(p, new(true, true, false, false, false, false)).Contains("Install / Repair"), "Missing bridge step absent");
            Check(GuidedWorkflowEngine.Instructions(p, new(true, true, true, true, false, false)).Contains("did not detect AZOM"), "AZOM detection not distinguished");
            Check(GuidedWorkflowEngine.Instructions(p, new(true, true, true, true, true, false)).Contains("not readable"), "Unreadable settings marked ready");
            Check(GuidedWorkflowEngine.Instructions(p, new(true, true, true, true, true, true)).Contains("Apply explicitly"), "Ready instructions imply automatic application");
        });
        test("guided preferences and progress survive reopening with driver car intent isolation", () =>
        {
            var dir = Path.Combine(root, "guided-workflow"); var store = new GuidedWorkflowStore(dir); var input = Input(); var driver = Guid.NewGuid().ToString("N");
            store.SavePreferences(new() { Completed = true, DriverName = "Tester", SimHub = "No", Azom = "Not sure" });
            store.Update(input, driver, j => { j.CarConfirmed = true; j.BaselineId = "before"; j.Recommendation = "test one"; j.Conditions = "dry solo"; });
            var reopened = new GuidedWorkflowStore(dir);
            Check(reopened.Preferences().DriverName == "Tester" && reopened.Journey(input, driver).Recommendation == "test one", "Workflow did not persist");
            Check(reopened.Journey(input, Guid.NewGuid().ToString("N")).BaselineId == "", "Different driver inherited progress");
            input.Intent = BuiltInProfiles.Intents().First(i => i.Kind != input.Intent.Kind);
            Check(reopened.Journey(input, driver).BaselineId == "", "Different intent inherited progress");
            input = Input(); input.Car.SourceFolderName = "another-car";
            Check(reopened.Journey(input, driver).BaselineId == "", "Different car inherited progress");
        });
        test("resetting a guided baseline retains unrelated driver progress and setup path", () =>
        {
            var store = new GuidedWorkflowStore(Path.Combine(root, "guided-reset")); var input = Input(); var a = Guid.NewGuid().ToString("N"); var b = Guid.NewGuid().ToString("N");
            store.Update(input, a, j => { j.BaselineId = "before"; j.AfterId = "after"; j.SetupPath = "saved.ini"; j.Reviewed = true; });
            store.Update(input, b, j => j.BaselineId = "other"); store.Reset(input, a);
            Check(store.Journey(input, a).BaselineId == "" && store.Journey(input, a).SetupPath == "saved.ini" && !store.Journey(input, a).Reviewed, "Reset left stale progress or lost setup selection");
            Check(store.Journey(input, b).BaselineId == "other", "Reset affected another driver");
        });
        test("damaged guided preferences are preserved instead of silently reset", () =>
        {
            var dir = Path.Combine(root, "guided-corruption"); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "preferences.json"); File.WriteAllText(path, "{");
            bool failed = false; try { new GuidedWorkflowStore(dir).Preferences(); } catch { failed = true; }
            Check(failed && File.ReadAllText(path) == "{", "Corrupt preferences were overwritten");
        });
        test("generated tune signature changes when recommendations change", () =>
        {
            var result = new TuningEngine().Generate(Input());
            var before = GuidedWorkflowEngine.TuneSignature(result);
            var same = GuidedWorkflowEngine.TuneSignature(result);
            result.Azom.Core.WheelRotationAngleDeg += 10;
            Check(before == same && before != GuidedWorkflowEngine.TuneSignature(result), "A changed generated tune retained the ready signature");
        });
        test("saved short interrupted and frozen runs do not advance the guided workflow", () =>
        {
            var session = IntelligenceChecks.Session(); var analyzer = new TelemetryAnalyzer();
            Check(GuidedWorkflowEngine.CanAdvanceFromRun(session, analyzer.Analyze(session)), "Clean baseline did not advance");
            session.Context!.Interrupted = true;
            Check(!GuidedWorkflowEngine.CanAdvanceFromRun(session, analyzer.Analyze(session)), "Interrupted run advanced");
            session = IntelligenceChecks.Session(); session.Samples = session.Samples.Take(300).ToList();
            Check(!GuidedWorkflowEngine.CanAdvanceFromRun(session, analyzer.Analyze(session)), "Short run advanced");
            session = IntelligenceChecks.Session(); foreach (var s in session.Samples) s.PacketId = 7;
            Check(!GuidedWorkflowEngine.CanAdvanceFromRun(session, analyzer.Analyze(session)), "Frozen run advanced");
        });
    }
    private static TuneInput Input() => new() { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
