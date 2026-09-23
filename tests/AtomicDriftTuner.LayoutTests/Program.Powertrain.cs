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
    private static void CheckPowertrain(string output)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Powertrain UI: " + message); checks++; }
        static void Call(object instance, string method, params object?[] args) => instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
        var input = new TuneInput(); input.Car.Id = input.Car.SourceFolderName = "isolated-powertrain-ui";
        var window = new TuningAssistantWindow(input);
        try
        {
            T Field<T>(string name) => (T)window.FindName(name);
            var content = (FrameworkElement)window.Content;
            var tabs = Field<TabControl>("AssistantTabs");
            var p = new PowertrainDiagnosis { Summary = "Synthetic UI fixture: 68.0s of forward drift across 1 gear.",
                TargetContext = "Recorded target: gear 3, 50–100 km/h, 4000–6500 RPM. Later target edits do not reinterpret this run.",
                SetupContext = "Recorded final drive: 4.3:1. File mapping only; confirm in game.",
                EcuContext = "Synthetic Race Fuel map: configured multiplier 1–1.3×; not measured horsepower.",
                NextTest = "Keep ECU fixed. Compare one shorter final-drive option, then repeat the same section.",
                Limitations = "Synthetic test. Tyre spin, clutch engagement and delivered torque are not measured.",
                Gears = [new() { Gear = 3, Seconds = 68, LowRpm = 3500, MedianRpm = 4000, HighRpm = 5500, LowSpeedKmh = 55, HighSpeedKmh = 80, HighThrottleSeconds = 50,
                    BelowTargetSeconds = 20, AboveTargetSeconds = 0, TargetSpeedSeconds = 60, NearBaseLimiterSeconds = 0, RecordedRatio = 1.8, Confidence = "MEDIUM" }],
                Phases = [new(3, "Initiation", 2.5, 3700, 3500, 3900), new(3, "Transition", 4, 4500, 3900, 5000)],
                Events = [new() { Kind = "High throttle below target", Gear = 3, StartSeconds = 5, EndSeconds = 8, Phase = "Initiation", Evidence = "Synthetic event: RPM 3500 → 3900; throttle 75%–90%; brake 0%; raw clutch 100%. Clutch engagement is unknown." },
                    new() { Kind = "Near recorded base limiter", Gear = 3, StartSeconds = 12, EndSeconds = 14, Evidence = "Synthetic second event: limiter proximity only, not proof of activation." }],
                MapPoints = [new(0, 1), new(4000, 1.2), new(8000, 1.3)] };
            var selected = new SavedTelemetrySession { Analysis = new() { Diagnosis = new() { Powertrain = p } } };
            Call(window, "RenderPowertrain", selected, null, null);
            Call(window, "ShowPowertrain_Click", window, new RoutedEventArgs());
            Layout(content, new Size(900, 800));
            Check(Field<TabItem>("PowertrainTab").IsSelected && Field<CheckBox>("AdvancedTelemetryToggle").IsChecked != true, "Review hidden behind advanced controls");
            Check(Field<DataGrid>("PowertrainGearGrid").Items.Count == 1 && Field<DataGrid>("PowertrainMapGrid").Items.Count == 3, "Evidence grids not bound");
            Check(Field<TextBlock>("PowertrainQuickText").Text == p.Summary && Field<TextBlock>("PowertrainEcuText").Text == p.EcuContext, "Summary and ECU context disagree");
            var expanders = new[] { "PowertrainMeasurementsExpander", "PowertrainSourceExpander", "PowertrainComparisonExpander" };
            Check(expanders.All(n => !Field<Expander>(n).IsExpanded), "Detailed tables overwhelm first view");
            foreach (var name in expanders) Field<Expander>(name).IsExpanded = true;
            Layout(content, new Size(900, 800));
            Field<DataGrid>("PowertrainEventGrid").SelectedIndex = 1;
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Check(Field<TextBlock>("PowertrainEventDetailText").Text == p.Events[1].Evidence, "Selected event context inaccessible");
            Call(window, "PowertrainBaseline_Click", window, new RoutedEventArgs());
            Check(((TabItem)tabs.SelectedItem).Header.ToString() == "Before / After", "Choose baseline navigated incorrectly");
            Call(window, "ShowPowertrain_Click", window, new RoutedEventArgs());
            var theme = ThemeCatalog.Clone(ThemeCatalog.Presets[0]); theme.FieldLabel = "#D5FFFF"; theme.SecondaryText = "#90EE90"; theme.SectionHeading = "#FFE06A";
            ThemeService.Apply(theme);
            foreach (var size in new[] { new Size(540, 600), new Size(680, 900), new Size(1600, 700) })
            {
                var scroll = Field<ScrollViewer>("AssistantBodyScroll"); scroll.ScrollToHome(); Layout(content, size);
                Render(content, size, Path.Combine(output, $"Powertrain-Top-{size.Width}.png"));
                Check(ThemeService.ToHex(((SolidColorBrush)Field<TextBlock>("PowertrainSummaryText").Foreground).Color) == theme.FieldLabel, "Summary ignores customization");
                scroll.ScrollToBottom(); Layout(content, size);
                var button = Field<Button>("PowertrainBaselineButton"); var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Top >= 0 && bounds.Bottom <= size.Height + 1, "Comparison control unreachable");
                Render(content, size, Path.Combine(output, $"Powertrain-Bottom-{size.Width}.png"));
                var grid = Field<DataGrid>("PowertrainGearGrid");
                Check(grid.Columns.All(c => c.ActualWidth > 0), "Gearing columns collapsed");
            }
            foreach (var name in expanders) Field<Expander>(name).IsExpanded = false;
            Field<ScrollViewer>("AssistantBodyScroll").ScrollToHome(); Layout(content, new Size(900, 800));
            Render(content, new Size(900, 800), Path.Combine(output, "Powertrain-Simple.png"));
            Call(window, "RenderPowertrain", null, null, null);
            Check(Field<DataGrid>("PowertrainGearGrid").Items.Count == 0 && Field<DataGrid>("PowertrainMapGrid").Items.Count == 0 && !Field<TextBlock>("PowertrainSummaryText").Text.Contains("Synthetic"), "Stale evidence remains after clearing");
        }
        finally { ThemeService.Apply(ThemeCatalog.Presets[0]); window.Close(); }
        Progress($"PASS {checks} gearing/ECU UI, navigation, theme, sizing and stale-evidence assertions.");
    }
}
