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
        File.WriteAllText(Path.Combine(data, "suspensions.ini"), "[FRONT]\nSPRING_RATE=80000\n[DAMAGE]\nMIN_VELOCITY=40\nUNCOMMENTED DAMAGE NOTE\n");
        File.WriteAllText(Path.Combine(data, "drivetrain.ini"), "[TRACTION]\nTYPE=RWD\n[DIFFERENTIAL]\nPOWER=0.7\nCOAST=65\n");
        File.WriteAllText(Path.Combine(data, "tyres.ini"), "[FRONT_1]\nNAME=Selected compound\nWIDTH=0.275\n");
        File.WriteAllText(Path.Combine(data, "setup.ini"), "[DIFF_POWER]\nMIN=0\nMAX=100\nSTEP=1\n[TYRES]\nMIN=0\nMAX=1\nSTEP=1\n[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n[ENGINE_MAPS]\nNAME=ECU tune\nSHOW_CLICKS=0\nLUT=maps.lut\n");
        File.WriteAllText(Path.Combine(data, "final.rto"), "Long|3.9\n4.30|4.3\n");
        File.WriteAllText(Path.Combine(data, "maps.lut"), "Stock|0\nTuned|1\nRace Fuel|2\n");
        File.WriteAllText(Path.Combine(data, "engine.ini"), "[ENGINE_DATA]\nLIMITER=8000\n[MAP]\nMAP_0=stock.lut\nMAP_1=tuned.lut\nMAP_2=race.lut\n");
        File.WriteAllText(Path.Combine(data, "stock.lut"), "0|1\n8000|1\n");
        File.WriteAllText(Path.Combine(data, "tuned.lut"), "0|1\n8000|1.1\n");
        File.WriteAllText(Path.Combine(data, "race.lut"), "0|1\n8000|1.3\n");
        var baseline = Path.Combine(root, "baseline.ini");
        File.WriteAllText(baseline, "VALUE=9\n[CAR]\nMODEL=physics_ui_fixture\n[DIFF_POWER]\nVALUE=60\n[TYRES]\nVALUE=1\n[FINAL_RATIO]\nVALUE=1\n[ENGINE_MAPS]\nVALUE=2\n");
        var earlierBaseline = Path.Combine(root, "earlier.ini");
        File.WriteAllText(earlierBaseline, "[CAR]\nMODEL=physics_ui_fixture\n[DIFF_POWER]\nVALUE=60\n[TYRES]\nVALUE=1\n[FINAL_RATIO]\nVALUE=0\n[ENGINE_MAPS]\nVALUE=0\n");
        var originalFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        var input = new TuneInput { Car = new() { Id = "physics_ui_fixture", SourceFolderPath = car, SourceFolderName = null } };
        var window = new CarSetupWindow(input, assistantBehaviorOverride: new());
        try
        {
            input.Car.SourceFolderName = "physics_ui_fixture";
            var choiceType = typeof(CarSetupWindow).GetNestedType("SetupChoice", BindingFlags.NonPublic)!;
            var choices = (ComboBox)window.FindName("BaselineBox");
            choices.ItemsSource = new[] { Activator.CreateInstance(choiceType, baseline)!, Activator.CreateInstance(choiceType, earlierBaseline)! }; choices.SelectedIndex = 0;
            var save = (Button)window.FindName("SaveGeneratedButton");
            var toggle = (CheckBox)window.FindName("UseCarPhysicsCheck");
            var status = (TextBlock)window.FindName("PhysicsStatusText");
            var details = (TextBlock)window.FindName("PhysicsDetailsText");
            var decoded = (TextBlock)window.FindName("DecodedSettingsText");
            var decodingSummary = (TextBlock)window.FindName("DecodingSummaryText");
            var decodeWarnings = (TextBlock)window.FindName("DecodeWarningsText");
            void Generate() => Call(window, "GenerateSetup_Click");
            Generate();
            Check(save.IsEnabled && status.Text.StartsWith("Imported"), "Import/generation failed");
            Check(details.Text.Contains("275 mm") && details.Text.Contains("FRONT_1"), "Saved compound missing from details");
            Check(details.Text.Contains("[DAMAGE]") && details.Text.Contains("80000 N/m") && !details.Text.Contains("unavailable or malformed"),
                "Auxiliary malformed text erased readable suspension details");
            Check(details.Text.Contains("raw file value 65") && details.Text.Contains("Effective lock remains unknown"),
                "Nonstandard differential scale was hidden or guessed");
            var analysis = (CarSetupAnalysis)typeof(CarSetupWindow).GetField("_analysis", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            Check(analysis.Parameters.Single(p => p.Section == "DIFF_POWER").PhysicsContext.Contains("70 %"), "Base context not attached to rows");
            Check(decodingSummary.Text.Contains("2 verified mapping(s)") && decoded.Text.Contains("[FINAL_RATIO] saved 1") &&
                decoded.Text.Contains("4.3:1") && decoded.Text.Contains("Race Fuel") && decoded.Text.Contains("race.lut"), "Saved gearing/ECU meanings or sources not shown");
            Check(decoded.Text.Contains("Unsupported") && decoded.Text.Contains("not horsepower"), "Unknown controls or engine-map limits hidden");
            Check(decodeWarnings.Text.Contains("without a section") && decodeWarnings.Text.Contains("cannot identify") &&
                !analysis.Parameters.Any(p => p.CurrentRaw == "9"), "Orphan VALUE misidentified as ECU or warning absent");
            Check(analysis.Parameters.Single(p => p.Section == "FINAL_RATIO").DecodedContext.Contains("4.3:1") &&
                analysis.Parameters.Single(p => p.Section == "ENGINE_MAPS").DecodedContext.Contains("Race Fuel"), "Saved meaning not available beside setup rows");
            var setupGrid = (DataGrid)window.FindName("SetupGrid");
            Check(setupGrid.Columns.OfType<DataGridBoundColumn>().Any(column => column.Binding is System.Windows.Data.Binding binding && binding.Path.Path == "DecodedContext"),
                "Grid does not expose saved setting meanings");
            choices.SelectedIndex = 1;
            Check(!save.IsEnabled, "Changing baseline left generated changes enabled");
            Generate();
            Check(decoded.Text.Contains("3.9:1") && decoded.Text.Contains("Stock") && !decoded.Text.Contains("Race Fuel") && decodeWarnings.Text.Length == 0,
                "Changing baseline retained the previous map or orphan-value warning");
            choices.SelectedIndex = 0; Generate();
            toggle.IsChecked = false;
            Check(!save.IsEnabled && status.Text.Contains("off"), "Opt-out left stale generation");
            Check(!decoded.Text.Contains("Race Fuel"), "Opt-out left a stale verified ECU display");
            Generate(); Check(save.IsEnabled, "Opt-out prevented baseline-only workflow");
            toggle.IsChecked = true; Check(!save.IsEnabled, "Opt-in left stale generation");
            Generate(); Call(window, "RefreshPhysics_Click"); Check(!save.IsEnabled, "Refresh left stale generation");
            Generate();
            var theme = ThemeCatalog.Clone(ThemeCatalog.Presets[0]); theme.PrimaryText = "#D5FFFF"; theme.FieldLabel = "#90EE90"; theme.SecondaryText = "#FFC0CB";
            ThemeService.Apply(theme);
            var content = (FrameworkElement)window.Content;
            Layout(content, new Size(900, 800));
            Check(ThemeService.ToHex(((SolidColorBrush)status.Foreground).Color) == theme.PrimaryText, "Status ignores custom color");
            Check(ThemeService.ToHex(((SolidColorBrush)details.Foreground).Color) == theme.FieldLabel, "Details ignore custom color");
            Check(ThemeService.ToHex(((SolidColorBrush)decoded.Foreground).Color) == theme.FieldLabel, "Decoded meanings ignore custom color");
            Check(ThemeService.ToHex(((SolidColorBrush)decodingSummary.Foreground).Color) == theme.PrimaryText, "Decoding summary ignores custom color");
            Check(ThemeService.ToHex(((SolidColorBrush)decodeWarnings.Foreground).Color) == theme.SecondaryText, "Decoding warning ignores custom color");
            ThemeService.Apply(ThemeCatalog.Presets[0]);
            ((Expander)window.FindName("PhysicsDetailsExpander")).IsExpanded = true;
            ((Expander)window.FindName("DecodedSettingsExpander")).IsExpanded = true;
            // Long explanatory text must be readable from first to last line without requiring
            // the entire expanded block to fit on a small screen at once.
            void CheckScrollableText(TextBlock text, Size size)
            {
                var scroll = (ScrollViewer)window.FindName("CarSetupScroll");
                scroll.ScrollToHome(); Layout(content, size);
                var original = text.TransformToAncestor(scroll).TransformBounds(new Rect(text.RenderSize));
                Check(original.Height > 0 && original.Left >= -1 && original.Right <= scroll.ViewportWidth + 1,
                    $"{text.Name} is horizontally clipped at {size}");
                scroll.ScrollToVerticalOffset(Math.Max(0, original.Top - 4)); Layout(content, size);
                var first = text.TransformToAncestor(scroll).TransformBounds(new Rect(text.RenderSize));
                Check(first.Top >= -1 && first.Top < scroll.ViewportHeight, $"First line of {text.Name} is unreachable at {size}");
                scroll.ScrollToVerticalOffset(Math.Max(0, original.Bottom - scroll.ViewportHeight + 4)); Layout(content, size);
                var last = text.TransformToAncestor(scroll).TransformBounds(new Rect(text.RenderSize));
                Check(last.Bottom > 0 && last.Bottom <= scroll.ViewportHeight + 1, $"Last line of {text.Name} is unreachable at {size}");
            }
            foreach (var size in new[] { new Size(340, 430), new Size(680, 900), new Size(1200, 700) })
            {
                Layout(content, size);
                AssertReachableByScrolling(content, "CarSetupScroll", "UseCarPhysicsCheck", size);
                AssertReachableByScrolling(content, "CarSetupScroll", "RefreshPhysicsButton", size);
                CheckScrollableText(decodingSummary, size);
                CheckScrollableText(decodeWarnings, size);
                CheckScrollableText(details, size);
                CheckScrollableText(decoded, size);
                AssertVisible(content, "SaveGeneratedButton", size);
                Render(content, size, Path.Combine(output, $"Car-Physics-{size.Width}.png"));
            }
            Check(originalFiles.All(file => File.ReadAllText(file.Key) == file.Value), "UI changed a baseline or car-data file");
        }
        finally { ThemeService.Apply(ThemeCatalog.Presets[0]); window.Close(); }
        Progress($"PASS {checks} car physics workflow/theme assertions and small/portrait/wide reachability checks.");
    }
}
