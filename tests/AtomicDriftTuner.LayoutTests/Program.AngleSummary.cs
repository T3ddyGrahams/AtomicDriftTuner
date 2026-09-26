using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckAngleSummary(string output)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Angle/summary UI: " + why); checks++; }
        static object? Get(object o, string field) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
        static void Set(object o, string field, object? value) => o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
        static object? Call(object o, string method, params object?[] args) => o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
        var input = new TuneInput { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
        input.Car.SourceFolderName = "isolated-angle-ui";
        var directory = Path.Combine(output, "angle-summary-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var setup = new CarSetupWindow(input);
        try
        {
            var store = (CarBehaviorProfileStore)Get(setup, "_behaviorStore")!;
            Set(store, "_directory", directory); Set(store, "_path", Path.Combine(directory, "car-behavior-targets.json"));
            Call(setup, "ApplyBehaviorToControls", new CarBehaviorTarget());
            var choice = (ComboBox)setup.FindName("AngleGoalBox"); choice.SelectedIndex = 2;
            Check(((TextBlock)setup.FindName("AngleGoalHelpText")).Text.Contains("65–80"), "Extreme preset target not explained");
            ((ComboBox)setup.FindName("BehaviorPresetBox")).SelectedItem = "Stable & Forgiving";
            var goal = (CarBehaviorTarget)Call(setup, "ReadBehaviorFromControls")!;
            Check(goal.HasAngleGoal && goal.AngleStability == 2, "Handling preset erased angle goal");
            var range = (Expander)setup.FindName("AngleRangeExpander");
            Check(!range.IsExpanded && range.IsEnabled, "Optional custom range is not initially collapsed");
            range.IsExpanded = true;
            ((CheckBox)setup.FindName("CustomAngleRangeCheck")).IsChecked = true;
            ((Slider)setup.FindName("AngleMinSlider")).Value = 79;
            Check(((Slider)setup.FindName("AngleMaxSlider")).Value >= 84, "Range controls allowed an inverted band");
            ((Slider)setup.FindName("AngleMaxSlider")).Value = 78;
            goal = (CarBehaviorTarget)Call(setup, "ReadBehaviorFromControls")!;
            Check(goal.ValidAngleGoal && goal.AngleMinDeg == 73 && goal.AngleMaxDeg == 78, "Custom controls did not retain valid endpoints");
            Call(setup, "SaveBehavior_Click", setup, new RoutedEventArgs());
            Check(RunHistoryStore.SameBehavior(store.Load(input), goal), "Save button lost the custom goal");
            Check(((TextBlock)setup.FindName("BehaviorStatusText")).Text.Contains("Sustain more extreme"), "Save result did not confirm angle goal");
            Call(setup, "ApplyBehaviorToControls", new CarBehaviorTarget());
            Call(setup, "LoadBehaviorTarget");
            Check(choice.SelectedIndex == 2 && ((Slider)setup.FindName("AngleMinSlider")).Value == 73, "Goal did not reappear when reloaded");
            var content = (FrameworkElement)setup.Content; var size = new Size(680, 900);
            Layout(content, size); ((ScrollViewer)setup.FindName("CarSetupScroll")).ScrollToHome(); Layout(content, size);
            Render(content, size, Path.Combine(output, "AngleGoal-Custom.png"));
        }
        finally { setup.Close(); }

        var assistant = new TuningAssistantWindow(input);
        try
        {
            var tabs = (TabControl)assistant.FindName("AssistantTabs"); var toggle = (CheckBox)assistant.FindName("AdvancedTelemetryToggle");
            assistant.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Check(toggle.IsChecked != true && tabs.Items.OfType<TabItem>().Count(t => t.Visibility == Visibility.Visible && t.Header.ToString() != "Track & sections") == 4, "Expert tables visible by default or powertrain review hidden");
            Check(((TabItem)tabs.SelectedItem).Header.ToString() == "Your next step", "Assistant does not open at the next step");
            var next = new AssistantNextStep { Goal = "Sustain more extreme angle (65–80°)", Noticed = "Synthetic example: your longest controlled hold was 4.0s.",
                Confidence = "Useful pattern; verify with another run", Instruction = "Keep this setup and record the same section again.", Why = "Synthetic UI test, not a driver's run.", Action = "Evidence", ActionLabel = "Review the attempts" };
            Call(assistant, "RenderNextStep", next);
            Check(((TextBlock)assistant.FindName("NextGoalText")).Text == next.Goal && ((Button)assistant.FindName("NextActionButton")).Content.ToString() == next.ActionLabel, "Summary does not bind goal/action");
            Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            Check(toggle.IsChecked == true && ((TabItem)tabs.SelectedItem).Header.ToString() == "Phase Evidence", "Review attempts did not open evidence");
            toggle.IsChecked = false;
            Check(((TabItem)tabs.SelectedItem).Header.ToString() == "Your next step", "Hiding evidence left a hidden tab selected");
            next.Action = "Compare"; Call(assistant, "RenderNextStep", next); Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            Check(((TabItem)tabs.SelectedItem).Header.ToString() == "Before / After" && ((Expander)assistant.FindName("ComparisonReasonsExpander")).IsExpanded, "Comparison action did not reveal reasons");
            next.Action = "Review"; Call(assistant, "RenderNextStep", next); Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            Check(((TabItem)tabs.SelectedItem).Header.ToString() == "Before / After", "Feedback action went to the wrong tab");
            var recommendation = new AssistantRecommendation { Area = RecommendationArea.General, Domain = "Throttle and rear response", Priority = "Repeat inputs", Confidence = "MEDIUM", Change = "Keep this setup and repeat this section." };
            var report = new TuningAssistantReport { Recommendations = [recommendation] };
            var history = new RunHistoryStore(Path.Combine(directory, "history")); var driver = history.GetOrCreateDriver("Angle tester");
            var run = new SavedTelemetrySession { Session = new() { Context = new() { DriverId = driver.Id, DriverName = driver.Name, Tune = history.CaptureTune(input, driver, "Baseline", new(), null) } } };
            Set(assistant, "_report", report); Set(assistant, "_reportSession", run);
            ((DataGrid)assistant.FindName("RecommendationGrid")).ItemsSource = report.Recommendations;
            next.Action = "Plan"; next.Recommendation = recommendation; Call(assistant, "RenderNextStep", next);
            Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            Check(((TextBlock)assistant.FindName("StatusText")).Text.Contains("dashboard"), "Missing plan callback silently reported success");
            bool dispatched = false;
            assistant.RecommendationTestRequested += (selected, change) => { dispatched = selected == run && change.Contains(recommendation.Change); };
            Call(assistant, "NextAction_Click", assistant, new RoutedEventArgs());
            Check(dispatched, "Primary action did not dispatch the selected recorded test");
        }
        finally { assistant.Close(); }
        Progress($"PASS {checks} angle goal and next-step UI assertions with isolated car profiles and history.");
    }
    private static void SeedAngleSummary(FrameworkElement root)
    {
        if (root.FindName("AngleGoalBox") is ComboBox choice)
        {
            choice.ItemsSource = new[] { "Keep my current angle", "Sustain more angle", "Sustain more extreme angle" }; choice.SelectedIndex = 2;
            ((TextBlock)root.FindName("AngleGoalHelpText")).Text = "Sustain more extreme angle (65–80°). ADT checks how long you hold it, speed retained and recovery. Save this goal before recording a new baseline.";
            ((TextBlock)root.FindName("AngleRangeText")).Text = "Starting target: 65–80° of body angle relative to travel. Choose a range suited to this car.";
        }
        if (root.FindName("NextGoalText") is not TextBlock goal) return;
        goal.Text = "Sustain more extreme angle (65–80°) · Predictable handling at angle";
        ((TextBlock)root.FindName("NextNoticedText")).Text = "Synthetic preview: you held your target band for 4.0s at its longest. A settled return was observed after 6 of 6 complete attempts.";
        ((TextBlock)root.FindName("NextConfidenceText")).Text = "Useful pattern; verify with another run";
        ((TextBlock)root.FindName("NextInstructionText")).Text = "Keep this setup. Record the same section again so we can check whether the result repeats.";
        ((Button)root.FindName("NextActionButton")).Content = "Back to dashboard";
    }
}
