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
    private static void CheckCarPhysics(string output)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Car physics UI: " + why); checks++; }
        static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, [target, new RoutedEventArgs()]);
        var root = Path.Combine(output, "car-physics-" + Guid.NewGuid().ToString("N"));
        var car = Path.Combine(root, "physics_ui_fixture"); var data = Path.Combine(car, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "suspensions.ini"), "[FRONT]\nSPRING_RATE=80000\n");
        File.WriteAllText(Path.Combine(data, "drivetrain.ini"), "[TRACTION]\nTYPE=RWD\n[DIFFERENTIAL]\nPOWER=0.7\n");
        File.WriteAllText(Path.Combine(data, "tyres.ini"), "[FRONT_1]\nNAME=Selected compound\nWIDTH=0.275\n");
        File.WriteAllText(Path.Combine(data, "setup.ini"), "[DIFF_POWER]\nMIN=0\nMAX=100\nSTEP=1\n[TYRES]\nMIN=0\nMAX=1\nSTEP=1\n");
        var baseline = Path.Combine(root, "baseline.ini"); File.WriteAllText(baseline, "[DIFF_POWER]\nVALUE=60\n[TYRES]\nVALUE=1\n");
        var input = new TuneInput { Car = new() { Id = "physics_ui_fixture", SourceFolderPath = car, SourceFolderName = null } };
        var window = new CarSetupWindow(input, assistantBehaviorOverride: new());
        try
        {
            input.Car.SourceFolderName = "physics_ui_fixture";
            var choiceType = typeof(CarSetupWindow).GetNestedType("SetupChoice", BindingFlags.NonPublic)!;
            var choices = (ComboBox)window.FindName("BaselineBox");
            choices.ItemsSource = new[] { Activator.CreateInstance(choiceType, baseline)! }; choices.SelectedIndex = 0;
            var save = (Button)window.FindName("SaveGeneratedButton");
            var toggle = (CheckBox)window.FindName("UseCarPhysicsCheck");
            var status = (TextBlock)window.FindName("PhysicsStatusText");
            var details = (TextBlock)window.FindName("PhysicsDetailsText");
            void Generate() => Call(window, "GenerateSetup_Click");
            Generate();
            Check(save.IsEnabled && status.Text.StartsWith("Imported"), "Import/generation failed");
            Check(details.Text.Contains("275 mm") && details.Text.Contains("FRONT_1"), "Saved compound missing from details");
            var analysis = (CarSetupAnalysis)typeof(CarSetupWindow).GetField("_analysis", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Check(analysis.Parameters.Single(p => p.Section == "DIFF_POWER").PhysicsContext.Contains("70 %"), "Base context not attached to rows");
            toggle.IsChecked = false;
            Check(!save.IsEnabled && status.Text.Contains("off"), "Opt-out left stale generation");
            Generate(); Check(save.IsEnabled, "Opt-out prevented baseline-only workflow");
            toggle.IsChecked = true; Check(!save.IsEnabled, "Opt-in left stale generation");
            Generate(); Call(window, "RefreshPhysics_Click"); Check(!save.IsEnabled, "Refresh left stale generation");
            Generate();
            var theme = ThemeCatalog.Clone(ThemeCatalog.Presets[0]); theme.PrimaryText = "#D5FFFF"; theme.FieldLabel = "#90EE90";
            ThemeService.Apply(theme);
            var content = (FrameworkElement)window.Content;
            Layout(content, new Size(900, 800));
            Check(ThemeService.ToHex(((SolidColorBrush)status.Foreground).Color) == theme.PrimaryText, "Status ignores custom color");
            Check(ThemeService.ToHex(((SolidColorBrush)details.Foreground).Color) == theme.FieldLabel, "Details ignore custom color");
            ThemeService.Apply(ThemeCatalog.Presets[0]);
            ((Expander)window.FindName("PhysicsDetailsExpander")).IsExpanded = true;
            foreach (var size in new[] { new Size(340, 430), new Size(680, 900), new Size(1200, 700) })
            {
                Layout(content, size);
                AssertReachableByScrolling(content, "CarSetupScroll", "UseCarPhysicsCheck", size);
                AssertReachableByScrolling(content, "CarSetupScroll", "RefreshPhysicsButton", size);
                AssertVisible(content, "SaveGeneratedButton", size);
                Render(content, size, Path.Combine(output, $"Car-Physics-{size.Width}.png"));
            }
            Check(File.ReadAllText(baseline).Contains("VALUE=60"), "UI changed baseline");
        }
        finally { ThemeService.Apply(ThemeCatalog.Presets[0]); window.Close(); }
        Progress($"PASS {checks} car physics workflow/theme assertions and small/portrait/wide reachability checks.");
    }
}
