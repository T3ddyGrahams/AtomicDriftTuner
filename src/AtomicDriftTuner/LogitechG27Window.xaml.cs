using System.Globalization;
using System.Windows;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class LogitechG27Window : Window
{
    public sealed class SettingRow(string label, string key, int value, int min, int max, string units, string help)
    {
        public string Label { get; } = label;
        public string Key { get; } = key;
        public string Value { get; set; } = value.ToString(CultureInfo.InvariantCulture);
        public int Min { get; } = min;
        public int Max { get; } = max;
        public string Help { get; } = $"{min}–{max}{units}. {help}";
        public int Read() => int.TryParse(Value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) && n >= Min && n <= Max
            ? n : throw new InvalidDataException($"{Label}: enter a whole number from {Min} to {Max}.");
    }
    private readonly LogitechG27Store _store;
    public bool SettingsSaved { get; private set; }
    public LogitechG27Window(LogitechG27Store? store = null)
    {
        InitializeComponent(); _store = store ?? new(); HelpText.Text = LogitechG27Support.Instructions(true);
        try { Show(_store.Load()); }
        catch (Exception ex) { Show(new()); StatusText.Text = "Saved plan could not load: " + ex.Message + " Shown values are an unsaved provisional starting point."; }
        WindowBoundsService.Attach(this);
    }
    private void Show(LogitechG27Settings s)
    {
        LogitechRows.ItemsSource = new[] {
            new SettingRow("Overall Effects Strength", nameof(s.OverallEffectsStrength), s.OverallEffectsStrength, 0, 150, "%", "Driver effect strength; this is not MOZA Base Torque Output."),
            new SettingRow("Spring Effect Strength", nameof(s.SpringEffectStrength), s.SpringEffectStrength, 0, 150, "%", "Scales game-requested spring effects; separate from the centering checkbox."),
            new SettingRow("Damper Effect Strength", nameof(s.DamperEffectStrength), s.DamperEffectStrength, 0, 150, "%", "Scales requested damper effects; not the same as a MOZA wheel-damper percentage."),
            new SettingRow("Centering Spring Strength", nameof(s.CenteringSpringStrength), s.CenteringSpringStrength, 0, 150, "%", "Used with Enable Centering Spring below."),
            new SettingRow("Degrees Of Rotation", nameof(s.DegreesOfRotation), s.DegreesOfRotation, 40, 900, "°", "Match and calibrate steering rotation in AC.") };
        AcRows.ItemsSource = new[] {
            new SettingRow("AC Gain", nameof(s.Ac.GainPct), s.Ac.GainPct, 0, 100, "%", "Start with your current gain; reduce if sustained steering force clips."),
            new SettingRow("AC Filter", nameof(s.Ac.FilterPct), s.Ac.FilterPct, 0, 100, "%", "Keep fixed for a comparison unless this is your one test."),
            new SettingRow("AC Minimum Force", nameof(s.Ac.MinimumForcePct), s.Ac.MinimumForcePct, 0, 100, "%", "Test small increases only if needed around centre; too much can cause oscillation."),
            new SettingRow("AC Kerb", nameof(s.Ac.KerbPct), s.Ac.KerbPct, 0, 100, "%", "Additional surface effect."),
            new SettingRow("AC Road", nameof(s.Ac.RoadPct), s.Ac.RoadPct, 0, 100, "%", "Additional road effect."),
            new SettingRow("AC Slip", nameof(s.Ac.SlipPct), s.Ac.SlipPct, 0, 100, "%", "Additional slip effect."),
            new SettingRow("AC ABS", nameof(s.Ac.AbsPct), s.Ac.AbsPct, 0, 100, "%", "Additional ABS effect.") };
        CenteringCheck.IsChecked = s.EnableCenteringSpring; CombinedCheck.IsChecked = s.ReportCombinedPedals;
        GameAdjustCheck.IsChecked = s.AllowGameToAdjustSettings; SoftwareVersionBox.Text = s.SoftwareVersion;
    }
    private void StartingPoint_Click(object sender, RoutedEventArgs e)
    { Show(new()); StatusText.Text = "Provisional values loaded into this form only. Review and save if you want to test them."; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = new LogitechG27Settings { EnableCenteringSpring = CenteringCheck.IsChecked == true,
                ReportCombinedPedals = CombinedCheck.IsChecked == true, AllowGameToAdjustSettings = GameAdjustCheck.IsChecked == true,
                SoftwareVersion = SoftwareVersionBox.Text.Trim() };
            foreach (var row in LogitechRows.Items.Cast<SettingRow>()) typeof(LogitechG27Settings).GetProperty(row.Key)!.SetValue(s, row.Read());
            foreach (var row in AcRows.Items.Cast<SettingRow>()) typeof(AssettoCorsaSettings).GetProperty(row.Key)!.SetValue(s.Ac, row.Read());
            _store.Save(s); SettingsSaved = true;
            StatusText.Text = "Plan saved in ADT only. Generate/review AC FFB on the dashboard, enter the plan in Logitech and AC, then confirm the actual settings before recording.";
        }
        catch (Exception ex) { StatusText.Text = "Plan not saved: " + ex.Message; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
