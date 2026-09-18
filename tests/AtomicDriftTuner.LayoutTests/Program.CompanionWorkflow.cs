using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckCompanionWorkflow(string output)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Companion workflow: " + why); checks++; }
        static object Get(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
        static void Set(object o, string name, object? value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        var directory = Path.Combine(output, "companion-workflow-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var settings = new AppSettingsStore();
        Set(settings, "_directory", directory); Set(settings, "_path", Path.Combine(directory, "settings.json")); Set(settings, "_backupPath", Path.Combine(directory, "settings.backup.json"));
        var main = (MainWindow)Activator.CreateInstance(typeof(MainWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [settings, false], null)!;
        var history = new RunHistoryStore(Path.Combine(directory, "history")); Set(main, "_companionWorkflowHistory", history);
        var workflow = new GuidedWorkflowStore(Path.Combine(directory, "workflow")); Set(main, "_workflow", workflow);
        var behavior = (CarBehaviorProfileStore)Get(main, "_behaviorStore");
        Set(behavior, "_directory", directory); Set(behavior, "_path", Path.Combine(directory, "goals.json"));
        var sessions = (TelemetrySessionStore)Get(main, "_companionWorkflowSessions");
        Set(sessions, "<RootDirectory>k__BackingField", Path.Combine(directory, "sessions"));
        CompanionWorkflowState State() => (CompanionWorkflowState)Call(main, "BuildCompanionWorkflowState")!;
        RemoteActionResponse Send(CompanionWorkflowCommand command) => (RemoteActionResponse)Call(main, "ExecuteCompanionWorkflowCommand", command)!;
        CompanionWorkflowCommand Command(string action) => new() { Action = action, ControlVersion = State().ControlVersion, SessionId = State().SavedSessionId };
        TelemetryWindow? recorder = null;
        using var hub = new TelemetryHubService(); hub.Dispose();
        try
        {
            Check(!State().Available && !Send(Command("prepare")).Ok, "fresh incomplete workflow could prepare a run");
            workflow.SavePreferences(new() { Completed = true, FocusChoiceConfirmed = true, DriverName = "Companion fixture", Focus = TuningFocus.Both });
            foreach (var name in new[] { "HardwareBox", "WheelBox", "PackBox", "CarBox", "IntentBox" }) ((ComboBox)main.FindName(name)).SelectedIndex = 0;
            var car = (CarProfile)((ComboBox)main.FindName("CarBox")).SelectedItem; car.IsInstalled = true; car.SourceFolderName = "companion_workflow_fixture";
            var input = (TuneInput)Call(main, "BuildInput")!;
            var driver = history.GetOrCreateDriver("Companion fixture");
            var setup = Path.Combine(directory, "baseline.ini"); File.WriteAllText(setup, "[CAMBER_LF]\nVALUE=-30\n");
            workflow.Update(input, driver.Id, j => { j.CarConfirmed = j.TuneReady = true; j.GoalSignature = GuidedWorkflowStore.GoalSignature(new()); j.SetupPath = setup; j.Conditions = "dry, layout A, transitions"; });
            Call(main, "CompanionWorkflowContextNow"); // Surface context errors rather than only the safe user-facing message.
            var unpreparedState = State();
            Check(unpreparedState.Available && !unpreparedState.CanPrepare, "workflow without a recorder did not require desktop preparation: " + unpreparedState.Message);
            Check(State().ControlVersion == State().ControlVersion, "unchanged status polls invalidated the command version");
            recorder = new TelemetryWindow(input, hub);
            ((CheckBox)recorder.FindName("UseAutomaticSetupCheck")).IsChecked = false; // Exercise the manual attachment fallback.
            Set(recorder, "_history", history);
            Set(Get(recorder, "_sessionStore"), "<RootDirectory>k__BackingField", sessions.RootDirectory);
            Set(main, "_telemetryWindow", recorder); Call(main, "TrackEmbeddedContext", recorder, input);
            var prepare = Command("prepare");
            var prepared = Send(prepare);
            Check(prepared.Ok && !Send(prepare).Ok, "prepare was not replay protected: " + prepared.Message + " / " + JsonSerializer.Serialize(State()));
            Check(State().CanConfirm && !State().TuneConfirmed && State().SetupName == "baseline.ini", "preparation claimed setup use or omitted actual attachment");
            Check(!Send(Command("confirm")).Ok, "confirmation accepted without an explicit true value");
            var confirm = Command("confirm"); confirm.TuneConfirmed = true;
            Check(Send(confirm).Ok && State().TuneConfirmed, "explicit setup confirmation was not applied");
            var attached = Path.Combine(directory, "manually-attached.ini"); File.WriteAllText(attached, "[CAMBER_LF]\nVALUE=-25\n");
            Set(recorder, "_setupSnapshotPath", attached); ((TextBox)recorder.FindName("ConditionsBox")).Text = "manually entered dry conditions";
            Check(Send(Command("prepare")).Ok && State().SetupName == "manually-attached.ini" && State().Conditions == "manually entered dry conditions" && !State().TuneConfirmed,
                "preparing an unchanged plan erased the user's attachment or conditions");
            ((ComboBox)recorder.FindName("DriverBox")).Text = "A different driver";
            Check(!State().CanConfirm && !(bool)Call(main, "CompanionPlanMatches")!, "changed actual driver retained readiness");
            Check(Send(Command("prepare")).Ok && ((ComboBox)recorder.FindName("DriverBox")).Text == driver.Name && (bool)Call(main, "CompanionPlanMatches")!,
                "prepare did not repair an edited actual driver");
            ((TextBox)recorder.FindName("TestedChangeBox")).Text = "Unplanned desktop edit";
            Check(!(bool)Call(main, "CompanionPlanMatches")! && !State().CanConfirm, "edited real recording controls retained plan confirmation");
            Check(Send(Command("prepare")).Ok && (bool)Call(main, "CompanionPlanMatches")! && ((TextBox)recorder.FindName("TestedChangeBox")).Text == "", "prepare did not repair edited controls for the same plan");

            SavedTelemetrySession SaveRun(bool interrupted, string baselineId = "", bool confirmed = true, bool sameConditions = true)
            {
                var tune = history.CaptureTune(input, driver, "Fixture", new(), null, setup);
                var session = new TelemetrySession { CarName = input.Car.DisplayName, CarFolder = input.Car.SourceFolderName,
                    Wheelbase = input.Hardware.Model, SteeringWheel = input.Wheel.Model, DriftPack = input.DriftPack.Name,
                    DriftTarget = input.Intent.Name, StartedUtc = DateTime.UtcNow.AddMinutes(baselineId.Length > 0 ? 2 : 0),
                    Context = new() { DriverId = driver.Id, DriverName = driver.Name, Tune = tune, TrackId = "fixture-track",
                        CarIdentityVerified = true, TuneConfirmedInUse = confirmed, Conditions = sameConditions ? "dry, layout A, transitions" : "wet, different layout", Interrupted = interrupted,
                        RecommendationSessionId = baselineId, TestedRecommendations = baselineId.Length > 0 ? ["AC FFB: one controlled test"] : [] } };
                for (int i = 0; i < 5000; i++)
                {
                    var time = i / 50.0; var phase = time % 25;
                    double angle = phase < 5 ? 0 : phase < 6 ? (phase - 5) * 30 : phase < 11 ? 30 : phase < 12 ? 30 - (phase - 11) * 60 : phase < 17 ? -30 : phase < 18 ? -30 + (phase - 17) * 60 : phase < 22 ? 30 : 0;
                    session.Samples.Add(new() { TimeSeconds = time, PacketId = i + 1, SpeedKmh = 60, Throttle = .75, Clutch = 1,
                        Gear = 2, Rpm = 5000, SlipAngleDeg = angle, SteeringAngleDeg = -angle * 2, YawRateDegPerSec = angle / 2,
                        FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, FinalFfb = 1, HasExtendedSignals = true });
                }
                var analysis = new TelemetryAnalyzer().Analyze(session);
                var path = sessions.Save(session, analysis);
                var saved = new SavedTelemetrySession { Session = session, Analysis = analysis, JsonPath = path.JsonPath };
                Set(recorder, "_session", session); Set(recorder, "_analysis", analysis); Set(recorder, "_sessionSaved", true); Set(recorder, "_lastSavedForGuide", saved);
                Call(main, "TrackSavedRun", input, saved);
                return saved;
            }
            var interrupted = SaveRun(true);
            Check(State().SavedSessionId == interrupted.Session.Id && State().CanReadFindings && workflow.Journey(input, driver.Id).BaselineId == "", "interrupted saved run disappeared when journey could not advance");
            Check(Send(Command("findings")).Ok && State().Report is { } low && low.Recommendations.All(r => !r.CanSelect), "interrupted run offered a tuning test");
            var baseline = SaveRun(false);
            Check(State().Report is null, "replacement saved run retained previous findings");
            Check(Send(Command("findings")).Ok && State().Report!.Recommendations.Any(r => r.CanSelect), "clean baseline did not offer its actual engine recommendation");
            var before = JsonSerializer.Serialize(baseline);
            var plan = Command("plan"); plan.RecommendationId = State().Report!.Recommendations.First(r => r.CanSelect).Id;
            Check(Send(plan).Ok && !Send(plan).Ok, "planning failed or replayed");
            Check(workflow.Journey(input, driver.Id).BaselineId == baseline.Session.Id && workflow.Journey(input, driver.Id).AfterId == "" && !workflow.Journey(input, driver.Id).Reviewed, "planning implied a completed or improved comparison");
            Check(((SavedTelemetrySession)((ComboBox)recorder.FindName("RecommendationRunBox")).SelectedItem).Session.Id == baseline.Session.Id && !State().TuneConfirmed, "planned comparison did not select the exact baseline and reset confirmation");
            Check(JsonSerializer.Serialize(baseline) == before, "planning mutated saved telemetry or analysis");
            ((ComboBox)recorder.FindName("RecommendationRunBox")).SelectedIndex = -1;
            Check(!(bool)Call(main, "CompanionPlanMatches")! && !State().CanConfirm, "actual baseline clearing retained plan readiness");
            Check(Send(Command("prepare")).Ok && (bool)Call(main, "CompanionPlanMatches")!, "prepare did not restore a cleared baseline");
            Set(recorder, "_sessionSaved", false);
            Check(!State().CanPrepare && !State().CanReadFindings && !Send(Command("prepare")).Ok, "unsaved samples could be replaced by workflow preparation");
            Set(recorder, "_sessionSaved", true);
            var after = SaveRun(false, baseline.Session.Id, confirmed: false, sameConditions: false);
            var comparisonFindings = Send(Command("findings"));
            Check(comparisonFindings.Ok && State().Report!.Comparison.Verdict == "Inconclusive", "mismatched comparison failed or claimed improvement: " + comparisonFindings.Message + " / " + JsonSerializer.Serialize(State()));
            var wrongRun = Command("review"); wrongRun.SessionId = baseline.Session.Id; wrongRun.DriverRating = "Better";
            Check(!Send(wrongRun).Ok && history.ListReviews(input, driver.Id).Count == 0, "review command acted on a different saved run");
            var invalidReview = Command("review"); invalidReview.DriverRating = "Better"; invalidReview.Notes = new string('x', 2001);
            Check(!Send(invalidReview).Ok && history.ListReviews(input, driver.Id).Count == 0, "oversized review reached persistent history");
            var review = Command("review"); review.DriverRating = "Better"; review.NextAction = "Test again";
            Check(Send(review).Ok && !Send(review).Ok, "review failed or duplicated");
            var persisted = history.ListReviews(input, driver.Id).Single();
            Check(persisted.DriverRating == "Better" && persisted.Comparison.Verdict == "Inconclusive" && State().Report!.ReviewSaved, "driver feedback overwrote measured result or was not reflected");

            // A rated comparable after-run must allow the next eligible test without erasing its result.
            after.Session.Context!.TuneConfirmedInUse = true;
            after.Session.Context.Conditions = baseline.Session.Context!.Conditions;
            Check(Send(Command("findings")).Ok, "rated comparison could not refresh");
            var report = (TuningAssistantReport)Get(main, "_companionWorkflowReport");
            Check(report.Outcome.Comparable, "matching confirmed fixture did not become comparable: " + string.Join("; ", report.Outcome.Limitations));
            Check(State().Report!.Recommendations.Any(r => r.CanSelect), "rated comparable after-run dead-ended instead of allowing another recommendation");
            Check(CompanionSavedRunLookup.Find(sessions, baseline.Session.Id)?.Session.Id == baseline.Session.Id, "exact baseline lookup failed");
            Check(CompanionSavedRunLookup.Find(sessions, Guid.NewGuid().ToString("N")) is null, "missing baseline was replaced with another run");
            var priorState = State();
            behavior.Save(input, new() { AngleStability = 2 });
            Check(!(bool)Call(main, "CompanionPlanMatches")! && !State().CanConfirm && State().Report is null && priorState.ControlVersion != State().ControlVersion,
                "changed saved goals retained old Start readiness, confirmation or findings");
        }
        finally
        {
            Set(main, "_telemetryWindow", null); recorder?.Close(); main.Close();
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        Progress($"PASS {checks} companion workflow assertions with isolated histories, plans, findings and reviews.");
    }
}
