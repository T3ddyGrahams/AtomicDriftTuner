using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class CarSetupWindow
{
    private void ShowPhysics(CarPhysicsSnapshot? snapshot = null, IEnumerable<string>? warnings = null)
    {
        if (snapshot is null)
        {
            snapshot = new CarPhysicsService().Read(_input.Car, _analysis?.Parameters, UseCarPhysicsCheck.IsChecked == true);
            if (_analysis?.BaselineIdentityVerified == false) snapshot = snapshot with {
                DecodedSettings = snapshot.DecodedSettings.Select(d => d.Status == DecodedSetupSetting.Verified ? d with {
                    Status = DecodedSetupSetting.Partial, Explanation = "Baseline car identity is unverified; this is only a candidate interpretation. " + d.Explanation } : d).ToArray() };
        }
        PhysicsStatusText.Text = snapshot.Status;
        PhysicsDetailsText.Text = snapshot.Details;
        DecodedSettingsText.Text = snapshot.DecodingDetails;
        DecodeWarningsText.Text = string.Join("\n", warnings ?? _analysis?.DecodeWarnings ?? []);
        var rows = snapshot.DecodedSettings;
        DecodingSummaryText.Text = rows.Count == 0 ? "Load a baseline to check its gearing and ECU selections." :
            $"Saved settings: {rows.Count(x => x.Status == DecodedSetupSetting.Verified)} verified mapping(s), " +
            $"{rows.Count(x => x.Status == DecodedSetupSetting.Partial)} partial, {rows.Count(x => x.Status == DecodedSetupSetting.Unsupported)} unsupported. " +
            "Mapping checks do not prove which settings are active in game. Existing tuning guidance remains available.";
    }

    private void PhysicsOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        ClearAnalysis(clearGrid: true);
        ShowPhysics();
        SetupStatusText.Text = "Car physics import changed. Load your baseline and generate again.";
    }

    private void RefreshPhysics_Click(object sender, RoutedEventArgs e)
    {
        InvalidateGeneratedRecommendations("Car physics refreshed. Generate again before saving or staging.");
        ShowPhysics();
    }
}
