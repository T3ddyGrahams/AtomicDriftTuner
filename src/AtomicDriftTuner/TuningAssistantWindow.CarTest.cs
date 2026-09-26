using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow
{
    public event Action<SavedTelemetrySession, RecommendationTest>? StructuredTestRequested;
    private void RenderCarTest()
    {
        CarTestCard.Visibility = TuningFocusOptions.IncludesCar(_focus) ? Visibility.Visible : Visibility.Collapsed;
        var reason = AssistantCarTestService.UnavailableReason(_input, _reportSession, _report);
        CarTestSummaryText.Text = reason.Length > 0 ? reason :
            "Review one adjustment for this finding. Choose the setup used in this run to see the exact current → proposed values, why to test them and how to compare the result. Nothing changes in game until you load or explicitly apply the test setup.";
        ReviewCarTestButton.IsEnabled = reason.Length == 0 && TuningFocusOptions.IncludesCar(_focus) && _guidedSetupWindow is null;
        if (reason.Length == 0 && TuningFocusOptions.IncludesCar(_focus))
        {
            NextNoticedText.Text = AssistantCarTestService.FindingSummary(_reportSession!, _report!);
            NextInstructionText.Text = "Review one car setup change below. Check the exact values, then save or stage a separate test and apply it in the pits before recording again.";
        }
        if (_nextStep.Action == "Plan" && _nextStep.Recommendation?.Area == RecommendationArea.CarSetup && TuningFocusOptions.IncludesCar(_focus))
        {
            NextActionButton.Content = reason.Length == 0 ? "Review one car setup change" : "Back to dashboard";
            NextActionButton.IsEnabled = _guidedSetupWindow is null;
        }
        else NextActionButton.IsEnabled = _guidedSetupWindow is null;
    }

    private void ReviewCarTest_Click(object sender, RoutedEventArgs e)
    {
        if (_guidedSetupWindow is not null) { RestoreAndActivateGuidedSetup(); return; }
        if (!TuningFocusOptions.IncludesCar(_focus)) return;
        var reason = AssistantCarTestService.UnavailableReason(_input, _reportSession, _report);
        if (reason.Length > 0 || SessionBox.SelectedItem is not SavedTelemetrySession selected || !ReferenceEquals(selected, _reportSession))
        { StatusText.Text = reason.Length > 0 ? reason : "Select the run again to refresh its report."; return; }
        try
        {
            var window = new CarSetupTestWindow(_input, selected, _report!)
            {
                StageHandler = StagePitSetupHandler,
                TestPrepared = (test, path) =>
                {
                    if (StructuredTestRequested is null) throw new InvalidOperationException("Open the assistant from the dashboard to track this test.");
                    StructuredTestRequested.Invoke(selected, test);
                    // A staged test has no saved desktop file yet. Clear the previous prepared
                    // path so it cannot be mistaken for the changed setup in the next recorder.
                    GuidedSetupSaved?.Invoke(selected, path ?? "");
                }
            };
            if (ResolveVisibleOwner() is { } owner) window.Owner = owner;
            _guidedSetupWindow = window;
            SetGuidedSetupState(true);
            window.Closed += GuidedSetupWindow_Closed;
            window.Show(); window.Activate();
            StatusText.Text = "Setup test review is open. The selected run is locked until you close it.";
        }
        catch (Exception ex)
        {
            _guidedSetupWindow = null; SetGuidedSetupState(false);
            StatusText.Text = "Could not open the setup test: " + ex.Message;
        }
    }
}
