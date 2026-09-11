using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using AtomicDriftTuner.Engine;

namespace AtomicDriftTuner;
public partial class SetupWizardWindow
{
    private readonly GuidedWorkflowStore _guidedStore = new();
    private bool _interviewReady;
    private IntegrationState? _integrationState;
    private void InitializeGuidedInterview()
    {
        var p = _guidedStore.Preferences();
        SimHubChoiceBox.ItemsSource = new[] { "Not sure", "Yes", "No" };
        AzomChoiceBox.ItemsSource = new[] { "Not sure", "Yes", "No" };
        SimHubChoiceBox.SelectedItem = p.SimHub; AzomChoiceBox.SelectedItem = p.Azom;
        UseLiveGuidanceBox.IsChecked = p.WantLiveConnection;
        InterviewDriverBox.Text = p.DriverName;
        _interviewReady = true;
        UpdateInterviewInstructions();
    }
    private GuidedPreferences InterviewPreferences() => new()
    {
        Completed = true, SimHub = SimHubChoiceBox.SelectedItem as string ?? "Not sure",
        Azom = AzomChoiceBox.SelectedItem as string ?? "Not sure", WantLiveConnection = UseLiveGuidanceBox.IsChecked == true,
        DriverName = InterviewDriverBox.Text.Trim()
    };
    private void InterviewChanged(object sender, RoutedEventArgs e)
    {
        if (!_interviewReady) return;
        _pathsEdited = true; UpdateInterviewInstructions();
    }
    private void InterviewSelectionChanged(object sender, SelectionChangedEventArgs e) => InterviewChanged(sender, e);
    private void InterviewTextChanged(object sender, TextChangedEventArgs e) => InterviewChanged(sender, e);
    private void UpdateInterviewInstructions()
    {
        InterviewInstructionsText.Text = GuidedWorkflowEngine.Instructions(InterviewPreferences(), _integrationState);
        OptionalSimHubCard.Visibility = UseLiveGuidanceBox.IsChecked == true && SimHubChoiceBox.SelectedItem as string != "No" && AzomChoiceBox.SelectedItem as string != "No" ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void CheckGuidedConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _closed) return;
        SetBusy(true); CheckGuidedConnectionButton.IsEnabled = false;
        InterviewInstructionsText.Text = "Checking installation, running process, bridge and AZOM readback…";
        try
        {
            _integrationState = await new GuidedIntegrationService().CheckAsync(SimHubPathBox.Text, _settings.AzomLive.PipeName, _lifetimeCancellation.Token);
            if (_closed) return;
            var s = _integrationState;
            InterviewDetectionText.Text = $"SimHub folder: {(s.SimHubInstalled ? "found" : "not found")} · Running: {(s.SimHubRunning ? "yes" : "no")} · " +
                $"Bridge: {(s.BridgeConnected ? "connected" : s.BridgeInstalled ? "file found, offline" : "not verified")}\n" +
                $"AZOM: {(!s.BridgeConnected ? "unknown while bridge is offline" : s.AzomDetected ? "detected" : "not detected by bridge")} · Settings: {(s.SettingsReadable ? "readable" : "not verified")}";
            UpdateInterviewInstructions();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) { if (!_closed) InterviewDetectionText.Text = "Check could not finish: " + ex.Message; }
        finally { if (!_closed) { SetBusy(false); CheckGuidedConnectionButton.IsEnabled = true; } }
    }
}
