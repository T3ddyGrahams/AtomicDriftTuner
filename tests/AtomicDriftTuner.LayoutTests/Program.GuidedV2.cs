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
            var mode = (ComboBox)wizard.FindName("InterviewFocusBox");
            var integrations = (StackPanel)wizard.FindName("IntegrationInterviewPanel");
            mode.SelectedValue = TuningFocus.CarSetupOnly;
            Check(mode.SelectedItem.ToString() == "Car setup only", "mode selector exposed implementation text");
            Check(integrations.Visibility == Visibility.Collapsed && ((FrameworkElement)wizard.FindName("OptionalSimHubCard")).Visibility == Visibility.Collapsed, "car-only asked for FFB integrations");
            var missing = (List<string>)Call(wizard, "BuildUnresolvedPathMessages")!;
            Check(!missing.Any(x => x.Contains("SimHub")), "car-only save warned about an optional SimHub path");
            var p = (GuidedPreferences)Call(wizard, "InterviewPreferences")!;
            Check(p.Focus == TuningFocus.CarSetupOnly && p.WantLiveConnection, "changing mode erased integration preferences");
            mode.SelectedValue = TuningFocus.FfbOnly;
            Check(integrations.Visibility == Visibility.Visible && ((TextBlock)wizard.FindName("InterviewFocusText")).Text.Contains("wheel feel"), "FFB path not explained");
            ((ComboBox)wizard.FindName("SimHubChoiceBox")).SelectedItem = "No";
            ((ComboBox)wizard.FindName("AzomChoiceBox")).SelectedItem = "No";
            Check(((CheckBox)wizard.FindName("UseLiveGuidanceBox")).IsChecked == false, "No integrations left live guidance selected");
            Check(((TextBlock)wizard.FindName("InterviewInstructionsText")).Text.Contains("MANUAL FFB"), "manual steps not shown");
            ((CheckBox)wizard.FindName("InterviewHelpCheck")).IsChecked = false;
            Check(!((GuidedPreferences)Call(wizard, "InterviewPreferences")!).ShowDetailedHelp, "help toggle lost");
            var content = (FrameworkElement)wizard.Content;
            foreach (var focus in Enum.GetValues<TuningFocus>())
            {
                mode.SelectedValue = focus; Layout(content, new Size(680, 850));
                ((ScrollViewer)wizard.FindName("SetupBodyScroll")).ScrollToHome(); Layout(content, new Size(680, 850));
                Render(content, new Size(680, 850), Path.Combine(output, $"Workflow-Interview-{focus}.png"));
            }
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
        }
        finally { assistant.Close(); }
        Progress($"PASS {checks} guided mode UI assertions with isolated preferences, recordings and reviews.");
    }
}
