using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using Microsoft.Win32;

namespace AtomicDriftTuner;

public partial class GearingWindow : Window
{
    private readonly TuneInput _input;
    private readonly GearingDataService _data = new();
    private readonly GearingTargetStore _targets;
    private GearingPlan? _plan;
    private bool _ready;
    private bool _mph;
    public event Action<string>? SetupFileSaved;

    public GearingWindow(TuneInput input, string baselinePath, GearingTargetStore? targets = null)
    {
        InitializeComponent();
        WindowBoundsService.Attach(this);
        _input = input;
        _targets = targets ?? new GearingTargetStore();
        CarText.Text = input.Car.DisplayName;
        BaselinePathBox.Text = baselinePath;
        try
        {
            var saved = _targets.Load(input);
            if (saved is not null)
            {
                _mph = saved.DisplayMph;
                UnitsBox.SelectedIndex = _mph ? 1 : 0;
                GearBox.Text = saved.Gear.ToString(CultureInfo.CurrentCulture);
                LowSpeedBox.Text = FormatSpeed(saved.MinimumSpeedKmh);
                HighSpeedBox.Text = FormatSpeed(saved.MaximumSpeedKmh);
                LowRpmBox.Text = saved.MinimumRpm.ToString("0.##", CultureInfo.CurrentCulture);
                HighRpmBox.Text = saved.MaximumRpm.ToString("0.##", CultureInfo.CurrentCulture);
                TargetStatusText.Text = "Loaded the saved gearing target for this car.";
            }
            else TargetStatusText.Text = "Example targets shown. Adjust them for your car and section, then save your target.";
        }
        catch (Exception ex) { TargetStatusText.Text = "Could not load your saved target: " + ex.Message; }
        UpdateUnits();
        _ready = true;
    }

    private string Unit => _mph ? "mph" : "km/h";
    private string FormatSpeed(double kmh) => (kmh / (_mph ? GearingPlanner.KmhPerMph : 1)).ToString("0.##", CultureInfo.CurrentCulture);
    private void UpdateUnits()
    {
        LowSpeedLabel.Text = $"Minimum road speed ({Unit})";
        HighSpeedLabel.Text = $"Maximum road speed ({Unit})";
    }
    private static double ReadNumber(TextBox box)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
            throw new InvalidDataException("Enter valid numbers in all target fields, using your Windows decimal separator.");
        return number;
    }
    private GearingTarget ReadTarget()
    {
        var gear = ReadNumber(GearBox);
        if (gear != Math.Truncate(gear) || gear is < 1 or > 10) throw new InvalidDataException("Enter a whole-number forward gear from 1 to 10.");
        var target = new GearingTarget
        {
            Gear = (int)gear, DisplayMph = _mph,
            MinimumSpeedKmh = ReadNumber(LowSpeedBox) * (_mph ? GearingPlanner.KmhPerMph : 1),
            MaximumSpeedKmh = ReadNumber(HighSpeedBox) * (_mph ? GearingPlanner.KmhPerMph : 1),
            MinimumRpm = ReadNumber(LowRpmBox), MaximumRpm = ReadNumber(HighRpmBox)
        };
        target.Validate();
        return target;
    }
    private void Invalidate()
    {
        _plan = null;
        SaveGearingButton.IsEnabled = false;
        ResultText.Text = "Targets or baseline changed. Calculate again to see the current recommendation.";
        SourceText.Text = OptionsText.Text = "";
        StatusText.Text = "Calculate before saving a gearing setup.";
    }
    private void ControlsChanged(object sender, TextChangedEventArgs e) { if (_ready) Invalidate(); }
    private void UnitsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var nextMph = UnitsBox.SelectedIndex == 1;
        if (_mph == nextMph) return;
        var factor = nextMph ? 1 / GearingPlanner.KmhPerMph : GearingPlanner.KmhPerMph;
        foreach (var box in new[] { LowSpeedBox, HighSpeedBox })
            if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var n) && double.IsFinite(n))
                box.Text = (n * factor).ToString("0.###", CultureInfo.CurrentCulture);
        _mph = nextMph;
        UpdateUnits();
        Invalidate();
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) BaselinePathBox.Text = dialog.FileName;
    }
    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        Invalidate();
        try
        {
            var target = ReadTarget();
            if (string.IsNullOrWhiteSpace(BaselinePathBox.Text)) throw new InvalidDataException("Choose a saved baseline setup for this car first.");
            _plan = GearingPlanner.Plan(_data.Load(_input.Car, BaselinePathBox.Text, target.Gear), target);
            var current = _plan.Current;
            var next = _plan.Recommended;
            var change = (next.FinalDrive.Ratio / current.FinalDrive.Ratio - 1) * 100;
            var speedRange = $"{FormatSpeed(target.MinimumSpeedKmh)}–{FormatSpeed(target.MaximumSpeedKmh)} {Unit}";
            var title = _plan.HasChange ? $"Try {next.FinalDrive.Ratio:0.###}:1 ({next.FinalDrive.Label}) — {(change > 0 ? "shorter" : "taller")} final drive." : "Keep your current final drive — it is already the best match to these targets.";
            var fit = next.FitsTarget ? "The predicted endpoints fit inside your target RPM band." : "No supported ratio fits your entire target RPM band. This is the closest endpoint match below the base limiter; consider a different gear or speed/RPM range.";
            ResultText.Text = $"{title}\n\nIn gear {target.Gear}, at {speedRange}:\nCurrent {current.FinalDrive.Ratio:0.###}:1: {current.LowRpm:N0} → {current.HighRpm:N0} RPM\nSuggested {next.FinalDrive.Ratio:0.###}:1: {next.LowRpm:N0} → {next.HighRpm:N0} RPM\nTarget: {target.MinimumRpm:N0}–{target.MaximumRpm:N0} RPM\n\n{fit}\nRPM and torque multiplication change: {change:+0.0;-0.0;0.0}% in every gear.\nEstimated speed at the base limiter in gear {target.Gear}: {FormatSpeed(next.LimiterSpeedKmh)} {Unit} (no slip).\n\nRanking prefers a full RPM-band fit, then the smallest proportional mismatch at both speed endpoints. It excludes ratios that reach the base engine.ini limiter within this range.";
            SourceText.Text = $"Decoded from {_plan.Data.CarDataEvidence?.Kind ?? "readable"} car data; installed files are unchanged. {_plan.Data.GearSource}; gear ratio {_plan.Data.GearRatio:0.####}:1.\n{_plan.Data.TyreSource}; nominal radius {_plan.Data.TyreRadius:0.###} m. Base engine.ini limiter {_plan.Data.LimiterRpm:N0} RPM.\nSaved final-drive index {current.FinalDrive.Index} → {next.FinalDrive.Index}. List order is preserved. This is a mapping of the selected saved setup, not live in-game readback. Active ECU, CSP or scripted limiter overrides are not verified; confirm the usable RPM range in game.";
            if (_plan.Data.CarDataEvidence?.MatchingUnpackedCopy == true)
                SourceText.Text = "Packed and unpacked copies verified: all supported physics files match. ADT checks both copies again before saving.\n" + SourceText.Text;
            OptionsText.Text = string.Join("\n\n", _plan.Options.Select(x => $"{x.FinalDrive.Ratio:0.###}:1 • {x.FinalDrive.Label}\n{x.LowRpm:N0}–{x.HighRpm:N0} RPM • {(x.HighRpm >= _plan.Data.LimiterRpm ? "excluded: reaches base limiter" : x.FitsTarget ? "fits target band" : "partial fit")}{(x.FinalDrive.Index == next.FinalDrive.Index ? " • suggested" : "")}{(x.FinalDrive.Index == current.FinalDrive.Index ? " • current" : "")}"));
            SaveGearingButton.IsEnabled = _plan.HasChange;
            StatusText.Text = _plan.HasChange ? "Review the estimate, then save a separate gearing setup." : "No change needed for this target. You can adjust the target and calculate again.";
        }
        catch (Exception ex) { _plan = null; StatusText.Text = ex.Message; ResultText.Text = "No gearing recommendation is available yet. See the explanation below."; }
    }
    private void SaveTarget_Click(object sender, RoutedEventArgs e)
    {
        try { _targets.Save(_input, ReadTarget()); TargetStatusText.Text = "Gearing target saved for this car. It will be restored when you return."; }
        catch (Exception ex) { TargetStatusText.Text = ex.Message; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_plan is null || ReadTarget() != _plan.Target) throw new InvalidOperationException("Calculate again before saving.");
            var dialog = new SaveFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini", DefaultExt = ".ini", AddExtension = true,
                OverwritePrompt = true, InitialDirectory = Path.GetDirectoryName(_plan.Data.BaselinePath), FileName = $"ADT_Gearing_{DateTime.Now:yyyyMMdd_HHmmss}.ini" };
            if (dialog.ShowDialog(this) != true) return;
            var path = _data.Save(_plan, dialog.FileName);
            StatusText.Text = $"Saved: {path}. Load this setup in Assetto Corsa and test the same section against your baseline.";
            SetupFileSaved?.Invoke(path);
        }
        catch (Exception ex) { Invalidate(); StatusText.Text = ex.Message; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
