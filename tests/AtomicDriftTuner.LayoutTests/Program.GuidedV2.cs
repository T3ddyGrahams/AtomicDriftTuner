using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckGuidedModes(string output)
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Guided modes UI: " + message); checks++; }
        static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        var root = Path.Combine(output, "guided-modes-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var preferences = new GuidedWorkflowStore(Path.Combine(root, "workflow"));
        preferences.SavePreferences(new() { Completed = true, SimHub = "Yes", Azom = "Yes", WantLiveConnection = true });
        var settings = new AppSettingsStore();
        foreach (var (field, value) in new[] { ("_directory", root), ("_path", Path.Combine(root, "settings.json")), ("_backupPath", Path.Combine(root, "settings.backup.json")) }) Set(settings, field, value);
        settings.Save(settings.Load());
        var wizard = new SetupWizardWindow(false, preferences, settings);
        try
        {
            var integrations = (StackPanel)wizard.FindName("IntegrationInterviewPanel");
            var focusBox = (ComboBox)wizard.FindName("InterviewFocusBox");
            Check(focusBox.Items.Count == 2 && focusBox.Items.Cast<TuningFocusOptions.Option>().All(x => x.Focus != TuningFocus.FfbOnly), "interview did not offer exactly the two supported choices");
            Check(focusBox.SelectedIndex == -1 && !((Button)wizard.FindName("SaveButton")).IsEnabled, "unconfirmed user could save a preselected mode");
            Check(integrations.Visibility == Visibility.Collapsed, "unconfirmed user saw optional integration questions before choosing scope");
            focusBox.SelectedItem = TuningFocusOptions.Current.Single(x => x.Focus == TuningFocus.Both);
            var p = (GuidedPreferences)Call(wizard, "InterviewPreferences")!;
            Check(p.Focus == TuningFocus.Both && p.FocusChoiceConfirmed && p.WantLiveConnection && !p.ShowDetailedHelp, "combined choice, simple help or integration preferences lost");
            Check(integrations.Visibility == Visibility.Visible, "optional integration questions disappeared");
            focusBox.SelectedItem = TuningFocusOptions.Current.Single(x => x.Focus == TuningFocus.CarSetupOnly);
            p = (GuidedPreferences)Call(wizard, "InterviewPreferences")!;
            Check(p.Focus == TuningFocus.CarSetupOnly && p.WantLiveConnection && p.SimHub == "Yes" && p.Azom == "Yes", "car-only selection erased saved integration answers");
            Check(integrations.Visibility == Visibility.Collapsed && ((Border)wizard.FindName("OptionalSimHubCard")).Visibility == Visibility.Collapsed, "car-only retained the live-connection path UI");
            Check(((TextBlock)wizard.FindName("InterviewInstructionsText")).Text.Contains("not needed"), "car-only instructions still required FFB tools");
            var carMissing = (List<string>)Call(wizard, "BuildUnresolvedPathMessages")!;
            Check(!carMissing.Any(x => x.Contains("SimHub")), "retained live preference caused a car-only connection warning");
            preferences.SavePreferences(p);
            Check(preferences.Preferences().Focus == TuningFocus.CarSetupOnly && preferences.Preferences().FocusChoiceConfirmed, "saved car-only choice reopened as combined");
            var carContent = (FrameworkElement)wizard.Content; Layout(carContent, new Size(680, 850));
            Render(carContent, new Size(680, 850), Path.Combine(output, "Workflow-Interview-CarOnly.png"));
            focusBox.SelectedItem = TuningFocusOptions.Current.Single(x => x.Focus == TuningFocus.Both);
            Check(((CheckBox)wizard.FindName("UseLiveGuidanceBox")).IsChecked == true && integrations.Visibility == Visibility.Visible, "returning to combined did not restore connection choices");
            ((ComboBox)wizard.FindName("SimHubChoiceBox")).SelectedItem = "No";
            ((ComboBox)wizard.FindName("AzomChoiceBox")).SelectedItem = "No";
            Check(((CheckBox)wizard.FindName("UseLiveGuidanceBox")).IsChecked == false, "No integrations left live guidance selected");
            Check(((TextBlock)wizard.FindName("InterviewInstructionsText")).Text.Contains("Manual FFB"), "simple manual steps not shown");
            var missing = (List<string>)Call(wizard, "BuildUnresolvedPathMessages")!;
            Check(!missing.Any(x => x.Contains("SimHub")), "manual setup warned about an optional SimHub path");
            ((CheckBox)wizard.FindName("InterviewHelpCheck")).IsChecked = false;
            Check(!((GuidedPreferences)Call(wizard, "InterviewPreferences")!).ShowDetailedHelp, "help toggle lost");
            var content = (FrameworkElement)wizard.Content;
            foreach (var detailed in new[] { false, true })
            {
                ((CheckBox)wizard.FindName("InterviewHelpCheck")).IsChecked = detailed; Layout(content, new Size(680, 850));
                ((ScrollViewer)wizard.FindName("SetupBodyScroll")).ScrollToHome(); Layout(content, new Size(680, 850));
                Render(content, new Size(680, 850), Path.Combine(output, $"Workflow-Interview-{(detailed ? "Detailed" : "Simple")}.png"));
            }
            focusBox.SelectedIndex = -1;
            Check(!((GuidedPreferences)Call(wizard, "InterviewPreferences")!).FocusChoiceConfirmed && !((Button)wizard.FindName("SaveButton")).IsEnabled, "cleared choice retained confirmation");
        }
        finally { Set(wizard, "_closingAfterSave", true); wizard.Close(); }

        var input = new TuneInput(); input.Car.SourceFolderName = "isolated_guided_test_car";
        using var hub = new TelemetryHubService(); hub.Dispose();
        var recorder = new TelemetryWindow(input, hub);
        try
        {
            Set(Get(recorder, "_sessionStore")!, "<RootDirectory>k__BackingField", Path.Combine(root, "sessions"));
            var id = Guid.NewGuid().ToString("N");
            var plan = new RecordingPlan(id, "Test driver", "", "", "", "dry, same section", TuningFocus.FfbOnly, false);
            recorder.UseRecordingPlan(plan);
            Check(recorder.RecordingFocus == TuningFocus.FfbOnly && ((TextBlock)recorder.FindName("RecorderHelpText")).Text.Contains("only a snapshot"), "FFB setup snapshot explanation absent");
            Check(!((Expander)recorder.FindName("RecorderHelpExpander")).IsExpanded, "simple help preference ignored");
            var token = recorder.GetCompanionState(true).ControlVersion;
            recorder.UseRecordingPlan(plan with { Focus = TuningFocus.CarSetupOnly });
            Check(token != recorder.GetCompanionState(true).ControlVersion, "mode change reused companion concurrency token");
            Check(((Button)recorder.FindName("ApplyButton")).Visibility == Visibility.Collapsed && !(bool)Call(recorder, "CanApplyCurrentSuggestion")!, "car-only allowed FFB correction");
            Check(!((TextBlock)recorder.FindName("RecorderConfirmationText")).Text.Contains("generated"), "car-only claimed generated FFB was applied");
            Set(recorder, "_recording", true);
            recorder.UseRecordingPlan(plan);
            Check(recorder.RecordingFocus == TuningFocus.CarSetupOnly, "active recording changed modes");
            Set(recorder, "_recording", false);
            recorder.UseRecordingPlan(plan with { Focus = TuningFocus.CarSetupOnly, ShowDetailedHelp = true });
            ((TextBlock)recorder.FindName("StatusText")).Text = "Ready to prepare a car-setup-only recording. Connect to AC when the driving session is loaded.";
            var content = (FrameworkElement)recorder.Content; Layout(content, new Size(680, 900));
            Render(content, new Size(680, 900), Path.Combine(output, "Workflow-Recorder-CarOnly.png"));
        }
        finally { recorder.Close(); }

        var history = new RunHistoryStore(Path.Combine(root, "history")); var driver = history.GetOrCreateDriver("Reviewer");
        var tune = history.CaptureTune(input, driver, "Baseline", new(), null);
        var run = new SavedTelemetrySession { Session = new() { Context = new() { DriverId = driver.Id, DriverName = driver.Name, Tune = tune } } };
        var assistant = new TuningAssistantWindow(input);
        try
        {
            Set(assistant, "_history", history);
            var report = new TuningAssistantReport();
            report.Recommendations.Add(new() { Area = RecommendationArea.Ffb, Change = "FFB change" });
            report.Recommendations.Add(new() { Area = RecommendationArea.CarSetup, Change = "Car change" });
            report.Recommendations.Add(new() { Area = RecommendationArea.General, Change = "Inspect control" });
            Set(assistant, "_report", report);
            foreach (var focus in Enum.GetValues<TuningFocus>())
            {
                assistant.UseTuningFocus(focus);
                var rows = ((DataGrid)assistant.FindName("RecommendationGrid")).Items.Count;
                Check(rows == (focus == TuningFocus.Both ? 3 : 2) && report.Recommendations.Count == 3, "focus filtering lost or leaked recommendations");
            }
            assistant.UseTuningFocus(TuningFocus.CarSetupOnly);
            Call(assistant, "ApplyCalibration_Click", assistant, new RoutedEventArgs());
            Check(((TextBlock)assistant.FindName("StatusText")).Text.Contains("outside"), "car-only calibration handler not guarded");
            assistant.UseTuningFocus(TuningFocus.FfbOnly);
            Call(assistant, "OpenSetupWithGuidance_Click", assistant, new RoutedEventArgs());
            Check(((TextBlock)assistant.FindName("StatusText")).Text.Contains("outside"), "FFB-only setup handler not guarded");
            Set(assistant, "_reportSession", run);
            ((ComboBox)assistant.FindName("DriverRatingBox")).SelectedItem = "Better";
            ((ComboBox)assistant.FindName("NextActionBox")).SelectedItem = "Revert manually";
            ((TextBox)assistant.FindName("DriverNotesBox")).Text = "Driver feedback stays separate from telemetry.";
            Call(assistant, "SaveRunReview_Click", assistant, new RoutedEventArgs());
            Check(history.ListReviews(input, driver.Id).Single().NextAction == "Revert manually", "review action not saved");
            ((ComboBox)assistant.FindName("NextActionBox")).SelectedItem = "Undecided";
            Call(assistant, "RestoreReviewDraft", run);
            Check((string)((ComboBox)assistant.FindName("NextActionBox")).SelectedItem == "Revert manually", "saved next action did not reload");
            run.Analysis = PedalFixture();
            Call(assistant, "RenderReport", report, run, null);
            Check(((DataGrid)assistant.FindName("PedalGrid")).Items.Count == run.Analysis.Diagnosis.Pedals.Events.Count, "pedal events not bound by production report handler");
            Check(((DataGrid)assistant.FindName("PedalContextGrid")).Items.Count == 11, "pedal exposure missing");
            Check(((TextBlock)assistant.FindName("PedalLimitationsText")).Text.Contains("assists"), "pedal limitations missing");
            assistant.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Check(((TextBlock)assistant.FindName("PedalSelectedEvidenceText")).Text == run.Analysis.Diagnosis.Pedals.Events[0].Evidence, "selected-event explanation is not bound");
            Call(assistant, "ClearReport", "No run", "", "", "");
            Check(((DataGrid)assistant.FindName("PedalGrid")).Items.Count == 0 && ((DataGrid)assistant.FindName("PedalContextGrid")).Items.Count == 0, "cleared report retained stale pedal evidence");
        }
        finally { assistant.Close(); }
        Progress($"PASS {checks} guided mode UI assertions with isolated preferences, recordings and reviews.");
    }
}
