using System.Windows;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow
{
    private void ShowPowertrain_Click(object sender, RoutedEventArgs e) => SelectAssistantTab("Gearing & ECU");
    private void PowertrainBaseline_Click(object sender, RoutedEventArgs e) => SelectAssistantTab("Before / After");

    private void RenderPowertrain(SavedTelemetrySession? selected, SavedTelemetrySession? baseline, RunComparison? conditions)
    {
        var evidence = selected?.Analysis.Diagnosis.Powertrain ?? new PowertrainDiagnosis();
        PowertrainSummaryText.Text = selected is null ? "Select a saved run to review gearing and ECU evidence." : evidence.Summary;
        PowertrainQuickText.Text = PowertrainSummaryText.Text;
        PowertrainTargetText.Text = evidence.TargetContext;
        PowertrainSetupText.Text = evidence.SetupContext;
        PowertrainEcuText.Text = evidence.EcuContext;
        PowertrainNextTestText.Text = evidence.NextTest;
        PowertrainLimitationsText.Text = evidence.Limitations;
        PowertrainGearGrid.ItemsSource = evidence.Gears;
        PowertrainPhaseGrid.ItemsSource = evidence.Phases;
        PowertrainEventGrid.ItemsSource = evidence.Events;
        PowertrainEventGrid.SelectedIndex = evidence.Events.Count > 0 ? 0 : -1;
        PowertrainMapGrid.ItemsSource = evidence.MapPoints;
        var comparison = selected is null ? new PowertrainComparisonReport("Select a saved run and an earlier baseline.", []) : PowertrainComparison.Build(selected, baseline, conditions);
        PowertrainComparisonText.Text = (baseline is null ? "" : "Baseline: " + baseline.DisplayName + "\n") + comparison.Summary;
        PowertrainComparisonGrid.ItemsSource = comparison.Rows;
    }
}
