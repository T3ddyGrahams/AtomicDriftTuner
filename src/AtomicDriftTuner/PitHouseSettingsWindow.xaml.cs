using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class PitHouseSettingsWindow : Window
{
    public sealed class SettingRow
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string ValuesText { get; init; } = "";
        public string Detail { get; init; } = "";
        public int Target { get; init; }
        public bool CanSelect { get; init; }
        public bool Selected { get; set; }
    }
    private readonly GuidedWorkflowStore _store;
    private readonly TuneResult _result;
    private readonly bool _sdkEnabled;
    private bool _ready, _busy;
    private PitHouseReading? _reading;
    private IReadOnlyDictionary<string, int>? _restoreTargets;
    private List<SettingRow> _rows = [];
    private string _backupPath = "";
    public PitHouseSettingsWindow(TuneInput input, TuneResult result, GuidedWorkflowStore? store = null)
    {
        InitializeComponent();
        _store = store ?? new GuidedWorkflowStore();
        _result = result;
        var prefs = _store.Preferences();
        _sdkEnabled = prefs.FfbProvider == FfbProvider.MozaPitHouse &&
            input.Hardware.Manufacturer.Trim().Equals("MOZA", StringComparison.OrdinalIgnoreCase);
        HeadingText.Text = FfbProviderOptions.Label(prefs.FfbProvider);
        ContextText.Text = $"{input.Car.DisplayName} · {input.Hardware.Manufacturer} {input.Hardware.Model} · Recommendations are not proof of applied settings.";
        HelpText.Text = prefs.FfbProvider == FfbProvider.MozaPitHouse ? FfbProviderOptions.PitHouseInstructions(prefs.ShowDetailedHelp)
            : "Save your current profile first. Enter AC FFB in Controls → Force Feedback. The wheelbase values below use MOZA's setting definitions; use only controls with matching meaning and units in your manufacturer's software. Other bases may have different ranges or no equivalent control. This manual view sends nothing to SimHub or Pit House.";
        if (prefs.FfbProvider == FfbProvider.MozaPitHouse && !_sdkEnabled)
            HelpText.Text += "\nSelect your actual MOZA hardware in Car & Hardware before connecting to the MOZA SDK.";
        SdkPanel.Visibility = ConfirmBaseCheck.Visibility = ApplyButton.Visibility = _sdkEnabled ? Visibility.Visible : Visibility.Collapsed;
        SdkFolderBox.Text = prefs.MozaSdkFolder;
        var a = result.Ac;
        AcValuesText.Text = $"Gain {a.GainPct}% · Filter {a.FilterPct}% · Minimum force {a.MinimumForcePct}% · Kerb {a.KerbPct}% · Road {a.RoadPct}% · Slip {a.SlipPct}% · ABS {a.AbsPct}%. These are AC menu values, not Pit House controls.";
        UnsupportedText.Text = PitHouseCatalog.Unsupported + $" Recommended steering rotation: {result.Azom.Core.WheelRotationAngleDeg}°. Review steering limits manually in Pit House and AC.";
        _ready = true;
        RefreshRows();
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
    }
    private void RefreshRows()
    {
        PlanTitleText.Text = _restoreTargets is null ? "Generated wheelbase recommendations" : "Restore preview — original values from the selected backup";
        _rows = PitHouseCatalog.Settings.Select(s =>
        {
            int target = _restoreTargets is null ? s.Target(_result.Azom) : _restoreTargets.GetValueOrDefault(s.Key, int.MinValue);
            int? current = _reading?.Values.TryGetValue(s.Key, out var v) == true ? v : null;
            bool included = target != int.MinValue;
            bool supported = included && s.Accepts(target);
            return new SettingRow { Key = s.Key, Label = s.Label, Target = target,
                ValuesText = $"Current: {(current.HasValue ? current.Value.ToString() : "not read")} → {(included ? target.ToString() : "not in this backup")} · SDK range {s.Min}–{s.Max}",
                CanSelect = _sdkEnabled && supported && current.HasValue && current != target,
                Detail = !included ? "Left untouched." : !supported ? "Target is outside the SDK range. Blocked; it will not be clamped or sent."
                    : _reading?.Errors.GetValueOrDefault(s.Key) ?? (!current.HasValue
                        ? _sdkEnabled ? "Enter manually, or Read Pit House to review current values before applying." : "Enter only if the control and units match your wheelbase software."
                        : current == target ? "Already matches; no write needed." : "Review this value; tick to include it in Apply.") };
        }).ToList();
        SettingsRows.ItemsSource = _rows;
        ConfirmBaseCheck.IsChecked = false;
        UpdateActions();
    }
    private void UpdateActions()
    {
        if (!_ready) return;
        SettingsBody.IsEnabled = !_busy;
        ApplyButton.IsEnabled = !_busy && _sdkEnabled && _reading is not null && ConfirmBaseCheck.IsChecked == true && _rows.Any(x => x.CanSelect);
        CloseButton.IsEnabled = !_busy;
    }
    private void Confirm_Changed(object sender, RoutedEventArgs e) => UpdateActions();
    private void SdkFolder_Changed(object sender, TextChangedEventArgs e)
    { if (!_ready) return; _reading = null; _restoreTargets = null; RefreshRows(); StatusText.Text = "SDK folder changed. Read Pit House before applying anything."; }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select MOZA SDK_CSharp/x64 (or x86) folder" };
        if (dialog.ShowDialog(this) == true) SdkFolderBox.Text = dialog.FolderName;
    }
    private PitHouseService Service()
    {
        if (!_sdkEnabled) throw new InvalidOperationException("Choose MOZA Pit House and your actual MOZA base before connecting.");
        var prefs = _store.Preferences();
        FfbProviderOptions.Require(FfbProvider.MozaPitHouse, prefs.FfbProvider);
        prefs.MozaSdkFolder = SdkFolderBox.Text.Trim();
        _store.SavePreferences(prefs);
        return new PitHouseService(prefs.MozaSdkFolder);
    }
    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true; _reading = null; _restoreTargets = null; UpdateActions();
        PlanTitleText.Text = "Generated wheelbase recommendations";
        StatusText.Text = "Reading Pit House. No settings will be changed…";
        try
        {
            _reading = await Service().ReadAsync();
            StatusText.Text = $"SDK readback: {_reading.Device} · {_reading.Values.Count}/{PitHouseCatalog.Settings.Count} controls readable · {_reading.CapturedUtc.LocalDateTime:t}. Review targets before selecting changes.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _busy = false; RefreshRows(); }
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Review an ADT Pit House backup", Filter = "ADT Pit House backup (*.json)|*.json", CheckFileExists = true };
        if (File.Exists(_backupPath)) { dialog.InitialDirectory = Path.GetDirectoryName(_backupPath); dialog.FileName = Path.GetFileName(_backupPath); }
        else dialog.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomicDriftTuner", "PitHouseBackups");
        if (dialog.ShowDialog(this) != true) return;
        _busy = true; _reading = null; _restoreTargets = null; UpdateActions();
        try
        {
            var backup = PitHouseService.LoadBackup(dialog.FileName);
            var reading = await Service().ReadAsync();
            _restoreTargets = PitHouseService.RestoreTargets(reading, backup);
            _reading = reading;
            PlanTitleText.Text = "Restore preview — original values from the selected backup";
            StatusText.Text = $"Read {reading.Device}. Backup from {backup.CreatedUtc.LocalDateTime:g}. Select original values to restore, confirm the physical base, then Apply. Nothing has been restored yet.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _busy = false; RefreshRows(); }
    }
    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _reading is null || ConfirmBaseCheck.IsChecked != true) return;
        _busy = true; UpdateActions();
        try
        {
            var plan = PitHouseService.Plan(_reading, _rows.Where(x => x.CanSelect && x.Selected).Select(x => KeyValuePair.Create(x.Key, x.Target)));
            StatusText.Text = "Saving original values, applying your selected controls and checking readback…";
            var result = await Service().ApplyAsync(plan);
            StatusText.Text = result.Message;
            _backupPath = result.BackupPath;
            BackupText.Text = "Original values saved locally: " + result.BackupPath;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _reading = null; _restoreTargets = null; _busy = false; RefreshRows(); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) { if (!_busy) Close(); }
}
