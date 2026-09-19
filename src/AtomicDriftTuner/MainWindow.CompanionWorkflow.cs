using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class MainWindow
{
    private readonly TelemetrySessionStore _companionWorkflowSessions = new();
    private readonly RunHistoryStore _companionWorkflowHistory = new();
    private long _companionWorkflowRevision;
    private string _companionWorkflowReportKey = "";
    private TuningAssistantReport? _companionWorkflowReport;
    private SavedTelemetrySession? _companionWorkflowRun;
    private SavedTelemetrySession? _companionWorkflowBaseline;
    private bool _companionWorkflowReviewed;

    private sealed record CompanionWorkflowContext(TuneInput Input, GuidedPreferences Preferences, DriverIdentity Driver,
        GuidedJourney Journey, CarBehaviorTarget Goal, CompanionRecorderState Recorder, TelemetryWindow.CompanionPreparation? Preparation,
        bool RecorderMatches, SavedTelemetrySession? Latest, string SessionId, string Key);

    private bool CompanionPlanMatches()
    {
        try
        {
            var input = BuildInput(); var preferences = _workflow.Preferences();
            if (!preferences.Completed || !preferences.FocusChoiceConfirmed) return false;
            var driver = _companionWorkflowHistory.GetOrCreateDriver(preferences.DriverName);
            var journey = JourneyForGuide(input, driver.Id);
            if (!journey.CarConfirmed || !journey.TuneReady || journey.GoalSignature != GuidedWorkflowStore.GoalSignature(_behaviorStore.Load(input))) return false;
            return _telemetryWindow is not null && _telemetryWindow.CompanionPlanMatches(CompanionRecordingPlan(journey, driver, preferences));
        }
        catch { return false; }
    }

    private static RecordingPlan CompanionRecordingPlan(GuidedJourney journey, DriverIdentity driver, GuidedPreferences preferences) =>
        new(driver.Id, driver.Name, journey.Recommendation.Length > 0 ? journey.BaselineId : "", journey.Recommendation,
            journey.SetupPath, journey.Conditions, journey.Focus, preferences.ShowDetailedHelp, preferences.FfbProvider);

    private CompanionWorkflowContext CompanionWorkflowContextNow()
    {
        var preferences = _workflow.Preferences();
        if (!preferences.Completed || !preferences.FocusChoiceConfirmed)
            throw new InvalidOperationException("Choose Car tuning only or Car + FFB in desktop Setup & Paths first.");
        if (!HasCompleteSessionSelection)
            throw new InvalidOperationException("Choose your own car, hardware and drift target in desktop ADT first.");
        var input = BuildInput();
        var driver = _companionWorkflowHistory.GetOrCreateDriver(preferences.DriverName);
        var journey = JourneyForGuide(input, driver.Id);
        var goal = _behaviorStore.Load(input);
        var matches = _telemetryWindow is not null && EmbeddedContextMatches(_telemetryWindow, input);
        var recorder = _telemetryWindow?.GetCompanionState(CompanionContextMatches()) ?? new();
        var preparation = _telemetryWindow?.GetCompanionPreparation();
        var latest = _telemetryWindow?.GetCompanionSavedRun();
        if (!RunBelongsToWorkflow(latest, input, driver.Id, preferences.Focus)) latest = null;
        var sessionId = latest?.Session.Id ?? (journey.AfterId.Length > 0 ? journey.AfterId : journey.BaselineId);
        var key = CompanionWorkflowPresentation.Version(new
        {
            input, preferences, driver.Id, journey, GoalSignature = GuidedWorkflowStore.GoalSignature(goal), recorder.WindowId, RecorderSessionId = recorder.SessionId,
            recorder.ControlVersion, recorder.State, RecorderMatches = matches, preparation, SavedSessionId = sessionId
        });
        return new(input, preferences, driver, journey, goal, recorder, preparation, matches, latest, sessionId, key);
    }

    private static bool RunBelongsToWorkflow(SavedTelemetrySession? run, TuneInput input, string driverId, TuningFocus focus) =>
        run?.Session.Context is { Tune: not null } context && RunHistoryStore.ValidContext(context) &&
        context.DriverId == driverId && context.Focus == focus && context.Tune.ContextKey == RunHistoryStore.ContextKey(input);

    private CompanionWorkflowState BuildCompanionWorkflowState()
    {
        Dispatcher.VerifyAccess();
        try
        {
            var context = CompanionWorkflowContextNow();
            var journey = context.Journey;
            var preparation = context.Preparation;
            var idle = preparation?.Busy != true;
            var step = GuidedWorkflowEngine.Next(context.Preferences, journey, GuidedWorkflowStore.GoalSignature(context.Goal));
            bool planReady = journey.CarConfirmed && journey.TuneReady && journey.GoalSignature == GuidedWorkflowStore.GoalSignature(context.Goal);
            var state = new CompanionWorkflowState
            {
                Available = true, Stage = step.Stage.ToString(), ComingNext = GuidedWorkflowEngine.ComingNext(step.Stage),
                Focus = TuningFocusOptions.Label(context.Preferences.Focus), Car = context.Input.Car.DisplayName, Driver = context.Driver.Name,
                Conditions = preparation?.Conditions ?? journey.Conditions, SetupName = preparation?.SetupName ?? Path.GetFileName(journey.SetupPath),
                ConfirmationText = preparation?.ConfirmationText ?? TuningFocusOptions.Confirmation(context.Preferences.Focus),
                TuneConfirmed = preparation?.Confirmed == true && CompanionContextMatches(),
                CanPrepare = context.RecorderMatches && idle && planReady,
                CanConfirm = context.RecorderMatches && idle && planReady && CompanionContextMatches() && preparation is { SetupAvailable: true, Conditions.Length: > 0 },
                CanReadFindings = idle && context.SessionId.Length > 0,
                SelectedRecommendation = journey.Recommendation, SavedSessionId = context.SessionId,
                BaselineSessionId = context.Latest?.Session.Context?.RecommendationSessionId ?? (journey.Recommendation.Length > 0 ? journey.BaselineId : "")
            };
            if (_companionWorkflowReportKey == context.Key && _companionWorkflowReport is not null && _companionWorkflowRun is not null)
            {
                var mayPlan = context.RecorderMatches && idle && (_companionWorkflowReport.NextStep.Action == "Plan" ||
                    _companionWorkflowReviewed && _companionWorkflowReport.Outcome.Comparable && _companionWorkflowReport.NextStep.Action == "Review") &&
                    _companionWorkflowReport.OverallConfidence != AssistantConfidence.Low &&
                    GuidedWorkflowEngine.CanAdvanceFromRun(_companionWorkflowRun.Session, _companionWorkflowRun.Analysis) &&
                    RunHistoryStore.SameBehavior(_companionWorkflowRun.Session.Context!.Tune!.DesiredBehavior, context.Goal);
                state.Report = CompanionWorkflowPresentation.Report(_companionWorkflowReport, _companionWorkflowRun.Session.Id,
                    _companionWorkflowBaseline?.Session.Id ?? "", context.Preferences.Focus, mayPlan, _companionWorkflowReviewed);
                state.CanSaveReview = idle && _companionWorkflowBaseline is not null && !_companionWorkflowReviewed;
            }
            state.Message = preparation?.Busy == true ? "Stop and save the captured run before preparing another test."
                : !context.RecorderMatches ? "In desktop ADT, open Telemetry Recorder for this car and driver once. Saved findings remain available here."
                : !planReady ? "Finish choosing the car, saving goals and preparing the starting tune in desktop ADT."
                : !CompanionContextMatches() ? "The selected test or driver changed. Prepare the next run here before starting it."
                : preparation?.SetupAvailable != true ? "Wait for current setup capture from the updated companion, or attach the loaded setup manually in the desktop recorder."
                : preparation.Conditions.Length == 0 ? "Enter the conditions and driving task in the desktop recorder, then return here."
                : !state.TuneConfirmed ? "Check the current setup and the displayed confirmation, then confirm here. Nothing is applied automatically."
                : state.Report?.ReviewSaved == true ? "Your rating is saved separately from the measured result. Keep/revert decisions do not apply settings."
                : "Prepared. Record, stop and save; then read findings here before choosing your next test.";
            state.ControlVersion = CompanionWorkflowPresentation.Version(new
            {
                context.Key, _companionWorkflowRevision, Report = state.Report, state.CanPrepare, state.CanConfirm,
                state.CanReadFindings, state.CanSaveReview
            });
            return state;
        }
        catch (Exception ex)
        {
            return new CompanionWorkflowState { Message = ex is InvalidOperationException ? ex.Message :
                "Desktop workflow needs attention. Check ADT's setup and saved-workflow message.",
                ControlVersion = CompanionWorkflowPresentation.Version(new { _companionWorkflowRevision, Error = ex.Message }) };
        }
    }

    private RemoteActionResponse ExecuteCompanionWorkflowCommand(CompanionWorkflowCommand command)
    {
        Dispatcher.VerifyAccess();
        if (_pitSetup.Busy) return new() { Message = _pitSetup.RecordingBlockReason! };
        var state = BuildCompanionWorkflowState();
        var rejection = CompanionWorkflowPresentation.Reject(command, state);
        if (rejection is not null) return new() { Ok = false, Message = rejection };
        try
        {
            var context = CompanionWorkflowContextNow();
            string message;
            switch (command.Action)
            {
                case "prepare":
                    var preparedPlan = CompanionRecordingPlan(context.Journey, context.Driver, context.Preferences);
                    if (!_telemetryWindow!.CompanionPlanMatches(preparedPlan)) _telemetryWindow.UseRecordingPlan(preparedPlan, force: true);
                    // A repeat of the same plan still requires fresh confirmation of what is loaded.
                    _telemetryWindow.TuneInUseCheck.IsChecked = false;
                    message = "Recording plan prepared. Make the change in AC, wait for current setup capture (or attach the file manually), then confirm here.";
                    break;
                case "confirm":
                    _telemetryWindow!.ConfirmCompanionPreparation();
                    message = "Your confirmation is recorded for the prepared setup. You can start the run when AC telemetry is live.";
                    break;
                case "findings":
                    LoadCompanionFindings(context);
                    message = "Findings loaded from the saved run. Recommendations and confidence use ADT's full analysis.";
                    break;
                case "plan":
                    var recommendation = _companionWorkflowReport!.Recommendations.Single(r =>
                        CompanionWorkflowPresentation.RecommendationId(_companionWorkflowRun!.Session.Id, r) == command.RecommendationId);
                    HandleRecommendation(context.Input, _companionWorkflowRun!, recommendation.Domain + ": " + recommendation.Change);
                    _telemetryWindow!.UseRecordingPlan(CompanionRecordingPlan(_workflow.Journey(context.Input, context.Driver.Id), context.Driver, context.Preferences), force: true);
                    message = "Test planned against this exact baseline. Make the chosen change, wait for current setup capture (or attach it manually), then confirm before recording.";
                    break;
                case "review":
                    var runContext = _companionWorkflowRun!.Session.Context!;
                    var review = new RunReview
                    {
                        SessionId = _companionWorkflowRun.Session.Id, BaselineSessionId = _companionWorkflowBaseline!.Session.Id,
                        Focus = runContext.Focus, DriverId = runContext.DriverId, ContextKey = runContext.Tune!.ContextKey,
                        DriverRating = command.DriverRating, NextAction = command.NextAction, Notes = command.Notes.Trim(),
                        Comparison = RunHistoryStore.Clone(_companionWorkflowReport!.Outcome)
                    };
                    _companionWorkflowHistory.SaveReview(review);
                    _workflow.Update(context.Input, review.DriverId, j =>
                    {
                        if (j.AfterId == review.SessionId && j.BaselineId == review.BaselineSessionId) j.Reviewed = true;
                    }, review.Focus);
                    _companionWorkflowReviewed = true;
                    _companionWorkflowReportKey = CompanionWorkflowContextNow().Key;
                    RefreshGuidedWorkflow();
                    message = "Review saved. Your rating and measured result remain separate; no settings were kept, reverted or applied automatically.";
                    break;
                default: throw new InvalidOperationException("Unknown workflow command.");
            }
            _companionWorkflowRevision++;
            return new() { Ok = true, Message = message };
        }
        catch (Exception ex)
        {
            // A partial operation must also invalidate a command whose effect is uncertain.
            _companionWorkflowRevision++;
            return new() { Ok = false, Message = ex is InvalidOperationException or InvalidDataException ? ex.Message :
                "ADT could not complete this workflow action. Refresh its status and check desktop ADT; your recordings remain saved." };
        }
    }

    private void LoadCompanionFindings(CompanionWorkflowContext context)
    {
        // Only an explicit findings request reads/reanalyzes history; one-second status polls never do.
        var selected = context.Latest ?? CompanionSavedRunLookup.Find(_companionWorkflowSessions, context.SessionId);
        if (!RunBelongsToWorkflow(selected, context.Input, context.Driver.Id, context.Preferences.Focus))
            throw new InvalidOperationException("This saved run is unavailable for the selected car, driver and tuning choice. Choose it in desktop ADT.");
        var baselineId = selected!.Session.Context!.RecommendationSessionId;
        var baseline = baselineId.Length == 0 ? null : CompanionSavedRunLookup.Find(_companionWorkflowSessions, baselineId);
        if (baselineId.Length > 0 && !RunBelongsToWorkflow(baseline, context.Input, context.Driver.Id, context.Preferences.Focus))
            throw new InvalidOperationException("The recorded baseline is unavailable. ADT has not substituted another run; review history on the desktop.");
        var report = new TelemetryTuningAssistantEngine().Build(context.Input, context.Goal, selected, baseline);
        _companionWorkflowRun = selected;
        _companionWorkflowBaseline = baseline;
        _companionWorkflowReport = report;
        _companionWorkflowReviewed = _companionWorkflowHistory.ListReviews(context.Input, context.Driver.Id)
            .Any(r => r.SessionId == selected.Session.Id && r.BaselineSessionId == baselineId && r.DriverRating != "Not rated");
        _companionWorkflowReportKey = context.Key;
    }
}
