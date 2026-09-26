using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow
{
    private AssistantNextStep _nextStep = new();
    private void RenderNextStep(AssistantNextStep next)
    {
        _nextStep = next;
        NextGoalText.Text = next.Goal;
        NextNoticedText.Text = next.Noticed;
        NextConfidenceText.Text = next.Confidence;
        NextInstructionText.Text = next.Instruction;
        NextWhyText.Text = next.Why;
        NextActionButton.Content = next.ActionLabel;
        if (next.Recommendation is not null && !TuningFocusOptions.Allows(_focus, next.Recommendation))
        {
            _nextStep = new AssistantNextStep { Goal = next.Goal };
            NextNoticedText.Text = "No supported next step is selected for this workflow.";
            NextInstructionText.Text = "Keep this setup and use the dashboard to prepare another run.";
            NextActionButton.Content = "Back to dashboard";
        }
        RenderCarTest();
    }
    private void SelectAssistantTab(string header)
    {
        AssistantTabs.SelectedItem = AssistantTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Header?.ToString() == header);
        AssistantBodyScroll.ScrollToHome();
    }
    private void AdvancedTelemetry_Changed(object sender, RoutedEventArgs e)
    {
        if (AssistantTabs is null) return;
        if (AdvancedTelemetryToggle.IsChecked != true &&
            (AssistantTabs.SelectedItem as TabItem)?.Header?.ToString() is "Assessment" or "Recommendations" or "Phase Evidence" or "Pedal Evidence")
            SelectAssistantTab("Your next step");
    }
    private void ShowEvidence_Click(object sender, RoutedEventArgs e)
    {
        AdvancedTelemetryToggle.IsChecked = true;
        SelectAssistantTab(_nextStep.Recommendation?.Priority == "Repeat inputs" ? "Pedal Evidence" : "Phase Evidence");
    }
    private void NextAction_Click(object sender, RoutedEventArgs e)
    {
        if (_guidedSetupWindow is not null) { StatusText.Text = "Close AC Setup with Guidance before choosing another next step."; return; }
        switch (_nextStep.Action)
        {
            case "Plan":
                if (_nextStep.Recommendation is null || _reportSession is null) return;
                if (_nextStep.Recommendation.Area == RecommendationArea.CarSetup)
                {
                    if (AssistantCarTestService.UnavailableReason(_input, _reportSession, _report).Length > 0) Close();
                    else ReviewCarTest_Click(sender, e);
                    break;
                }
                if (RecommendationTestRequested is null) { StatusText.Text = "Open this assistant from the dashboard to prepare a recorded test."; return; }
                RecommendationGrid.SelectedItem = _nextStep.Recommendation;
                TestRecommendation_Click(sender, e);
                // The existing handler saves the plan through the dashboard event.
                // Keep this window available if the save failed.
                if (StatusText.Text.StartsWith("Test plan saved.")) Close();
                break;
            case "Compare": SelectAssistantTab("Before / After"); ComparisonReasonsExpander.IsExpanded = true; break;
            case "Review": SelectAssistantTab("Before / After"); QuickFeedback.BringIntoView(); break;
            case "Evidence": ShowEvidence_Click(sender, e); break;
            default: Close(); break;
        }
    }
}
