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
    private static void CheckGearingWorkflow(string output)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Gearing UI: " + message); checks++; }
        static void Call(object window, string method) => window.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [window, new RoutedEventArgs()]);
        var root = Path.Combine(output, "gearing-fixture");
        var carPath = Path.Combine(root, "example_car"); var data = Path.Combine(carPath, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "setup.ini"), "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n");
        File.AppendAllText(Path.Combine(data, "setup.ini"), "[FRONT_BIAS]\nMIN=55\nMAX=100\n[FRONT_BIAS]\nMIN=45\nMAX=85\n");
        File.WriteAllText(Path.Combine(data, "drivetrain.ini"), "[TRACTION]\nTYPE=RWD\n[GEARS]\nCOUNT=6\nGEAR_2=2.1\nGEAR_3=1.5\nFINAL=3\n");
        File.WriteAllText(Path.Combine(data, "engine.ini"), "[HEADER]\nPOWER_CURVE=power.lut\n[ENGINE_DATA]\nLIMITER=8000\n");
        File.WriteAllText(Path.Combine(data, "power.lut"), "1000|100\n3000|250\n5000|300\n6000|280\n7000|200\n8000|100\n");
        File.WriteAllText(Path.Combine(data, "tyres.ini"), "[REAR]\nRADIUS=0.3\nNAME=Example rear tyre\n");
        File.WriteAllText(Path.Combine(data, "final.rto"), "Short|4.5\nLong|3\nMiddle|4\n");
        var baseline = Path.Combine(root, "baseline.ini");
        File.WriteAllText(baseline, "[CAR]\nMODEL=example_car\n[FINAL_RATIO]\nVALUE=1\n[TYRES]\nVALUE=0\n");
        var input = new TuneInput(); input.Car.SourceFolderPath = carPath; input.Car.SourceFolderName = "example_car";
        var store = new GearingTargetStore(Path.Combine(root, "targets"));
        store.Save(input, new GearingTarget());
        var preferences = new SpeedUnitPreferenceStore(Path.Combine(root, "units.json"));
        var window = new GearingWindow(input, baseline, store, preferences);
        TextBox Box(string name) => (TextBox)window.FindName(name);
        var save = (Button)window.FindName("SaveGearingButton");
        Box("LowRpmBox").Text = "3100"; Box("HighRpmBox").Text = "5400";
        Call(window, "Calculate_Click");
        Check(save.IsEnabled, "calculation did not enable a supported setup export");
        Check(((TextBlock)window.FindName("DetectedText")).Text.Contains("FRONT_BIAS"), "unrelated conflicting definition was hidden");
        var result = (TextBlock)window.FindName("ResultText");
        Check(result.Text.Contains("4:1") && result.Text.Contains("3,183") && result.Text.Contains("5,305"), "decoded ratio/RPM missing from review");
        Box("HighSpeedBox").Text = "110";
        Check(!save.IsEnabled && !result.Text.Contains("Try 4:1"), "changing a target left a stale recommendation");
        Box("HighSpeedBox").Text = "100";
        var units = (ComboBox)window.FindName("UnitsBox"); units.SelectedIndex = 1;
        Check(Math.Abs(double.Parse(Box("LowSpeedBox").Text) - 37.2822715) < .001, "mph switch changed physical speed");
        Check(((TextBlock)window.FindName("LowSpeedLabel")).Text.Contains("mph"), "unit labels not updated");
        units.SelectedIndex = 0;
        Call(window, "Calculate_Click"); Check(save.IsEnabled, "unit conversion prevented recalculation");
        Call(window, "SaveTarget_Click");
        var reopened = new GearingWindow(input, baseline, store, preferences);
        Check(((TextBox)reopened.FindName("HighRpmBox")).Text == "5400", "saved target did not reopen");
        reopened.Close();
        Box("GearBox").Text = "0"; Call(window, "Calculate_Click");
        Check(!save.IsEnabled && ((TextBlock)window.FindName("StatusText")).Text.Contains("gear"), "invalid target did not produce actionable error");
        Box("GearBox").Text = "3"; Call(window, "Calculate_Click");
        var cornerMode = (CheckBox)window.FindName("TwoGoalsCheck"); cornerMode.IsChecked = true;
        Check(!save.IsEnabled, "enabling corner targets retained a single-gear plan");
        Box("GearBox").Text = "2"; Box("LowSpeedBox").Text = "45"; Box("HighSpeedBox").Text = "65";
        Box("SweeperGearBox").Text = "3"; Box("SweeperLowBox").Text = "65"; Box("SweeperHighBox").Text = "100";
        Box("HighRpmBox").Text = "5900";
        Call(window, "Calculate_Click");
        Check(save.IsEnabled && result.Text.Contains("Tight corners") && result.Text.Contains("Long sweepers") && result.Text.Contains("4:1"), "joint fit missing from production review");
        Check(((TextBlock)window.FindName("DetectedText")).Text.Contains("3 final-drive presets"), "preset availability hidden");
        Box("SweeperHighBox").Text = "135"; Check(!save.IsEnabled, "sweeper edit left stale export enabled"); Call(window, "Calculate_Click");
        Check(result.Text.Contains("No available preset fits every") && ((TextBlock)window.FindName("OptionsText")).Text.Contains("excluded: reaches base limiter"), "joint compromise or excluded ratio hidden");
        Box("SweeperHighBox").Text = "100";
        for (int i = 0; i < 50; i++) { units.SelectedIndex = 1; units.SelectedIndex = 0; }
        Check(Box("LowSpeedBox").Text == "45" && Box("SweeperHighBox").Text == "100", "repeated unit switches drifted the target");
        units.SelectedIndex = 1; Call(window, "SaveUnits_Click");
        Check(preferences.Load() == true && ((TextBlock)window.FindName("SweeperHighLabel")).Text.Contains("mph"), "global preference or second-goal units missing");
        var newCar = new GearingWindow(input, baseline, new GearingTargetStore(Path.Combine(root, "new-targets")), preferences);
        Check(((ComboBox)newCar.FindName("UnitsBox")).SelectedIndex == 1 && Math.Abs(double.Parse(((TextBox)newCar.FindName("LowSpeedBox")).Text) - 24.8548) < .01, "new car did not honor default units with converted examples");
        Check(((TextBlock)newCar.FindName("RpmSummaryText")).Text.Contains("Base engine curve estimate"), "new target did not read engine curve"); newCar.Close();
        Call(window, "SaveTarget_Click"); reopened = new GearingWindow(input, baseline, store, preferences);
        Check(((CheckBox)reopened.FindName("TwoGoalsCheck")).IsChecked == true && ((TextBox)reopened.FindName("SweeperGearBox")).Text == "3", "second target did not reopen"); reopened.Close();
        Call(window, "ReadCar_Click");
        Check(((TextBlock)window.FindName("RpmSummaryText")).Text.Contains("Base engine curve estimate") && !save.IsEnabled, "engine read failed to invalidate prior calculation");
        Box("LowRpmBox").Text = "3100"; Box("HighRpmBox").Text = "5900"; units.SelectedIndex = 0; Call(window, "Calculate_Click");
        Check(((TextBlock)window.FindName("RpmSummaryText")).Text.Contains("Driver target"), "manual RPM edit retained automatic provenance");
        var theme = ThemeCatalog.Clone(ThemeCatalog.Presets[0]);
        theme.SectionHeading = "#FFE06A"; theme.PrimaryText = "#D5FFFF"; theme.SecondaryText = "#90EE90"; theme.ButtonText = "#FFC0CB";
        ThemeService.Apply(theme);
        var content = (FrameworkElement)window.Content;
        Layout(content, new Size(900, 800));
        static string ColorOf(TextBlock text) => ThemeService.ToHex(((SolidColorBrush)text.Foreground).Color);
        Check(ColorOf(result) == theme.PrimaryText, "result text ignores theme");
        Check(ColorOf((TextBlock)window.FindName("SourceText")) == theme.SecondaryText, "source text ignores theme");
        Check(ThemeService.ToHex(((SolidColorBrush)save.Foreground).Color) == theme.ButtonText, "save button ignores theme");
        Render(content, new Size(900, 800), Path.Combine(output, "Gearing-Custom-Theme.png"));
        ThemeService.Apply(ThemeCatalog.Presets[0]);
        foreach (var size in new[] { new Size(340, 430), new Size(680, 900), new Size(1200, 700) })
        {
            var scroll = (ScrollViewer)window.FindName("GearingScroll");
            scroll.ScrollToHome(); Layout(content, size);
            Render(content, size, Path.Combine(output, $"Gearing-Targets-{size.Width}.png"));
            scroll.ScrollToBottom(); Layout(content, size);
            Render(content, size, Path.Combine(output, $"Gearing-Result-{size.Width}.png"));
            var bounds = save.TransformToAncestor(content).TransformBounds(new Rect(save.RenderSize));
            Check(bounds.Bottom <= content.RenderSize.Height + 1 && bounds.Left >= 0, "actual save button unreachable");
        }
        window.Close();
        Progress($"PASS {checks} gearing workflow/theme assertions; isolated targets and fixture only.");
    }
}
