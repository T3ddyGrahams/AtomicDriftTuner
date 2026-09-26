using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckCarTest(string output)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Car test UI: " + why); checks++; }
        static object? Get(object o, string field) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
        static void Set(object o, string field, object? value) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        static object? Call(object o, string method, params object?[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        static Button Button(Window w, string name) => (Button)w.FindName(name);
        static TextBlock Text(Window w, string name) => (TextBlock)w.FindName(name);
        var f = new CarTestFixture(output);
        var stages = new List<PitSetupPlan>(); var prepared = new List<(string Text, string? Path)>();
        var window = new CarSetupTestWindow(f.Input, f.Run, f.Report) {
            StageHandler = (a, label) => { stages.Add(new PitSetupPlanService().Create(a, f.Input.Car.SourceFolderName!, label)); return "Staged for review."; },
            TestPrepared = (test, path) => prepared.Add((test.Description, path)) };
        try
        {
            Check(!Button(window, "SaveTestButton").IsEnabled && !Button(window, "StageTestButton").IsEnabled, "Actions enabled before verified baseline");
            Call(window, "LoadBaseline", f.Baseline);
            Check(Button(window, "SaveTestButton").IsEnabled && Button(window, "StageTestButton").IsEnabled, "Matching baseline did not enable actions: " + Text(window, "TestStatusText").Text);
            Check(Text(window, "ChangesText").Text.Contains("28 psi → 26 psi") && Text(window, "AdjustmentHelpText").Text.Contains("Watch for"), "Exact change/tradeoff missing");
            Check(stages.Count == 0 && prepared.Count == 0, "Preview applied/staged/tracked without action");
            var content = (FrameworkElement)window.Content;
            foreach (var size in new[] { new Size(430, 430), new Size(680, 900), new Size(1200, 540) })
            {
                Layout(content, size);
                foreach (var name in new[] { "SaveTestButton", "StageTestButton" }) AssertVisible(content, name, size);
                foreach (var name in new[] { "BrowseBaselineButton", "ChoiceBox", "ChangesText", "AdjustmentHelpText" })
                    AssertReachableByScrolling(content, "TestBodyScroll", name, size);
                ((ScrollViewer)window.FindName("TestBodyScroll")).ScrollToHome(); Layout(content, size);
                Render(content, size, Path.Combine(output, $"CarTest-{size.Width}.png"));
            }
            var saved = Path.Combine(f.DirectoryPath, "Test.ini"); Call(window, "SaveTo", saved);
            Check(File.Exists(saved) && prepared.Count == 1 && prepared[0].Path == saved && prepared[0].Text.Contains("PRESSURE_LR 28 → 26"), "Save did not track exact test");
            Check(Text(window, "TestStatusText").Text.Contains("Load it") && stages.Count == 0, "Save was described as applied or staged");
            Call(window, "StageTest_Click", window, new RoutedEventArgs());
            Check(stages.Count == 1 && prepared.Count == 2 && prepared[1].Path is null && stages[0].Changes.Count == 2, "Stage did not separate explicit apply or retained obsolete path");
            Check(Text(window, "TestStatusText").Text.Contains("Save & Apply Tune"), "Explicit companion apply step missing");
            File.AppendAllText(f.Definitions, "\n; changed externally"); Call(window, "StageTest_Click", window, new RoutedEventArgs());
            Check(stages.Count == 1 && !Button(window, "SaveTestButton").IsEnabled && !Button(window, "StageTestButton").IsEnabled, "Stale car data reached staging");
        }
        finally { window.Close(); }

        foreach (var mode in new[] { "1", "2" })
        {
            var clickFixture = new CarTestFixture(output, mode);
            var clickStages = new List<PitSetupPlan>();
            RecommendationTest? exactTest = null;
            var clicks = new CarSetupTestWindow(clickFixture.Input, clickFixture.Run, clickFixture.Report)
            {
                StageHandler = (a, label) => { clickStages.Add(new PitSetupPlanService().Create(a, clickFixture.Input.Car.SourceFolderName!, label)); return "Staged for review."; },
                TestPrepared = (test, _) => exactTest = test
            };
            try
            {
                Call(clicks, "LoadBaseline", clickFixture.Baseline);
                Check(Button(clicks, "SaveTestButton").IsEnabled && Button(clicks, "StageTestButton").IsEnabled, "Verified clicks were blocked");
                Check(Text(clicks, "ChangesText").Text.Contains(mode == "2" ? "38 psi → 36 psi" : "28 psi → 26 psi"), "Click counts were mislabeled as pressure");
                var body = (FrameworkElement)clicks.Content; var size = new Size(430, 560);
                Layout(body, size); AssertVisible(body, "StageTestButton", size);
                Render(body, size, Path.Combine(output, $"CarTest-ClickMode-{mode}.png"));
                Call(clicks, "StageTest_Click", clicks, new RoutedEventArgs());
                Check(clickStages.Single().Changes.All(c => c.Before == 28 && c.After == 26), "Displayed units leaked into pit command");
                Check(exactTest is not null && exactTest.Changes.Count == 2 && exactTest.Changes.All(c => c.Before == 28 && c.After == 26), "Exact test lost raw encoded values");
                var saved = Path.Combine(clickFixture.DirectoryPath, "ClickTest.ini"); Call(clicks, "SaveTo", saved);
                Check(new AssettoCorsaSetupService().LoadBaseline(saved, clickFixture.Input.Car).Parameters.Single(p => p.Section == "PRESSURE_LR").CurrentValue == 26, "Click export did not match preview and staged test");
            }
            finally { clicks.Close(); }
        }

        var missing = new CarTestFixture(output);
        var partial = new CarSetupTestWindow(missing.Input, missing.Run, missing.Report) { TestPrepared = (_, _) => throw new InvalidOperationException("tracking fixture failure") };
        try
        {
            Call(partial, "LoadBaseline", missing.Baseline);
            Check(!Button(partial, "StageTestButton").IsEnabled, "Absent stage callback enabled action");
            var path = Path.Combine(missing.DirectoryPath, "Prepared.ini"); Call(partial, "SaveTo", path);
            Check(File.Exists(path) && Text(partial, "TestStatusText").Text.Contains("tracking was not saved") && Text(partial, "TestStatusText").Text.Contains("Saved Prepared.ini"), "History failure concealed file success");
            Call(partial, "LoadBaseline", Path.Combine(missing.DirectoryPath, "missing.ini"));
            Check(!Button(partial, "SaveTestButton").IsEnabled && ((ComboBox)partial.FindName("ChoiceBox")).SelectedItem is null, "Bad new baseline retained old preview");
        }
        finally { partial.Close(); }

        var parentFixture = new CarTestFixture(output); var assistant = new TuningAssistantWindow(parentFixture.Input);
        try
        {
            var behavior = Get(assistant, "_behaviorStore")!;
            Set(behavior, "_directory", parentFixture.DirectoryPath); Set(behavior, "_path", Path.Combine(parentFixture.DirectoryPath, "behavior.json"));
            ((CarBehaviorProfileStore)behavior).Save(parentFixture.Input, parentFixture.Run.Session.Context!.Tune!.DesiredBehavior);
            var sessions = (ComboBox)assistant.FindName("SessionBox"); sessions.ItemsSource = new[] { parentFixture.Run }; sessions.SelectedIndex = 0;
            Set(assistant, "_report", parentFixture.Report); Set(assistant, "_reportSession", parentFixture.Run);
            assistant.UseTuningFocus(TuningFocus.CarSetupOnly);
            Call(assistant, "RenderNextStep", parentFixture.Report.NextStep);
            Check(((CheckBox)assistant.FindName("AdvancedTelemetryToggle")).IsChecked != true && ((Border)assistant.FindName("CarTestCard")).Visibility == Visibility.Visible && Button(assistant, "ReviewCarTestButton").IsEnabled, "Normal review hidden behind Advanced");
            Check(Button(assistant, "NextActionButton").Content.ToString() == "Review one car setup change", "Primary action remains generic plan");
            var history = new List<string>(); var savedPaths = new List<string>();
            assistant.StructuredTestRequested += (run, test) => { Check(ReferenceEquals(run, parentFixture.Run) && RecommendationTestService.Valid(test) && test.Changes.Count == 2, "Wrong or incomplete baseline test tracked"); history.Add(test.Description); };
            assistant.GuidedSetupSaved += (run, path) => savedPaths.Add(path);
            assistant.StagePitSetupHandler = (a, label) => { new PitSetupPlanService().Create(a, parentFixture.Input.Car.SourceFolderName!, label); return "Staged."; };
            Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            var child = Get(assistant, "_guidedSetupWindow") as CarSetupTestWindow;
            Check(child is not null && history.Count == 0 && !sessions.IsEnabled && !((ComboBox)assistant.FindName("HistoryDriverBox")).IsEnabled, "Primary action did not open review and lock run without saving a plan");
            Call(child!, "LoadBaseline", parentFixture.Baseline); Call(child!, "StageTest_Click", child, new RoutedEventArgs());
            Check(history.Count == 1 && history[0].Contains("PRESSURE_RR 28 → 26") && savedPaths.Single() == "", "Stage failed to track exact test and clear old manual attachment");
            child!.Close();
            Check(Get(assistant, "_guidedSetupWindow") is null && sessions.IsEnabled, "Child close did not unlock selected run");
            assistant.UseTuningFocus(TuningFocus.FfbOnly);
            Check(((Border)assistant.FindName("CarTestCard")).Visibility == Visibility.Collapsed, "Car test shown in FFB-only mode");
            assistant.UseTuningFocus(TuningFocus.CarSetupOnly);
            var currentReport = (TuningAssistantReport)Get(assistant, "_report")!;
            currentReport.NextStep.Action = "Compare"; Call(assistant, "RenderNextStep", currentReport.NextStep);
            Check(!Button(assistant, "ReviewCarTestButton").IsEnabled && Text(assistant, "CarTestSummaryText").Text.Contains("No car setup change"), "Comparison gate did not explain hold");
        }
        finally { assistant.Close(); }
        Progress($"PASS {checks} focused car setup review assertions plus responsive layout checks.");
    }
}
