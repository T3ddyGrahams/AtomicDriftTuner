using System.Windows;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;
public partial class MainWindow
{
    private readonly GuidedWorkflowStore _workflow = new();
    private bool _guidedReady;
    private IntegrationState? _guidedConnection;
    private DateTime? _guidedCheckedAt;

    private void InitializeGuidedWorkflow()
    {
        try
        {
            GuidedDriverBox.ItemsSource = new RunHistoryStore().ListDrivers();
            GuidedDriverBox.Text = _workflow.Preferences().DriverName;
            _guidedReady = true;
            RefreshGuidedWorkflow();
        }
        catch (Exception ex) { GuidedInstructionsText.Text = "Workflow could not load: " + ex.Message; }
    }
    private DriverIdentity CurrentGuidedDriver() => new RunHistoryStore().GetOrCreateDriver(_workflow.Preferences().DriverName);
    private GuidedJourney JourneyForGuide(TuneInput input, string driver)
    {
        var j = _workflow.Journey(input, driver);
        if (j.BaselineId.Length == 0 && j.TuneGenerated)
        {
            var current = _engine.Generate(input, _calibrationStore.Get(_calibrationEngine.BuildKey(input)), _azomPreferences);
            if (j.GeneratedSignature != GuidedWorkflowEngine.TuneSignature(current)) j.TuneGenerated = j.TuneReady = false;
        }
        return j;
    }
    private void RefreshGuidedWorkflow()
    {
        if (!_guidedReady) return;
        try
        {
            var input = BuildInput(); var prefs = _workflow.Preferences(); var driver = CurrentGuidedDriver();
            var j = JourneyForGuide(input, driver.Id);
            var goal = _behaviorStore.Load(input);
            var step = GuidedWorkflowEngine.Next(prefs, j, GuidedWorkflowStore.GoalSignature(goal));
            GuidedStepText.Text = step.Title;
            GuidedInstructionsText.Text = $"{input.Car.DisplayName} · {input.Hardware.Model} · Driver: {driver.Name}\n{step.Instructions}";
            if (step.Stage == GuidedStage.Goals) GuidedInstructionsText.Text += $"\nSaved goals: front bite {goal.FrontEndBite:+0;-0;0}, rear grip {goal.RearGrip:+0;-0;0}, self-steer {goal.SelfSteerSpeed:+0;-0;0}, transition {goal.TransitionSpeed:+0;-0;0}, stability {goal.AngleStability:+0;-0;0}, throttle {goal.ThrottleSteering:+0;-0;0}, initiation {goal.InitiationSharpness:+0;-0;0}.";
            if (step.Stage == GuidedStage.Test) GuidedInstructionsText.Text += "\nSelected test: " + j.Recommendation;
            GuidedNextButton.Content = step.Action;
            GuidedNextButton.IsEnabled = true;
            GuidedReadyCheck.Visibility = step.Stage == GuidedStage.Prepare && j.TuneGenerated ? Visibility.Visible : Visibility.Collapsed;
            GuidedEditSetupButton.Visibility = step.Stage is GuidedStage.Goals or GuidedStage.Prepare ? Visibility.Visible : Visibility.Collapsed;
            GuidedPrepareChangeButton.Visibility = step.Stage == GuidedStage.Test ? Visibility.Visible : Visibility.Collapsed;
            GuidedWheelbaseButton.Visibility = step.Stage is GuidedStage.Prepare or GuidedStage.Test ? Visibility.Visible : Visibility.Collapsed;
            GuidedResetButton.Visibility = j.BaselineId.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            GuidedIntegrationText.Text = GuidedWorkflowEngine.Instructions(prefs, _guidedConnection);
            if (_guidedConnection is { } connection)
                GuidedIntegrationText.Text = $"Last checked {_guidedCheckedAt:t}: SimHub folder {(connection.SimHubInstalled ? "found" : "not found")}; process {(connection.SimHubRunning ? "running" : "not running")}; bridge {(connection.BridgeConnected ? "connected" : "offline")}; AZOM {(!connection.BridgeConnected ? "unverified" : connection.AzomDetected ? "detected" : "not detected")}.\n" + GuidedIntegrationText.Text;
            GuidedProgressText.Text = $"Car {(j.CarConfirmed ? "✓" : "○")} → Goals {(j.GoalSignature.Length > 0 ? "✓" : "○")} → Prepare {(j.TuneReady ? "✓" : "○")} → Baseline {(j.BaselineId.Length > 0 ? "✓" : "○")} → Test {(j.Recommendation.Length > 0 ? "✓" : "○")} → Compare {(j.AfterId.Length > 0 ? "✓" : "○")} → Review {(j.Reviewed ? "✓" : "○")}";
        }
        catch (Exception ex)
        {
            GuidedNextButton.IsEnabled = false;
            GuidedInstructionsText.Text = "Select a complete car/rig below. " + ex.Message;
        }
    }
    private void SaveGuidedDriver_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var driver = new RunHistoryStore().GetOrCreateDriver(GuidedDriverBox.Text);
            var p = _workflow.Preferences(); p.DriverName = driver.Name; _workflow.SavePreferences(p);
            GuidedReadyCheck.IsChecked = false; RefreshGuidedWorkflow();
        }
        catch (Exception ex) { GuidedInstructionsText.Text = ex.Message; }
    }
    private async void CheckDashboardIntegration_Click(object sender, RoutedEventArgs e)
    {
        GuidedCheckButton.IsEnabled = false;
        try
        {
            var settings = _appSettingsStore.Load();
            _guidedConnection = await new GuidedIntegrationService().CheckAsync(settings.SimHubRoot, settings.AzomLive.PipeName, CancellationToken.None);
            _guidedCheckedAt = DateTime.Now;
            RefreshGuidedWorkflow();
        }
        catch (Exception ex) { GuidedIntegrationText.Text = "Could not check the connection: " + ex.Message; }
        finally { GuidedCheckButton.IsEnabled = true; }
    }
    private void GuidedNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput(); var driver = CurrentGuidedDriver(); var j = JourneyForGuide(input, driver.Id);
            var signature = GuidedWorkflowStore.GoalSignature(_behaviorStore.Load(input));
            var next = GuidedWorkflowEngine.Next(_workflow.Preferences(), j, signature);
            switch (next.Stage)
            {
                case GuidedStage.Welcome: OpenSetupWizard(false); return;
                case GuidedStage.Car:
                    if (!input.Car.IsInstalled) { GuidedInstructionsText.Text = "Scan AC and select your installed car under Car & Hardware before confirming."; return; }
                    _workflow.Update(input, driver.Id, x => x.CarConfirmed = true); break;
                case GuidedStage.Goals: _workflow.Update(input, driver.Id, x => x.GoalSignature = signature); break;
                case GuidedStage.GoalsChanged: _workflow.Reset(input, driver.Id); break;
                case GuidedStage.Prepare:
                    if (!j.TuneGenerated) { Generate_Click(sender, e); return; }
                    if (GuidedReadyCheck.IsChecked != true) { GuidedInstructionsText.Text = "Review/use the settings first, then tick the confirmation below. Generating a tune does not apply it."; return; }
                    _workflow.Update(input, driver.Id, x => x.TuneReady = true); break;
                case GuidedStage.Baseline:
                case GuidedStage.Test: OpenTelemetry_Click(sender, e); return;
                default: OpenTuningAssistant_Click(sender, e); return;
            }
            GuidedReadyCheck.IsChecked = false; RefreshGuidedWorkflow();
        }
        catch (Exception ex) { GuidedInstructionsText.Text = "Next step could not complete: " + ex.Message; }
    }
    private void NewGuidedBaseline_Click(object sender, RoutedEventArgs e)
    {
        try { _workflow.Reset(BuildInput(), CurrentGuidedDriver().Id); GuidedReadyCheck.IsChecked = false; RefreshGuidedWorkflow(); }
        catch (Exception ex) { GuidedInstructionsText.Text = ex.Message; }
    }
    private void TrackGeneratedForGuide(TuneInput input)
    {
        if (!_guidedReady) return;
        _workflow.Update(input, CurrentGuidedDriver().Id, j => { j.TuneGenerated = true; j.GeneratedSignature = GuidedWorkflowEngine.TuneSignature(_lastResult!); j.TuneReady = false; });
        GuidedReadyCheck.IsChecked = false; RefreshGuidedWorkflow();
        ShowDashboardSection(GeneratedTuneCard);
    }
    private void BackToGuided_Click(object sender, RoutedEventArgs e) => ShowDashboardSection(GuidedWorkflowCard);
    private RecordingPlan GuidedRecordingPlan(TuneInput input)
    {
        var driver = CurrentGuidedDriver(); var j = _workflow.Journey(input, driver.Id);
        return new(driver.Id, driver.Name, j.Recommendation.Length > 0 ? j.BaselineId : "", j.Recommendation, j.SetupPath, j.Conditions);
    }
    private void TrackSavedRun(TuneInput input, SavedTelemetrySession saved)
    {
        var c = saved.Session.Context;
        if (!RunHistoryStore.ValidContext(c)) return;
        if (!GuidedWorkflowEngine.CanAdvanceFromRun(saved.Session, saved.Analysis)) return;
        if (RunHistoryStore.ContextKey(BuildInput()) == RunHistoryStore.ContextKey(input))
        {
            var p = _workflow.Preferences(); p.DriverName = c!.DriverName; _workflow.SavePreferences(p);
            GuidedDriverBox.Text = p.DriverName;
        }
        _workflow.Update(input, c!.DriverId, j =>
        {
            j.CarConfirmed = true; j.GoalSignature = GuidedWorkflowStore.GoalSignature(c.Tune!.DesiredBehavior);
            j.TuneGenerated = j.TuneReady = true; // A recording exists; this is navigation progress, never proof of applied settings.
            j.Conditions = c.Conditions; j.Reviewed = false;
            if (c.RecommendationSessionId.Length > 0)
            { j.BaselineId = c.RecommendationSessionId; j.AfterId = saved.Session.Id; j.Recommendation = string.Join("\n", c.TestedRecommendations); }
            else { j.BaselineId = saved.Session.Id; j.AfterId = j.Recommendation = ""; }
        });
        RefreshGuidedWorkflow();
    }
    private void HandleRecommendation(TuneInput input, SavedTelemetrySession run, string recommendation)
    {
        RequireGuidedContext(input);
        var c = run.Session.Context;
        if (!RunHistoryStore.ValidContext(c)) throw new InvalidOperationException("Record a new baseline with driver and tune context before starting a guided test.");
        if (!RunHistoryStore.SameBehavior(c!.Tune!.DesiredBehavior, _behaviorStore.Load(input)))
            throw new InvalidOperationException("This run's recorded goals differ from the current saved Desired Behavior. Use matching goals or record a new baseline.");
        var prefs = _workflow.Preferences(); prefs.DriverName = c.DriverName; _workflow.SavePreferences(prefs); GuidedDriverBox.Text = c.DriverName;
        _workflow.Update(input, c.DriverId, j =>
        {
            j.CarConfirmed = j.TuneGenerated = j.TuneReady = true; j.GoalSignature = GuidedWorkflowStore.GoalSignature(c.Tune.DesiredBehavior);
            j.BaselineId = run.Session.Id; j.AfterId = ""; j.Recommendation = recommendation; j.Conditions = c.Conditions; j.Reviewed = false;
        });
        RefreshGuidedWorkflow(); ShowDashboardSection(GuidedWorkflowCard);
    }
    private void RequireGuidedContext(TuneInput input)
    {
        var current = BuildInput();
        if (RunHistoryStore.ContextKey(current) != RunHistoryStore.ContextKey(input) || current.Intent.Kind != input.Intent.Kind)
            throw new InvalidOperationException("The dashboard car/rig changed while this tool was open. Select this run's car and rig on the dashboard before continuing its guided workflow.");
    }
}
