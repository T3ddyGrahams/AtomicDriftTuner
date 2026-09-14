using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class GuidedModeChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("both offered guided modes provide instructions and the next milestone at every stage", () =>
        {
            Check(TuningFocusOptions.Current.Select(x => x.Focus).SequenceEqual(new[] { TuningFocus.CarSetupOnly, TuningFocus.Both }), "New work offered an unwanted third mode");
            foreach (var focus in TuningFocusOptions.Current.Select(x => x.Focus))
            {
                var p = new GuidedPreferences { Focus = focus }; var j = new GuidedJourney { Focus = focus };
                var stages = new HashSet<GuidedStage>();
                void Inspect()
                {
                    var s = GuidedWorkflowEngine.Next(p, j, "goal"); stages.Add(s.Stage);
                    Check(s.Instructions.Length > 30 && s.Details.Length > 50 && s.Completion.Length > 20 && s.Action.Length > 0, "Incomplete instructions: " + s.Stage);
                    Check(GuidedWorkflowEngine.ComingNext(s.Stage).StartsWith("Next:") && GuidedWorkflowEngine.Overview(focus).Contains("baseline"), "Next milestone or route missing");
                }
                Inspect(); p.Completed = true; p.FocusChoiceConfirmed = true; Inspect(); j.CarConfirmed = true; Inspect(); j.GoalSignature = "goal"; Inspect();
                var prepare = GuidedWorkflowEngine.Next(p, j, "goal");
                Check(focus == TuningFocus.CarSetupOnly ? prepare.Action.Contains("Confirm") : prepare.Action.Contains("Generate"), "Wrong preparation branch");
                j.TuneGenerated = true; Inspect(); j.TuneReady = true; Inspect(); j.BaselineId = "before"; Inspect();
                j.Recommendation = "one change"; Inspect(); j.AfterId = "after"; Inspect(); j.Reviewed = true; Inspect();
                Check(stages.Count == 9, "A stage was skipped");
                Check(GuidedWorkflowEngine.Next(p, j, "changed").Stage == GuidedStage.GoalsChanged, "Changed goals reused old progress");
            }
        });
        test("car-only guidance never requires live FFB even when prior live preferences are retained", () =>
        {
            foreach (var state in new IntegrationState?[] { null, new(false, false, false, false, false, false), new(true, true, true, true, true, true) })
            {
                var p = new GuidedPreferences { Focus = TuningFocus.CarSetupOnly, WantLiveConnection = true, SimHub = "Yes", Azom = "Yes" };
                var text = GuidedWorkflowEngine.Instructions(p, state);
                Check(text.Contains("not needed") && !text.Contains("Install / Repair") && !text.Contains("Apply explicitly"), "Optional FFB connection became a car-only requirement");
            }
        });
        test("FFB-only guidance distinguishes unchanged setup snapshots from tuning the car", () =>
        {
            var p = new GuidedPreferences { Focus = TuningFocus.FfbOnly, SimHub = "No", Azom = "No", ShowDetailedHelp = true };
            var text = GuidedWorkflowEngine.Instructions(p, null);
            Check(text.Contains("Controls") && text.Contains("Keep your existing car setup fixed") && !text.Contains("Generate Car Setup"), "FFB path instructed car tuning");
            Check(GuidedWorkflowEngine.RecordingHelp(p.Focus, false).Contains("only a snapshot"), "Setup attachment was not explained");
            p.SimHub = "Yes"; Check(GuidedWorkflowEngine.Instructions(p, null).Contains("SimHub by itself"), "SimHub-only explanation missing");
        });
        test("legacy workflow preferences and Both journey keep their original paths and progress", () =>
        {
            var folder = Path.Combine(root, "guided-mode-legacy"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "preferences.json"), "{\"Schema\":\"adt/guided-preferences/1\",\"Completed\":true,\"DriverName\":\"Tester\",\"SimHub\":\"Yes\",\"Azom\":\"No\"}");
            var store = new GuidedWorkflowStore(folder); var input = Input(); var driver = Guid.NewGuid().ToString("N");
            Check(store.Preferences().Focus == TuningFocus.Both && !store.Preferences().FocusChoiceConfirmed, "Legacy user skipped the new scope choice");
            store.Update(input, driver, j => { j.BaselineId = "original"; j.SetupPath = "baseline.ini"; });
            var original = Directory.GetFiles(folder).Single(x => !x.EndsWith("preferences.json"));
            var bytes = File.ReadAllBytes(original);
            foreach (var focus in new[] { TuningFocus.FfbOnly, TuningFocus.CarSetupOnly })
            {
                Check(store.Journey(input, driver, focus).BaselineId == "", "A historical mode inherited the baseline");
                store.Update(input, driver, j => { j.BaselineId = focus.ToString(); j.SetupPath = "keep.ini"; }, focus);
            }
            Check(File.ReadAllBytes(original).SequenceEqual(bytes), "Mode switching rewrote existing progress");
            Check(store.Journey(input, driver, TuningFocus.Both).BaselineId == "original", "Original progress did not resume");
            Check(store.Journey(input, driver, TuningFocus.FfbOnly).BaselineId == "FfbOnly", "FFB progress lost");
            var p = store.Preferences(); p.ShowDetailedHelp = false; store.SavePreferences(p);
            var reopened = new GuidedWorkflowStore(folder); Check(!reopened.Preferences().ShowDetailedHelp, "Explanation preference lost");
            reopened.Reset(input, driver);
            Check(reopened.Journey(input, driver).SetupPath == "baseline.ini" && reopened.Journey(input, driver, TuningFocus.FfbOnly).BaselineId == "FfbOnly", "Reset affected historical progress or setup path");
        });
        test("scope choices persist while legacy FFB-only reopens unconfirmed without rewriting history", () =>
        {
            var folder = Path.Combine(root, "guided-combined-restore"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "preferences.json");
            var store = new GuidedWorkflowStore(folder); var input = Input(); var driver = Guid.NewGuid().ToString("N");
            store.Update(input, driver, j => j.BaselineId = "combined");
            foreach (var focus in new[] { TuningFocus.FfbOnly, TuningFocus.CarSetupOnly })
            {
                var legacy = new GuidedPreferences { Focus = focus, Completed = true, FocusChoiceConfirmed = true, DriverName = "Keep driver", SimHub = "Yes", Azom = "No", ShowDetailedHelp = true };
                File.WriteAllText(path, JsonSerializer.Serialize(legacy));
                var bytes = File.ReadAllBytes(path);
                store.Update(input, driver, j => j.BaselineId = "historical", focus);
                var actual = store.Preferences();
                var expected = focus == TuningFocus.FfbOnly ? TuningFocus.Both : focus;
                Check(actual.Focus == expected && actual.FocusChoiceConfirmed == (focus != TuningFocus.FfbOnly) && actual.DriverName == legacy.DriverName && actual.Completed && actual.ShowDetailedHelp && actual.SimHub == "Yes" && actual.Azom == "No", "Reopening lost preferences or confirmed a legacy hidden mode");
                Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Reading preferences rewrote the original");
                Check(store.Journey(input, driver, TuningFocus.Both).BaselineId == "combined" && store.Journey(input, driver, focus).BaselineId == "historical", "Reopening lost or mixed progress");
                store.SavePreferences(legacy);
                var saved = JsonSerializer.Deserialize<GuidedPreferences>(File.ReadAllText(path))!;
                Check(saved.Focus == expected && saved.FocusChoiceConfirmed == (focus != TuningFocus.FfbOnly) && legacy.Focus == focus && legacy.FocusChoiceConfirmed, "Save restored a hidden mode, lost car-only, or mutated the caller");
            }
        });
        test("fresh and legacy users explicitly choose scope before any saved journey can advance", () =>
        {
            var folder = Path.Combine(root, "guided-explicit-choice"); var store = new GuidedWorkflowStore(folder);
            var fresh = store.Preferences();
            Check(!fresh.Completed && !fresh.FocusChoiceConfirmed && !fresh.ShowDetailedHelp, "Fresh users did not start with a simple unconfirmed workflow");
            var journey = new GuidedJourney { CarConfirmed = true, GoalSignature = "goal", BaselineId = "before", AfterId = "after", Reviewed = true };
            foreach (var focus in TuningFocusOptions.Current.Select(x => x.Focus))
            {
                var p = new GuidedPreferences { Completed = true, Focus = focus };
                Check(GuidedWorkflowEngine.Next(p, journey, "goal").Stage == GuidedStage.Welcome, "An old user bypassed scope selection");
                p.FocusChoiceConfirmed = true; p.ShowDetailedHelp = true; store.SavePreferences(p);
                var reopened = new GuidedWorkflowStore(folder).Preferences();
                Check(reopened.Focus == focus && reopened.FocusChoiceConfirmed && reopened.ShowDetailedHelp, "The explicit choice was not remembered");
                Check(GuidedWorkflowEngine.Next(reopened, journey, "goal").Stage == GuidedStage.Complete, "Confirming scope erased an existing journey");
            }
        });
        test("simple connection help names the next action for each detected state", () =>
        {
            var p = new GuidedPreferences { FocusChoiceConfirmed = true, SimHub = "Yes", Azom = "Yes", WantLiveConnection = true };
            foreach (var (state, expected) in new (IntegrationState?, string)[]
            {
                (null, "Check for Me"), (new(false, false, false, false, false, false), "Choose its folder"),
                (new(true, false, true, false, false, false), "Start SimHub"),
                (new(true, true, false, false, false, false), "Install / Repair"),
                (new(true, true, true, false, false, false), "Enable ADT's bridge"),
                (new(true, true, true, true, false, false), "AZOM was not detected"),
                (new(true, true, true, true, true, false), "not readable"),
                (new(true, true, true, true, true, true), "Apply explicitly")
            }) Check(GuidedWorkflowEngine.Instructions(p, state).Contains(expected), "Simple instructions omitted: " + expected);
        });
        test("late recording updates belong to their original mode after switching workflows", () =>
        {
            var store = new GuidedWorkflowStore(Path.Combine(root, "guided-mode-late")); var driver = Guid.NewGuid().ToString("N"); var input = Input();
            store.SavePreferences(new() { Focus = TuningFocus.CarSetupOnly });
            store.Update(input, driver, j => j.BaselineId = "ffb-run", TuningFocus.FfbOnly);
            Check(store.Journey(input, driver).BaselineId == "" && store.Journey(input, driver, TuningFocus.FfbOnly).BaselineId == "ffb-run", "Late run moved to selected mode");
        });
        test("focus filtering retains original diagnosis recommendations and calibration", () =>
        {
            var session = IntelligenceChecks.Session(); var analysis = new TelemetryAnalyzer().Analyze(session);
            analysis.CalibrationSuggestion.AcGainDelta = -2;
            var report = new TelemetryTuningAssistantEngine().Build(Input(), new(), new() { Session = session, Analysis = analysis });
            var original = JsonSerializer.Serialize(report);
            foreach (var focus in Enum.GetValues<TuningFocus>())
            {
                var shown = report.Recommendations.Where(x => TuningFocusOptions.Allows(focus, x)).ToList();
                if (focus == TuningFocus.Both) Check(shown.Count == report.Recommendations.Count, "Both mode dropped advice");
                if (focus == TuningFocus.FfbOnly) Check(shown.All(x => x.Area != RecommendationArea.CarSetup), "Car advice leaked into FFB-only");
                if (focus == TuningFocus.CarSetupOnly) Check(shown.All(x => x.Area != RecommendationArea.Ffb), "FFB advice leaked into car-only");
                Check(JsonSerializer.Serialize(report) == original, "Display filtering mutated the intelligence report");
            }
            Check(report.Assessments.Count > 5 && report.Recommendations.Any(x => x.Area == RecommendationArea.Ffb), "Full phase/FFB intelligence disappeared");
        });
        test("all telemetry metrics and tuning outputs are unchanged across workflow modes", () =>
        {
            var input = Input(); var session = IntelligenceChecks.Session(); var analyzer = new TelemetryAnalyzer();
            var before = JsonSerializer.Serialize(analyzer.Analyze(session));
            var tune = JsonSerializer.Serialize(new TuningEngine().Generate(input));
            foreach (var focus in Enum.GetValues<TuningFocus>())
            {
                var copy = RunHistoryStore.Clone(session); copy.Context!.Focus = copy.Context.Tune!.Focus = focus;
                Check(JsonSerializer.Serialize(analyzer.Analyze(copy)) == before, "Mode changed raw telemetry analysis");
                Check(JsonSerializer.Serialize(new TuningEngine().Generate(input)) == tune, "Mode changed generated tuning values");
            }
        });
        test("car-only history captures real setup values without claiming FFB targets were used", () =>
        {
            var store = new RunHistoryStore(Path.Combine(root, "guided-mode-snapshot")); var driver = store.GetOrCreateDriver("Tester");
            var setup = Path.Combine(root, "guided-mode-baseline.ini"); File.WriteAllText(setup, "[CAMBER_LF]\nVALUE=-30\n[FINAL_RATIO]\nVALUE=2\n");
            var car = store.CaptureTune(Input(), driver, "Car only", new(), null, setup, focus: TuningFocus.CarSetupOnly);
            Check(car.Settings.Count == 2 && car.Settings.Keys.All(x => x.StartsWith("ACSetup.")) && car.SetupSha256.Length > 0, "Snapshot claims out-of-scope generated targets");
            foreach (var focus in new[] { TuningFocus.Both, TuningFocus.FfbOnly })
            {
                var tune = store.CaptureTune(Input(), driver, "FFB", new(), null, setup, focus: focus);
                Check(tune.Settings.Keys.Any(x => x.StartsWith("Generated.ACFFB")) && tune.Settings.Keys.Any(x => x.StartsWith("Generated.AZOM")) && tune.Settings.ContainsKey("ACSetup.CAMBER_LF"), "FFB intelligence or unchanged car snapshot lost");
            }
        });
        test("comparisons keep measured evidence but reject a baseline from another tuning mode", () =>
        {
            var a = IntelligenceChecks.Session(); var b = RunHistoryStore.Clone(a); b.Id = Guid.NewGuid().ToString("N"); b.StartedUtc = a.StartedUtc.AddMinutes(5);
            b.Context!.Focus = b.Context.Tune!.Focus = TuningFocus.CarSetupOnly;
            var analyzer = new TelemetryAnalyzer(); var result = new RunComparisonEngine().Compare(new() { Session = a, Analysis = analyzer.Analyze(a) }, new() { Session = b, Analysis = analyzer.Analyze(b) });
            Check(!result.Comparable && result.Limitations.Any(x => x.Contains("Tuning mode changed")) && result.Metrics.Count > 5, "Cross-mode test claimed improvement or lost evidence");
        });
        test("saved next-action decisions preserve measured outcomes and earlier reviews", () =>
        {
            var store = new RunHistoryStore(Path.Combine(root, "guided-mode-review")); var input = Input(); var driver = store.GetOrCreateDriver("Reviewer");
            var session = Guid.NewGuid().ToString("N");
            foreach (var decision in new[] { "Keep and verify", "Revert manually", "Test again" })
                store.SaveReview(new() { SessionId = session, DriverId = driver.Id, ContextKey = RunHistoryStore.ContextKey(input), Focus = TuningFocus.CarSetupOnly,
                    NextAction = decision, DriverRating = "Better", Comparison = new() { Verdict = "Inconclusive" } });
            var reviews = store.ListReviews(input, driver.Id);
            Check(reviews.Count == 3 && reviews.Select(x => x.NextAction).Distinct().Count() == 3 && reviews.All(x => x.Comparison.Verdict == "Inconclusive"), "Decision overwrote history or measured outcome");
        });
        test("invalid modes and corrupted preferences are rejected without overwriting stored work", () =>
        {
            var store = new GuidedWorkflowStore(Path.Combine(root, "guided-mode-invalid"));
            Refuse(() => store.SavePreferences(new() { Focus = (TuningFocus)99 }));
            store.SavePreferences(new()); var path = Path.Combine(root, "guided-mode-invalid", "preferences.json"); File.WriteAllText(path, "broken");
            Refuse(() => store.SavePreferences(new())); Check(File.ReadAllText(path) == "broken", "Corrupted preferences were replaced");
        });
    }
    private static TuneInput Input() => new() { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refuse(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { return; } throw new Exception("Invalid data was accepted"); }
}
