using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class CarSetupWindow
{
    private void ShowPhysics(CarPhysicsSnapshot? snapshot = null)
    {
        snapshot ??= new CarPhysicsService().Read(_input.Car, _analysis?.Parameters, UseCarPhysicsCheck.IsChecked == true);
        PhysicsStatusText.Text = snapshot.Status;
        PhysicsDetailsText.Text = snapshot.Details;
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
