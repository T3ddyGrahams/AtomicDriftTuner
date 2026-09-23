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
    private readonly SpeedUnitPreferenceStore _units;
    private readonly Dictionary<TextBox, (string Text, double Kmh)> _speeds = new();
    private GearingPlan? _plan;
    private bool _ready, _mph, _closed;
    private string _rpmSource = "Driver target";
    private string? _rpmFingerprint;
    public event Action<string>? SetupFileSaved;

    public GearingWindow(TuneInput input, string baselinePath, GearingTargetStore? targets = null, SpeedUnitPreferenceStore? units = null)
    {
        InitializeComponent();
        Closed += (_, _) => _closed = true;
        WindowBoundsService.Attach(this);
        _input = input; _targets = targets ?? new(); _units = units ?? new();
        CarText.Text = input.Car.DisplayName; BaselinePathBox.Text = baselinePath;
        GearingTarget? saved = null;
        bool canAutoRead = true;
        try { _mph = _units.Load() ?? false; }
        catch (Exception ex) { UnitsStatusText.Text = "Could not load the default units: " + ex.Message; }
        try { saved = _targets.Load(input); }
        catch (Exception ex) { canAutoRead = false; TargetStatusText.Text = "Could not load your saved target: " + ex.Message; }
        if (saved is not null)
        {
            _mph = saved.DisplayMph; TwoGoalsCheck.IsChecked = saved.Sweeper is not null;
            GearBox.Text = saved.Gear.ToString(CultureInfo.CurrentCulture);
            SetSpeed(LowSpeedBox, saved.MinimumSpeedKmh); SetSpeed(HighSpeedBox, saved.MaximumSpeedKmh);
            if (saved.Sweeper is { } s) { SweeperGearBox.Text = s.Gear.ToString(CultureInfo.CurrentCulture); SetSpeed(SweeperLowBox, s.MinimumSpeedKmh); SetSpeed(SweeperHighBox, s.MaximumSpeedKmh); }
            else { SetSpeed(SweeperLowBox, 70); SetSpeed(SweeperHighBox, 110); }
            LowRpmBox.Text = saved.MinimumRpm.ToString("0.##", CultureInfo.CurrentCulture); HighRpmBox.Text = saved.MaximumRpm.ToString("0.##", CultureInfo.CurrentCulture);
            _rpmSource = saved.RpmSource; _rpmFingerprint = saved.RpmSourceFingerprint;
            TargetStatusText.Text = "Loaded your saved targets. " + (saved.Sweeper is null ? "Your earlier single-gear target is preserved; enable both corner goals when ready." : "Both corner goals will be checked together.");
            RpmEvidenceText.Text = "Saved RPM target: " + _rpmSource + ". Read the car again to refresh an engine-curve estimate; this does not rewrite earlier runs.";
        }
        else
        {
            SetSpeed(LowSpeedBox, 40); SetSpeed(HighSpeedBox, 70); SetSpeed(SweeperLowBox, 70); SetSpeed(SweeperHighBox, 110);
            if (canAutoRead) TargetStatusText.Text = "Choose your gears and actual section speeds, then save the targets for this car.";
        }
        UnitsBox.SelectedIndex = _mph ? 1 : 0; UpdateUnits(); UpdateGoalMode(); UpdateRpmSummary(); _ready = true;
        if (saved is null && canAutoRead && File.Exists(baselinePath)) ReadCar_Click(this, new RoutedEventArgs());
    }
    private string Unit => _mph ? "mph" : "km/h";
    private string FormatSpeed(double kmh) => (kmh / (_mph ? GearingPlanner.KmhPerMph : 1)).ToString("0.###", CultureInfo.CurrentCulture);
    private void SetSpeed(TextBox box, double kmh) { box.Text = FormatSpeed(kmh); _speeds[box] = (box.Text, kmh); }
    private double ReadSpeed(TextBox box) => _speeds.TryGetValue(box, out var saved) && saved.Text == box.Text ? saved.Kmh : ReadNumber(box) * (_mph ? GearingPlanner.KmhPerMph : 1);
    private void UpdateUnits()
    {
        LowSpeedLabel.Text = SweeperLowLabel.Text = $"Slow end of this section ({Unit})";
        HighSpeedLabel.Text = SweeperHighLabel.Text = $"Fast end of this section ({Unit})";
    }
    private void UpdateGoalMode()
    {
        SweeperPanel.Visibility = TwoGoalsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PrimaryGoalTitle.Text = TwoGoalsCheck.IsChecked == true ? "Tight corners" : "My drift section";
    }
    private static double ReadNumber(TextBox box)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
            throw new InvalidDataException("Enter valid numbers in the target fields. If RPM is missing, read the car or enter a known range under Advanced.");
        return number;
    }
    private static int ReadGear(TextBox box)
    {
        var gear = ReadNumber(box);
        if (gear != Math.Truncate(gear) || gear is < 1 or > 10) throw new InvalidDataException("Choose a whole-number forward gear from 1 to 10.");
        return (int)gear;
    }
    private GearingTarget ReadTarget()
    {
        var t = new GearingTarget { Gear = ReadGear(GearBox), DisplayMph = _mph, MinimumSpeedKmh = ReadSpeed(LowSpeedBox), MaximumSpeedKmh = ReadSpeed(HighSpeedBox),
            MinimumRpm = ReadNumber(LowRpmBox), MaximumRpm = ReadNumber(HighRpmBox), RpmSource = _rpmSource, RpmSourceFingerprint = _rpmFingerprint,
            Sweeper = TwoGoalsCheck.IsChecked == true ? new(ReadGear(SweeperGearBox), ReadSpeed(SweeperLowBox), ReadSpeed(SweeperHighBox)) : null };
        t.Validate(); return t;
    }
    private void Invalidate()
    {
        _plan = null; SaveGearingButton.IsEnabled = false;
        ResultText.Text = "Targets or baseline changed. Calculate again to compare the available choices.";
        SourceText.Text = OptionsText.Text = ""; StatusText.Text = "Calculate before saving a gearing setup.";
    }
    private void ControlsChanged(object sender, TextChangedEventArgs e) { if (_ready) { if (sender is TextBox box) _speeds.Remove(box); Invalidate(); } }
    private void RpmChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _rpmSource = "Driver target"; _rpmFingerprint = null; RpmEvidenceText.Text = "Using your manually entered RPM band.";
        UpdateRpmSummary(); Invalidate();
    }
    private void UpdateRpmSummary() => RpmSummaryText.Text = string.IsNullOrWhiteSpace(LowRpmBox.Text) || string.IsNullOrWhiteSpace(HighRpmBox.Text)
        ? "RPM target needed: read the car or enter a known range under Advanced."
        : $"RPM target: {LowRpmBox.Text}–{HighRpmBox.Text} • {_rpmSource}. You can adjust it under Advanced.";
    private void GoalModeChanged(object sender, RoutedEventArgs e) { if (_ready) { UpdateGoalMode(); Invalidate(); } }
    private void UnitsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _mph == (UnitsBox.SelectedIndex == 1)) return;
        var previousMph = _mph;
        try
        {
            // Convert atomically and retain canonical values across repeated display switches.
            var values = new[] { LowSpeedBox, HighSpeedBox, SweeperLowBox, SweeperHighBox }.Select(b => (Box: b, Kmh: ReadSpeed(b))).ToArray();
            _ready = false; _mph = UnitsBox.SelectedIndex == 1;
            foreach (var value in values) SetSpeed(value.Box, value.Kmh);
            UpdateUnits(); Invalidate();
        }
        catch (Exception ex) { _ready = false; UnitsBox.SelectedIndex = previousMph ? 1 : 0; StatusText.Text = "Correct the speed fields before changing units. " + ex.Message; }
        finally { _ready = true; }
    }
    private void SaveUnits_Click(object sender, RoutedEventArgs e)
    {
        try { _units.Save(_mph); UnitsStatusText.Text = $"Default saved: {Unit}. New car targets start in these units; existing saved car choices are preserved."; }
        catch (Exception ex) { UnitsStatusText.Text = "Could not save default units: " + ex.Message; }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) { BaselinePathBox.Text = dialog.FileName; DetectedText.Text = "New baseline selected. Read the car or calculate to refresh its gearing choices."; }
    }
    private void ReadCar_Click(object sender, RoutedEventArgs e)
    {
        Invalidate();
        try
        {
            var data = _data.Load(_input.Car, BaselinePathBox.Text, ReadGear(GearBox));
            DetectedText.Text = DescribeCar(data); RpmEvidenceText.Text = data.RpmEstimate.Explanation;
            if (data.RpmEstimate.Available)
            {
                _ready = false;
                LowRpmBox.Text = data.RpmEstimate.MinimumRpm!.Value.ToString("0", CultureInfo.CurrentCulture);
                HighRpmBox.Text = data.RpmEstimate.MaximumRpm!.Value.ToString("0", CultureInfo.CurrentCulture);
                _rpmSource = "Base engine curve estimate"; _rpmFingerprint = data.CarDataEvidence!.Fingerprint;
                StatusText.Text = "Car read. Review the RPM starting estimate and your corner speeds, then calculate.";
            }
            else { AdvancedExpander.IsExpanded = true; StatusText.Text = data.RpmEstimate.Explanation; }
        }
        catch (Exception ex) { DetectedText.Text = "Car data could not be resolved: " + ex.Message; StatusText.Text = ex.Message; AdvancedExpander.IsExpanded = true; }
        finally { _ready = true; UpdateRpmSummary(); }
    }
    private static string DescribeCar(GearingData data) => $"Detected: {data.GearCount} forward gears. {data.GearboxKind}. " +
        (data.SelectedGearChoices > 1 ? $"Gear {data.Gear} has {data.SelectedGearChoices} defined choices. " : "") +
        (data.FinalDriveAdjustable ? $"{data.FinalDrives.Count} final-drive presets available. This planner compares those presets and keeps individual gears as saved." : "Final drive is fixed; this planner can assess the target but cannot export a final-drive change.") +
        (data.DefinitionWarnings.Count == 0 ? "" : "\n" + string.Join("\n", data.DefinitionWarnings));
    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        Invalidate();
        try
        {
            var target = ReadTarget(); _plan = _data.Calculate(_input.Car, BaselinePathBox.Text, target);
            var current = _plan.Current; var next = _plan.Recommended;
            var change = (next.FinalDrive.Ratio / current.FinalDrive.Ratio - 1) * 100;
            var title = _plan.HasChange ? $"Try {next.FinalDrive.Ratio:0.###}:1 ({next.FinalDrive.Label}) — {(change > 0 ? "shorter" : "taller")} final drive."
                : _plan.Data.FinalDriveAdjustable ? "Keep your current final drive — it is the best available match to these targets." : "This car's final drive is fixed.";
            var lines = next.Goals.Select((goal, i) =>
            {
                var t = target.Goals().ElementAt(i).Target; var before = current.Goals[i];
                string fit = goal.FitsTarget ? "Fits the requested RPM band." :
                    (goal.LowRpm < target.MinimumRpm ? "Below your RPM band at the slow end. " : "") + (goal.HighRpm > target.MaximumRpm ? "Above your RPM band at the fast end." : "");
                return $"{goal.Label}: gear {goal.Gear}, {FormatSpeed(t.MinimumSpeedKmh)}–{FormatSpeed(t.MaximumSpeedKmh)} {Unit}\nCurrent: {before.LowRpm:N0}–{before.HighRpm:N0} RPM → suggested: {goal.LowRpm:N0}–{goal.HighRpm:N0} RPM\n{fit}";
            });
            ResultText.Text = title + "\n\n" + string.Join("\n\n", lines) + "\n\n" +
                (next.FitsTarget ? "All requested sections fit this RPM target in the no-slip estimate." : "No available preset fits every requested section. This is the closest combined match below the base limiter; adjust the affected gear or speed range if the compromise is unsuitable.") +
                $"\nRPM and torque multiplication change: {change:+0.0;-0.0;0.0}% in every gear. Test the same sections before keeping the change.";
            DetectedText.Text = DescribeCar(_plan.Data) + (_plan.SweeperData is { SelectedGearChoices: > 1 } second ? $"\nSweeper gear {second.Gear} has {second.SelectedGearChoices} individual ratio choices; its saved selection is retained." : "");
            SourceText.Text = $"{target.RpmSource}: {target.MinimumRpm:N0}–{target.MaximumRpm:N0} RPM.\n{_plan.Data.RpmEstimate.Explanation}\n\n{_plan.Data.CarDataEvidence?.Kind} car data; {_plan.Data.TyreSource}, radius {_plan.Data.TyreRadius:0.###} m; base limiter {_plan.Data.LimiterRpm:N0} RPM.\nA preset must stay below the base limiter in both sections. Ranking prefers a fit for all targets, then the smallest average proportional error at their speed endpoints, with equal weight per section. Indexes follow the car's original list order. ECU/script limiter overrides and wheelspin are not simulated.";
            if (_plan.Data.CarDataEvidence?.MatchingUnpackedCopy == true) SourceText.Text += "\nPacked and unpacked data match; both are checked again at save.";
            SourceText.Text += $"\nGear {target.Gear}: {_plan.Data.GearSource}; ratio {_plan.Data.GearRatio:0.####}:1.";
            if (_plan.SweeperData is { } sweepData) SourceText.Text += $"\nGear {sweepData.Gear}: {sweepData.GearSource}; ratio {sweepData.GearRatio:0.####}:1.";
            OptionsText.Text = string.Join("\n\n", _plan.Options.Select(x => $"{x.FinalDrive.Ratio:0.###}:1 • {x.FinalDrive.Label} • {(x.FinalDrive.Ratio / current.FinalDrive.Ratio - 1) * 100:+0.0;-0.0;0.0}% RPM vs current" +
                (x.FinalDrive.Index == current.FinalDrive.Index ? " • current" : "") + (x.FinalDrive.Index == next.FinalDrive.Index ? " • suggested" : "") + "\n" +
                string.Join("\n", x.Goals.Select(g => $"{g.Label}, gear {g.Gear}: {g.LowRpm:N0}–{g.HighRpm:N0} RPM • {(!g.BelowLimiter ? "excluded: reaches base limiter" : g.FitsTarget ? "fits target band" : "partial fit")}; base-limit speed {FormatSpeed(g.LimiterSpeedKmh)} {Unit}"))));
            SaveGearingButton.IsEnabled = _plan.HasChange;
            StatusText.Text = _plan.HasChange ? "Review both sections, then save a separate gearing setup." : "No final-drive change to save. Review the fit or adjust the target.";
        }
        catch (Exception ex) { _plan = null; StatusText.Text = ex.Message; ResultText.Text = "No gearing recommendation is available yet. See the explanation below."; }
    }
    private void SaveTarget_Click(object sender, RoutedEventArgs e)
    {
        try { _targets.Save(_input, ReadTarget()); TargetStatusText.Text = "Targets saved for this car. New recordings will retain both goals and this RPM target."; }
        catch (Exception ex) { TargetStatusText.Text = ex.Message; }
    }
    private async void FindRuns_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        RunStatusText.Text = "Reading recent runs for this car…";
        try
        {
            var input = RunHistoryStore.Clone(_input);
            var runs = await Task.Run(() => new TelemetrySessionStore().ListRecent(input, 30));
            if (_closed) return;
            RunBox.ItemsSource = runs;
            RunStatusText.Text = runs.Count == 0 ? "No saved runs for this car/profile. Record a run first or enter the section speeds manually." : "Select a run by the intended driver on comparable sections, then choose Use this run's speeds.";
        }
        catch (Exception ex) { if (!_closed) RunStatusText.Text = ex.Message; }
        finally { if (button is not null) button.IsEnabled = true; }
    }
    private void UseRun_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RunBox.SelectedItem is not SavedTelemetrySession run) throw new InvalidDataException("Choose a recorded run first.");
            var gears = TwoGoalsCheck.IsChecked == true ? new[] { ReadGear(GearBox), ReadGear(SweeperGearBox) } : new[] { ReadGear(GearBox) };
            var speeds = GearingRunSpeeds.Read(run, _input, gears);
            Invalidate();
            SetSpeed(LowSpeedBox, speeds[0].MinimumSpeedKmh); SetSpeed(HighSpeedBox, speeds[0].MaximumSpeedKmh);
            if (speeds.Count > 1) { SetSpeed(SweeperLowBox, speeds[1].MinimumSpeedKmh); SetSpeed(SweeperHighBox, speeds[1].MaximumSpeedKmh); }
            RunStatusText.Text = "Filled typical drift speeds from the selected run. Check that each range represents the intended corner; gear alone does not identify a tight corner or sweeper. RPM targets were not inferred from this run.";
        }
        catch (Exception ex) { RunStatusText.Text = ex.Message; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_plan is null || ReadTarget() != _plan.Target) throw new InvalidOperationException("Calculate again before saving.");
            var dialog = new SaveFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini", DefaultExt = ".ini", AddExtension = true, OverwritePrompt = true,
                InitialDirectory = Path.GetDirectoryName(_plan.Data.BaselinePath), FileName = $"ADT_Gearing_{DateTime.Now:yyyyMMdd_HHmmss}.ini" };
            if (dialog.ShowDialog(this) != true) return;
            var path = _data.Save(_plan, dialog.FileName); StatusText.Text = $"Saved: {path}. Load this setup in Assetto Corsa and test both sections against your baseline."; SetupFileSaved?.Invoke(path);
        }
        catch (Exception ex) { Invalidate(); StatusText.Text = ex.Message; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
