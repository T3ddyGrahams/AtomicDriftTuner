using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using AtomicDriftTuner.Engine;

namespace AtomicDriftTuner;
public partial class SetupWizardWindow
{
    private readonly GuidedWorkflowStore _guidedStore;
    private bool _interviewReady;
    private IntegrationState? _integrationState;
    private void InitializeGuidedInterview()
    {
        var p = _guidedStore.Preferences();
        InterviewFocusBox.ItemsSource = TuningFocusOptions.Current;
        InterviewFocusBox.SelectedItem = p.FocusChoiceConfirmed
            ? TuningFocusOptions.Current.FirstOrDefault(x => x.Focus == p.Focus) : null;
        InterviewHelpCheck.IsChecked = p.ShowDetailedHelp;
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
        DriverName = InterviewDriverBox.Text.Trim(),
        Focus = (InterviewFocusBox.SelectedItem as TuningFocusOptions.Option)?.Focus ?? TuningFocus.Both,
        FocusChoiceConfirmed = InterviewFocusBox.SelectedItem is TuningFocusOptions.Option,
        ShowDetailedHelp = InterviewHelpCheck.IsChecked == true
    };
    private void InterviewChanged(object sender, RoutedEventArgs e)
    {
        if (!_interviewReady) return;
        if ((ReferenceEquals(sender, SimHubChoiceBox) || ReferenceEquals(sender, AzomChoiceBox)) &&
            (SimHubChoiceBox.SelectedItem as string == "No" || AzomChoiceBox.SelectedItem as string == "No"))
            UseLiveGuidanceBox.IsChecked = false;
        _pathsEdited = true; UpdateInterviewInstructions(); UpdateControls();
    }
    private void InterviewSelectionChanged(object sender, SelectionChangedEventArgs e) => InterviewChanged(sender, e);
    private void InterviewTextChanged(object sender, TextChangedEventArgs e) => InterviewChanged(sender, e);
    private void UpdateInterviewInstructions()
    {
        var p = InterviewPreferences();
        var ffb = p.FocusChoiceConfirmed && TuningFocusOptions.IncludesFfb(p.Focus);
        InterviewFocusDescription.Text = p.FocusChoiceConfirmed ? TuningFocusOptions.Description(p.Focus)
            : "Choose Car tuning only or Car + FFB to continue. You can change this later; previous runs stay saved.";
        InterviewOverviewText.Text = p.FocusChoiceConfirmed ? GuidedWorkflowEngine.Overview(p.Focus) : "";
        InterviewInstructionsText.Text = p.FocusChoiceConfirmed ? GuidedWorkflowEngine.Instructions(p, _integrationState) : "";
        IntegrationInterviewPanel.Visibility = ffb ? Visibility.Visible : Visibility.Collapsed;
        UseLiveGuidanceBox.IsEnabled = !_busy && ffb && p.SimHub != "No" && p.Azom != "No";
        OptionalSimHubCard.Visibility = ffb && p.WantLiveConnection && p.SimHub != "No" && p.Azom != "No" ? Visibility.Visible : Visibility.Collapsed;
        TestEverythingButton.Content = ffb && p.WantLiveConnection && p.SimHub != "No" && p.Azom != "No"
            ? "Check Paths & Connection" : "Check AC Paths";
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
