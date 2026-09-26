using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow
{
    private readonly SpeedUnitPreferenceStore _speedUnits = new();
    private void RenderDrivingContext(TuningAssistantReport? report)
    {
        DrivingContextGrid.ItemsSource = report?.ContextAssessments;
        DrivingContextGrid.SelectedIndex = report is null ? -1 : report.ContextAssessments.FindIndex(r => r.Confidence != "LOW");
        if (report?.ContextAssessments.Count > 0 && DrivingContextGrid.SelectedIndex < 0) DrivingContextGrid.SelectedIndex = 0;
        DrivingContextSummaryText.Text = report?.DrivingContextSummary ?? "";
    }
}
