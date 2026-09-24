using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtomicDriftTuner;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckLogitechG27(string output)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("G27 UI: " + why); checks++; }
        static IEnumerable<DependencyObject> Visuals(DependencyObject root) {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Visuals(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        var dir = Path.Combine(output, "g27-plan-" + Guid.NewGuid().ToString("N")); var store = new LogitechG27Store(dir);
        var window = new LogitechG27Window(store);
        try {
            void Click(string method) => typeof(LogitechG27Window).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [window, new RoutedEventArgs()]);
            var rows = (ItemsControl)window.FindName("LogitechRows"); var ac = (ItemsControl)window.FindName("AcRows");
            var rotation = rows.Items.Cast<LogitechG27Window.SettingRow>().Single(r => r.Key == "DegreesOfRotation");
            Check(rows.Items.Count == 5 && ac.Items.Count == 7, "Missing control rows.");
            Check(rotation.Min == 40 && rotation.Max == 900 && rows.Items.Cast<LogitechG27Window.SettingRow>().Where(r => r != rotation).All(r => r.Min == 0 && r.Max == 150), "Ranges differ from tester's controls.");
            Check(!Directory.Exists(dir), "Opening the form wrote defaults.");
            var root = (FrameworkElement)window.Content;
            foreach (var size in new[] { new Size(430, 430), new Size(700, 900), new Size(1200, 700) }) {
                Layout(root, size); AssertVisible(root, "SavePlanButton", size);
                foreach (var name in new[] { "SoftwareVersionBox", "CenteringCheck", "CombinedCheck", "GameAdjustCheck", "StartingPointButton" })
                    AssertReachableByScrolling(root, "G27BodyScroll", name, size);
                foreach (var box in Visuals(root).OfType<TextBox>().Where(b => b.DataContext is LogitechG27Window.SettingRow)) {
                    var scroll = (ScrollViewer)window.FindName("G27BodyScroll"); scroll.ScrollToHome(); Layout(root, size);
                    var bounds = box.TransformToAncestor(scroll).TransformBounds(new Rect(box.RenderSize));
                    scroll.ScrollToVerticalOffset(Math.Max(0, bounds.Top - 4)); Layout(root, size);
                    bounds = box.TransformToAncestor(scroll).TransformBounds(new Rect(box.RenderSize));
                    Check(bounds.Top >= -1 && bounds.Bottom <= scroll.ViewportHeight + 1 && bounds.Right <= scroll.ViewportWidth + 1, "Numeric field inaccessible.");
                }
            }
            foreach (var r in rows.Items.Cast<LogitechG27Window.SettingRow>()) r.Value = r.Max.ToString();
            ((CheckBox)window.FindName("CenteringCheck")).IsChecked = true;
            ((CheckBox)window.FindName("CombinedCheck")).IsChecked = true;
            ((CheckBox)window.FindName("GameAdjustCheck")).IsChecked = false;
            Click("Save_Click"); var saved = store.Load();
            Check(window.SettingsSaved && saved.OverallEffectsStrength == 150 && saved.EnableCenteringSpring && saved.ReportCombinedPedals && !saved.AllowGameToAdjustSettings, "Save lost controls.");
            rotation.Value = "39"; Click("Save_Click");
            Check(store.Load().DegreesOfRotation == 900 && ((TextBlock)window.FindName("StatusText")).Text.StartsWith("Plan not saved"), "Invalid input overwrote saved plan.");
            Click("StartingPoint_Click");
            Check(store.Load().OverallEffectsStrength == 150 && !((CheckBox)window.FindName("CenteringCheck")).IsChecked!.Value, "Starting point applied or saved without explicit save.");
            var screenshotSize = new Size(820, 860); var body = (ScrollViewer)window.FindName("G27BodyScroll");
            body.ScrollToHome(); Layout(root, screenshotSize); Render(root, screenshotSize, Path.Combine(output, "G27-Settings.png"));
            body.ScrollToVerticalOffset(750); Layout(root, screenshotSize); Render(root, screenshotSize, Path.Combine(output, "G27-Controls.png"));
        } finally { window.Close(); }
        File.WriteAllText(Path.Combine(dir, "logitech-g27.json"), "null");
        var recovery = new LogitechG27Window(store);
        try { Check(((TextBlock)recovery.FindName("StatusText")).Text.Contains("could not load"), "Corrupt plan silently accepted."); }
        finally { recovery.Close(); }
        Progress($"PASS {checks} G27 UI checks plus scroll and footer reachability");
    }
}
