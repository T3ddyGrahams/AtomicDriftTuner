using System.Windows;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner;

public partial class CarSetupWindow
{
    public Func<CarSetupAnalysis, string, string>? StagePitSetupHandler { get; set; }
    public Func<string>? ClearPendingPitSetupHandler { get; set; }

    private void StagePitSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_analysis is null || _generatedSignature is null || _analysis.ChangedCount == 0)
                throw new InvalidOperationException("Generate and review the setup changes first.");
            var mode = AggressivenessBox.SelectedItem is SetupAggressiveness selected ? selected : SetupAggressiveness.Balanced;
            if (_generatedSignature != BuildGenerationSignature(SelectedPath(), mode, ReadBehaviorFromControls()))
            {
                InvalidateGeneratedRecommendations("Setup controls or the baseline changed.");
                throw new InvalidOperationException("Generate again before staging the setup so it matches the changes shown.");
            }
            if (StagePitSetupHandler is null) throw new InvalidOperationException("Open the car setup tuner from the main ADT window to stage an in-game setup.");
            SetupStatusText.Text = StagePitSetupHandler(_analysis, "ADT " + _input.Intent.Name);
        }
        catch (Exception ex) { SetupStatusText.Text = "Setup was not staged: " + ex.Message; }
    }

    private void ClearPendingPitSetup_Click(object sender, RoutedEventArgs e)
    {
        try { SetupStatusText.Text = ClearPendingPitSetupHandler?.Invoke() ?? "Open this tool from the main ADT window first."; }
        catch (Exception ex) { SetupStatusText.Text = ex.Message; }
    }
}
