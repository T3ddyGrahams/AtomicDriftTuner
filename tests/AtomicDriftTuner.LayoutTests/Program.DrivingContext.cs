using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckDrivingContext(string output)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Driving context UI: " + why); checks++; }
        static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
        static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
        static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
        static IEnumerable<DependencyObject> Visuals(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        var f = new CarTestFixture(output); var window = new TuningAssistantWindow(f.Input);
        var unitsPath = Path.Combine(f.DirectoryPath, "units.json"); var units = new SpeedUnitPreferenceStore(unitsPath); units.Save(true);
        try
        {
            Set(window, "_speedUnits", units);
            var behaviorStore = (CarBehaviorProfileStore)Get(window, "_behaviorStore")!;
            Set(behaviorStore, "_directory", f.DirectoryPath); Set(behaviorStore, "_path", Path.Combine(f.DirectoryPath, "behavior.json"));
            behaviorStore.Save(f.Input, f.Run.Session.Context!.Tune!.DesiredBehavior);
            Call(window, "BuildReportForSelection", f.Run);
            var report = (TuningAssistantReport)Get(window, "_report")!;
            var grid = (DataGrid)window.FindName("DrivingContextGrid");
            var summary = (TextBlock)window.FindName("DrivingContextSummaryText");
            var detail = (TextBlock)window.FindName("DrivingContextEvidenceText");
            var selected = (TextBlock)window.FindName("DrivingContextSelectedText");
            var tabs = (TabControl)window.FindName("AssistantTabs"); var toggle = (CheckBox)window.FindName("AdvancedTelemetryToggle");
            var phase = tabs.Items.OfType<TabItem>().Single(t => t.Header.ToString() == "Phase Evidence");
            var body = (FrameworkElement)window.Content;
            Layout(body, new Size(680, 900));
            Check(report.ContextAssessments.Count == 2 && grid.Items.Count == 2 && grid.SelectedIndex == 0, "Production report did not populate/select condition evidence");
            Check(toggle.IsChecked != true && phase.Visibility == Visibility.Collapsed, "Detailed context overwhelms the default next-step screen");
            Check(((TextBlock)window.FindName("NextNoticedText")).Text.Contains("mph") && report.ContextAssessments.All(a => a.Behavior.Contains("mph")), "Saved MPH preference not reflected in real report handler");
            toggle.IsChecked = true; tabs.SelectedItem = phase;
            foreach (var size in new[] { new Size(430, 560), new Size(680, 900), new Size(1800, 620) })
            {
                Layout(body, size);
                foreach (var name in new[] { "DrivingContextSummaryText", "DrivingContextGrid", "DrivingContextEvidenceText", "PhaseGrid" })
                    AssertReachableByScrolling(body, "AssistantBodyScroll", name, size);
                grid.SelectedIndex = 1; Layout(body, size);
                var row = report.ContextAssessments[1];
                Check(detail.Text == row.Evidence && selected.Text == row.Behavior, "Selecting a condition did not reveal its complete explanation");
                Check(detail.TextWrapping == TextWrapping.Wrap && detail.ActualWidth <= size.Width && summary.TextWrapping == TextWrapping.Wrap, "Condition details clip at narrow widths");
                if (size.Width == 430)
                {
                    var scroller = Visuals(grid).OfType<ScrollViewer>().First();
                    Check(scroller.ScrollableWidth > 0, "Wide condition columns have no horizontal navigation");
                    scroller.ScrollToRightEnd(); Layout(body, size);
                    Check(scroller.HorizontalOffset > 0, "Remaining columns cannot be reached");
                    scroller.ScrollToHome();
                }
                grid.BringIntoView(); Layout(body, size);
                Render(body, size, Path.Combine(output, $"DrivingContext-{size.Width}.png"));
            }
            var oldLabel = Application.Current.Resources["FieldLabelBrush"]; var oldHeading = Application.Current.Resources["SectionHeadingBrush"];
            try
            {
                Application.Current.Resources["FieldLabelBrush"] = new SolidColorBrush(Colors.LimeGreen);
                Application.Current.Resources["SectionHeadingBrush"] = new SolidColorBrush(Colors.Gold);
                Layout(body, new Size(680, 900));
                Check(((SolidColorBrush)detail.Foreground).Color == Colors.LimeGreen && ((SolidColorBrush)selected.Foreground).Color == Colors.Gold, "Condition text ignores customizable theme colors");
            }
            finally { Application.Current.Resources["FieldLabelBrush"] = oldLabel; Application.Current.Resources["SectionHeadingBrush"] = oldHeading; }
            File.WriteAllText(unitsPath, "broken preference"); Call(window, "BuildReportForSelection", f.Run);
            report = (TuningAssistantReport)Get(window, "_report")!;
            Check(report.ContextAssessments.All(a => a.Behavior.Contains("km/h")) && summary.Text.Contains("could not load") && File.ReadAllText(unitsPath) == "broken preference", "Damaged display preference discarded the analysis or was overwritten");
            Call(window, "RenderDrivingContext", new object?[] { null }); Layout(body, new Size(680, 900));
            Check(grid.Items.Count == 0 && grid.SelectedIndex == -1 && summary.Text == "" && selected.Text == "", "Changing/clearing a run left stale contextual evidence");
            toggle.IsChecked = false; Layout(body, new Size(680, 900));
            Check(((TabItem)tabs.SelectedItem).Header.ToString() == "Your next step", "Closing advanced view stranded the selected tab");
        }
        finally { window.Close(); }
        Progress($"PASS {checks} driving-context UI assertions plus responsive layout checks.");
    }
}
