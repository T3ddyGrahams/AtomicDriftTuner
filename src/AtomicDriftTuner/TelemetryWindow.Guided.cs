using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;
public partial class TelemetryWindow
{
    public event Action<SavedTelemetrySession>? SessionSaved;
    public event Action<SavedTelemetrySession>? CompareRequested;
    private RecordingPlan? _recordingPlan;
    private SavedTelemetrySession? _lastSavedForGuide;
    public void UseRecordingPlan(RecordingPlan plan)
    {
        if (_recordingPlan == plan) return;
        if (_recording || !_sessionSaved && _session.Samples.Count > 0)
        {
            StatusText.Text = "Finish and save the current recording first. Return to the dashboard and open the next step again to load the new test plan.";
            return;
        }
        var recent = _sessionStore.ListRecent(_input, 100);
        var baseline = recent.FirstOrDefault(s => s.Session.Id == plan.BaselineId && s.Session.Context?.DriverId == plan.DriverId);
        if (plan.BaselineId.Length > 0 && baseline is null)
            throw new InvalidOperationException("The planned baseline is not in this car/driver's recent history. Select it in Tuning Assistant or start a new baseline; ADT has not substituted another run.");
        _recordingPlan = plan;
        DriverBox.Text = plan.DriverName;
        RecommendationRunBox.ItemsSource = recent; RecommendationRunBox.SelectedItem = baseline;
        ConditionsBox.Text = plan.Conditions;
        TestedChangeBox.Text = plan.Recommendation;
        TuneLabelBox.Text = baseline is null ? "Baseline" : $"Test {DateTime.Now:MMM d HH:mm}";
        if (File.Exists(plan.SetupPath))
        {
            _setupSnapshotPath = plan.SetupPath;
            SetupSnapshotText.Text = "Prepared setup: " + Path.GetFileName(plan.SetupPath) + ". Confirm this is the file loaded in AC, or attach the correct one.";
        }
        else { _setupSnapshotPath = null; SetupSnapshotText.Text = "Attach the AC setup file actually used. ADT cannot detect which saved setup you loaded in the game."; }
        TuneInUseCheck.IsChecked = false;
        RecorderStepsText.Text = baseline is null
            ? "BASELINE: Connect → attach the setup used → describe conditions → confirm tune use → Record → Stop → Save Session → Review Findings. SimHub is not required for AC telemetry."
            : "COMPARISON: your driver, baseline, conditions and selected recommendation are filled in. Make the change, attach the revised setup, confirm tune use, then Connect → Record → Stop → Save Session → Compare With Baseline.";
    }
    private void CompareSavedRun_Click(object sender, RoutedEventArgs e)
    {
        if (_lastSavedForGuide is null || !_sessionSaved || _recording) return;
        try { CompareRequested?.Invoke(_lastSavedForGuide); }
        catch (Exception ex) { StatusText.Text = "Run is saved; comparison could not open: " + ex.Message; }
    }
    private void NotifySavedRun(string path)
    {
        _lastSavedForGuide = new SavedTelemetrySession { Session = RunHistoryStore.Clone(_session), Analysis = _analysis!, JsonPath = path };
        CompareSavedRunButton.Content = _session.Context?.RecommendationSessionId.Length > 0 ? "Compare With Baseline" : "Review Baseline Findings";
        CompareSavedRunButton.IsEnabled = true;
        if (!Engine.GuidedWorkflowEngine.CanAdvanceFromRun(_session, _analysis!))
            RecorderStepsText.Text = "Run saved for inspection. Guided progress needs at least 20 seconds of clean drift with reliable sampling. Record another clean run; use Review / Compare to inspect this recording's limitations.";
        try { SessionSaved?.Invoke(_lastSavedForGuide); }
        catch (Exception ex) { StatusText.Text += " Run saved; workflow progress could not be updated: " + ex.Message; }
    }
}
