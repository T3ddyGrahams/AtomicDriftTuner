using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class AzomSettingsWindow : Window
{
    private readonly TuneInput _input;
    private readonly AzomSettings _azom;
    private readonly AppSettingsStore _settingsStore = new();
    private AzomUserPreferences _preferences;
    private AzomLiveSnapshot? _liveSnapshot;
    private List<AzomApplyPlanItem> _livePlan = [];
    public bool PreferencesChanged { get; private set; }

    public AzomSettingsWindow(TuneInput input, TuneResult result, AzomUserPreferences preferences)
    {
        InitializeComponent();
        SectionSelectionBox.ItemsSource = new[] { "Core", "Wheelbase", "Game Effects", "Protection", "Soft Limit", "High Speed Damping", "FFB Equalizer", "FFB Curve", "Preferences" };
        SectionSelectionBox.SelectedIndex = 0;
        _input = input;
        _azom = result.Azom;
        _preferences = Clone(preferences);
        SetupText.Text = $"{input.Hardware.Model} • {input.Wheel.Model} • {input.DriftPack.Name} • {input.Car.DisplayName} • {input.Intent.Name}";
        Render();
        LoadPreferences();
        var app = _settingsStore.Load();
        SimHubPathBox.Text = SimHubLocator.FindSimHubExe(app.AzomLive?.SimHubExePath) ?? app.AzomLive?.SimHubExePath ?? "";
        LiveStatusText.Text = "Live integration is idle. Build/install the bundled Atomic SimHub Bridge, start SimHub + AZOM, then click READ LIVE AZOM.";
    }

    private void Render()
    {
        var a = _azom;
        CoreText.Text =
            $"Wheel Rotation Angle    {a.Core.WheelRotationAngleDeg,5}°    [60..2700]\n" +
            $"Game FFB Strength       {a.Core.GameFfbStrengthPct,5}%    [0..100]\n" +
            $"Base Torque Output      {a.Core.BaseTorqueOutputPct,5}%    [50..100]\n" +
            $"Maximum Wheel Speed     {a.Core.MaximumWheelSpeedPct,5}%    [0..200]\n" +
            $"Interpolation           {a.Core.Interpolation,5}     [0..10]";

        WheelbaseText.Text =
            $"Wheel Damper            {a.WheelbaseEffects.WheelDamperPct,5}%    [0..100]\n" +
            $"Wheel Friction          {a.WheelbaseEffects.WheelFrictionPct,5}%    [0..100]\n" +
            $"Natural Inertia         {a.WheelbaseEffects.NaturalInertia,5}     [100..500]\n" +
            $"Wheel Spring            {a.WheelbaseEffects.WheelSpringPct,5}%    [0..100]";

        GameEffectsText.Text =
            $"Game Damper             {a.GameEffects.GameDamperPct,5}%    [0..100]\n" +
            $"Game Friction           {a.GameEffects.GameFrictionPct,5}%    [0..100]\n" +
            $"Game Inertia            {a.GameEffects.GameInertiaPct,5}%    [0..100]\n" +
            $"Game Spring             {a.GameEffects.GameSpringPct,5}%    [0..100]";

        ProtectionText.Text =
            $"Hands-Off Protection    {OnOff(a.Protection.HandsOffProtection),5}     [preference]\n" +
            $"Steering Wheel Inertia  {a.Protection.SteeringWheelInertia,5}     [100..4000]";

        HighSpeedText.Text =
            $"Damping Level           {a.HighSpeedDamping.DampingLevelPct,5}%    [0..100]\n" +
            $"Trigger Speed           {a.HighSpeedDamping.TriggerSpeedKph,5} kph [0..400]";

        SoftLimitText.Text =
            $"Stiffness               {a.SoftLimit.Stiffness,5}     [1..10]\n" +
            $"Retain Game FFB         {OnOff(a.SoftLimit.RetainGameFfb),5}     [preference]";

        EqualizerText.Text =
            $"10 Hz                   {a.FfbEqualizer.Hz10,5}%\n" +
            $"15 Hz                   {a.FfbEqualizer.Hz15,5}%\n" +
            $"25 Hz                   {a.FfbEqualizer.Hz25,5}%\n" +
            $"40 Hz                   {a.FfbEqualizer.Hz40,5}%\n" +
            $"60 Hz                   {a.FfbEqualizer.Hz60,5}%\n" +
            $"100 Hz                  {a.FfbEqualizer.Hz100,5}%\n" +
            $"Sensitivity             {a.FfbEqualizer.Sensitivity,5}     [0..10]";

        CurveText.Text =
            $"Preset                  {a.FfbOutputCurve.Preset}\n" +
            $"Input 20  -> Output     {a.FfbOutputCurve.Node20,5}\n" +
            $"Input 40  -> Output     {a.FfbOutputCurve.Node40,5}\n" +
            $"Input 60  -> Output     {a.FfbOutputCurve.Node60,5}\n" +
            $"Input 80  -> Output     {a.FfbOutputCurve.Node80,5}\n" +
            $"Input 100 -> Output     {a.FfbOutputCurve.Node100,5}";

        AllSettingsText.Text = BuildAllSettings(a);
    }

    private static string BuildAllSettings(AzomSettings a) =>
        "CORE SETTINGS\n" +
        $"  Wheel Rotation Angle: {a.Core.WheelRotationAngleDeg}°\n  Game FFB Strength: {a.Core.GameFfbStrengthPct}%\n  Base Torque Output: {a.Core.BaseTorqueOutputPct}%\n  Maximum Wheel Speed: {a.Core.MaximumWheelSpeedPct}%\n  Interpolation: {a.Core.Interpolation}\n\n" +
        "GEARSHIFT VIBRATION\n" +
        $"  Shift Intensity: {a.GearshiftVibration.ShiftIntensity}\n  Vibrate on Neutral: {OnOff(a.GearshiftVibration.VibrateOnNeutral)}\n  Shift Debounce: {a.GearshiftVibration.ShiftDebounceMs} ms\n\n" +
        "WHEELBASE EFFECTS\n" +
        $"  Wheel Damper: {a.WheelbaseEffects.WheelDamperPct}%\n  Wheel Friction: {a.WheelbaseEffects.WheelFrictionPct}%\n  Natural Inertia: {a.WheelbaseEffects.NaturalInertia}\n  Wheel Spring: {a.WheelbaseEffects.WheelSpringPct}%\n\n" +
        "GAME EFFECTS\n" +
        $"  Game Damper: {a.GameEffects.GameDamperPct}%\n  Game Friction: {a.GameEffects.GameFrictionPct}%\n  Game Inertia: {a.GameEffects.GameInertiaPct}%\n  Game Spring: {a.GameEffects.GameSpringPct}%\n\n" +
        "PROTECTION\n" +
        $"  Hands-Off Protection: {OnOff(a.Protection.HandsOffProtection)}\n  Steering Wheel Inertia: {a.Protection.SteeringWheelInertia}\n\n" +
        "SOFT LIMIT\n" +
        $"  Stiffness: {a.SoftLimit.Stiffness}\n  Retain Game FFB: {OnOff(a.SoftLimit.RetainGameFfb)}\n\n" +
        "FFB EQUALIZER\n" +
        $"  10Hz {a.FfbEqualizer.Hz10}% | 15Hz {a.FfbEqualizer.Hz15}% | 25Hz {a.FfbEqualizer.Hz25}% | 40Hz {a.FfbEqualizer.Hz40}% | 60Hz {a.FfbEqualizer.Hz60}% | 100Hz {a.FfbEqualizer.Hz100}% | Sensitivity {a.FfbEqualizer.Sensitivity}\n\n" +
        "FFB OUTPUT CURVE\n" +
        $"  {a.FfbOutputCurve.Preset}: 20->{a.FfbOutputCurve.Node20}, 40->{a.FfbOutputCurve.Node40}, 60->{a.FfbOutputCurve.Node60}, 80->{a.FfbOutputCurve.Node80}, 100->{a.FfbOutputCurve.Node100}\n\n" +
        "HIGH SPEED DAMPING\n" +
        $"  Damping Level: {a.HighSpeedDamping.DampingLevelPct}%\n  Trigger Speed: {a.HighSpeedDamping.TriggerSpeedKph} kph\n\n" +
        "MISCELLANEOUS\n" +
        $"  FFB Reversal: {OnOff(a.Miscellaneous.ForceFeedbackReversal)}\n  Standby Mode: {OnOff(a.Miscellaneous.StandbyMode)}\n  Standby After: {a.Miscellaneous.StandbyAfter}\n  Base Status LED: {OnOff(a.Miscellaneous.BaseStatusLed)}\n  Bluetooth: {OnOff(a.Miscellaneous.Bluetooth)}";

    private void LoadPreferences()
    {
        ShiftIntensityBox.Text = _preferences.ShiftIntensity.ToString(CultureInfo.InvariantCulture);
        VibrateNeutralBox.IsChecked = _preferences.VibrateOnNeutral;
        ShiftDebounceBox.Text = _preferences.ShiftDebounceMs.ToString(CultureInfo.InvariantCulture);
        HandsOffBox.IsChecked = _preferences.HandsOffProtection;
        RetainGameFfbBox.IsChecked = _preferences.RetainGameFfb;
        FfbReversalBox.IsChecked = _preferences.ForceFeedbackReversal;
        StandbyModeBox.IsChecked = _preferences.StandbyMode;
        StandbyAfterBox.Text = _preferences.StandbyAfter;
        BaseStatusLedBox.IsChecked = _preferences.BaseStatusLed;
        BluetoothBox.IsChecked = _preferences.Bluetooth;
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(ShiftIntensityBox.Text, out var shift) || shift < 0 || shift > 5)
                throw new InvalidOperationException("Shift Intensity must be 0–5.");
            if (!int.TryParse(ShiftDebounceBox.Text, out var debounce) || debounce < 0 || debounce > 1000)
                throw new InvalidOperationException("Shift Debounce must be 0–1000 ms.");

            _preferences = new AzomUserPreferences
            {
                ShiftIntensity = shift,
                VibrateOnNeutral = VibrateNeutralBox.IsChecked == true,
                ShiftDebounceMs = debounce,
                HandsOffProtection = HandsOffBox.IsChecked == true,
                RetainGameFfb = RetainGameFfbBox.IsChecked == true,
                ForceFeedbackReversal = FfbReversalBox.IsChecked == true,
                StandbyMode = StandbyModeBox.IsChecked == true,
                StandbyAfter = string.IsNullOrWhiteSpace(StandbyAfterBox.Text) ? "Disabled" : StandbyAfterBox.Text.Trim(),
                BaseStatusLed = BaseStatusLedBox.IsChecked == true,
                Bluetooth = BluetoothBox.IsChecked == true
            };

            var app = _settingsStore.Load();
            app.AzomPreferences = Clone(_preferences);
            _settingsStore.Save(app);
            PreferencesChanged = true;
            StatusText.Text = "AZOM preference controls saved. Regenerate the main tune to see them reflected.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AZOM Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    private void AutoDetectSimHub_Click(object sender, RoutedEventArgs e)
    {
        var found = SimHubLocator.FindSimHubExe(SimHubPathBox.Text);
        if (found == null)
        {
            MessageBox.Show("SimHubWPF.exe was not found in the standard install folders. Use Browse to select it.", "SimHub", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SimHubPathBox.Text = found;
        SaveLiveConnectionSettings();
        LiveStatusText.Text = "SimHub detected. Start SimHub and ensure the Atomic Drift Tuner Bridge + AZOM plugins are enabled.";
    }

    private void BrowseSimHub_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SimHubWPF.exe|SimHubWPF.exe|Executable (*.exe)|*.exe", Title = "Select SimHubWPF.exe" };
        if (dialog.ShowDialog() != true) return;
        SimHubPathBox.Text = dialog.FileName;
        SaveLiveConnectionSettings();
    }

    private async void ReadLiveAzom_Click(object sender, RoutedEventArgs e)
    {
        try { await ReadAndCompareAsync(); }
        catch (Exception ex) { ShowLiveError(ex); }
    }

    private async void RefreshComparison_Click(object sender, RoutedEventArgs e)
    {
        try { await ReadAndCompareAsync(); }
        catch (Exception ex) { ShowLiveError(ex); }
    }

    private async void ApplyLiveAzom_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_liveSnapshot == null) await ReadAndCompareAsync();
            if (_liveSnapshot == null) return;
            RenderComparison(_liveSnapshot);
            var changed = _livePlan.Where(x => x.CanApply && x.IsDifferent && x.IsSelectedForApply).ToList();
            if (changed.Count == 0)
            {
                LiveStatusText.Text = "No differing writable settings are selected for apply.";
                return;
            }
            var actionCount = changed.Sum(x => x.EstimatedActions);
            var answer = MessageBox.Show(
                $"Atomic will change {changed.Count} selected AZOM settings. Public-action fallback would require about {actionCount} actions, but v0.6 tries the exact AZOM commit first.\n\n" +
                "AZOM actions push to the wheelbase immediately and persist to the active profile/base as applicable. Atomic will save a pre-apply snapshot for Revert and will not touch undocumented controls.\n\nContinue?",
                "Apply AZOM Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            LiveStatusText.Text = "Applying AZOM differences...";
            var controller = CreateLiveController();
            var result = await controller.ApplyAsync(_livePlan, _liveSnapshot);
            _liveSnapshot = result.After ?? await controller.ReadAsync();
            RenderComparison(_liveSnapshot);
            ShowBatchResult("Apply", result);
            LiveStatusText.Text = $"Apply complete: verified {result.VerifiedSettingsChanged}/{result.SettingsChanged} selected settings." +
                                  (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
        }
        catch (Exception ex) { ShowLiveError(ex); }
    }

    private async void RevertLiveAzom_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var controller = CreateLiveController();
            var backup = controller.LoadRevertRecord();
            if (backup == null || backup.ChangedProperties.Count == 0)
            {
                MessageBox.Show("No Atomic AZOM apply backup is available yet.", "Revert AZOM", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var current = await controller.ReadAsync();
            var plan = controller.BuildRevertPlan(backup.Snapshot, current, backup.ChangedProperties);
            var changed = plan.Where(x => x.CanApply && x.IsDifferent).ToList();
            if (changed.Count == 0)
            {
                LiveStatusText.Text = "The settings changed by the last Atomic apply already match the saved pre-apply snapshot.";
                _liveSnapshot = current; RenderComparison(current);
                return;
            }
            var answer = MessageBox.Show($"Revert {changed.Count} settings to the snapshot saved before the last Atomic apply?", "Revert AZOM", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            LiveStatusText.Text = "Reverting last Atomic AZOM apply...";
            var result = await controller.ApplyAsync(plan, current);
            _liveSnapshot = result.After ?? await controller.ReadAsync();
            RenderComparison(_liveSnapshot);
            ShowBatchResult("Revert", result);
            LiveStatusText.Text = $"Revert complete: verified {result.VerifiedSettingsChanged}/{result.SettingsChanged} settings.";
        }
        catch (Exception ex) { ShowLiveError(ex); }
    }

    private async Task ReadAndCompareAsync()
    {
        try
        {
            SaveLiveConnectionSettings();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new UnauthorizedAccessException(
                "Atomic Drift Tuner could not save its local Live AZOM connection settings. " +
                "Check permissions for %LOCALAPPDATA%\\AtomicDriftTuner.",
                ex);
        }

        LiveStatusText.Text = "Reading AZOM properties from SimHub...";
        var controller = CreateLiveController();
        _liveSnapshot = await controller.ReadAsync();

        if (!_liveSnapshot.SettingsReadable)
        {
            if (!_liveSnapshot.PluginDetected)
                throw new InvalidOperationException(
                    "The Atomic bridge is connected, but SimHub is not publishing any AZOM/Moza properties. " +
                    "Verify AZOM is enabled in SimHub and restart SimHub after enabling it.");

            if (string.Equals(_liveSnapshot.PropertyNamespace, "Moza", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Atomic detected a legacy AZOM property namespace (Moza.*), but this AZOM build does not expose the full Base settings needed for Live Apply. " +
                    $"Published Moza properties: {_liveSnapshot.LegacyMozaPropertyCount}. Update AZOM to a current build that exposes AZOM.FfbStrength, AZOM.Torque, AZOM.Rotation, etc.");

            if (_liveSnapshot.BaseConnected == false)
                throw new InvalidOperationException(
                    "AZOM is detected, but AZOM.BaseConnected is false. Close MOZA Pit House completely, connect the wheelbase, and wait for the Base tab to populate before reading again.");

            throw new InvalidOperationException(
                $"AZOM is detected and the bridge sees {_liveSnapshot.AzomPropertyCount} AZOM properties, but the Base-setting values are not readable yet. " +
                $"BaseConnected={_liveSnapshot.BaseConnected?.ToString() ?? "unknown"}, setting properties found={_liveSnapshot.SettingsPropertyCount}. " +
                "Open AZOM's Base tab and wait for the wheelbase settings read to complete, then try again.");
        }

        RenderComparison(_liveSnapshot);
        LiveStatusText.Text =
            $"Live AZOM read OK • Bridge {_liveSnapshot.BridgeVersion} • namespace {_liveSnapshot.PropertyNamespace} • " +
            $"source {_liveSnapshot.ReadSource} • captured {_liveSnapshot.CapturedUtc.ToLocalTime():T}.";
    }

    private void RenderComparison(AzomLiveSnapshot snapshot)
    {
        var previousSelection = _livePlan
            .Where(x => x.IsSelectedForApply)
            .Select(x => x.PropertyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var controller = CreateLiveController();
        _livePlan = controller.BuildPlan(_azom, snapshot, IncludePreferencesBox.IsChecked == true);

        if (previousSelection.Count > 0)
        {
            foreach (var row in _livePlan)
                row.IsSelectedForApply = row.CanApply && row.IsDifferent && previousSelection.Contains(row.PropertyName);
        }

        LiveComparisonGrid.ItemsSource = null;
        LiveComparisonGrid.ItemsSource = _livePlan;
        UpdateSelectionStatus();
    }

    private void SelectAllDifferences_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _livePlan)
            row.IsSelectedForApply = row.CanApply && row.IsDifferent;
        LiveComparisonGrid.Items.Refresh();
        UpdateSelectionStatus();
    }

    private void ClearApplySelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _livePlan)
            row.IsSelectedForApply = false;
        LiveComparisonGrid.Items.Refresh();
        UpdateSelectionStatus();
    }

    private void SelectSection_Click(object sender, RoutedEventArgs e)
    {
        if (SectionSelectionBox.SelectedItem is not string group) return;
        foreach (var row in _livePlan)
            row.IsSelectedForApply = row.CanApply && row.IsDifferent && string.Equals(row.Group, group, StringComparison.OrdinalIgnoreCase);
        LiveComparisonGrid.Items.Refresh();
        UpdateSelectionStatus();
    }

    private void ApplyRowCheckBox_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateSelectionStatus));
    }

    private void LiveComparisonGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateSelectionStatus));
    }

    private void UpdateSelectionStatus()
    {
        var selected = _livePlan.Count(x => x.CanApply && x.IsDifferent && x.IsSelectedForApply);
        var differences = _livePlan.Count(x => x.CanApply && x.IsDifferent);
        SelectionStatusText.Text = $"Selected {selected} of {differences} writable differences.";
    }

    private void ShowBatchResult(string operation, AzomApplyResult result)
    {
        BatchAuditGrid.ItemsSource = null;
        BatchAuditGrid.ItemsSource = result.Audit;
        BatchSummaryText.Text =
            $"{operation}: verified {result.VerifiedSettingsChanged}/{result.SettingsChanged}. " +
            $"Exact commits: {result.DirectFallbackSettingsTriggered}; " +
            $"public/CLI actions: {result.ActionsTriggered}. " +
            (result.Warnings.Count > 0 ? $"Warnings: {result.Warnings.Count}." : "No warnings.");
    }

    private AzomLiveController CreateLiveController()
    {
        var settings =
            _settingsStore.Load().AzomLive ??
            new AzomLiveConnectionSettings();

        SimHubActionInvoker? cliFallback = null;

        var exe =
            SimHubLocator.FindSimHubExe(SimHubPathBox.Text) ??
            SimHubPathBox.Text;

        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            cliFallback =
                new SimHubActionInvoker(
                    exe,
                    settings.ActionDelayMs);
        }

        return new AzomLiveController(
            new AzomBridgeClient(settings.PipeName),
            settings.ActionDelayMs,
            cliFallback);
    }

    private void SaveLiveConnectionSettings()
    {
        var app = _settingsStore.Load();
        app.AzomLive ??= new AzomLiveConnectionSettings();
        if (!string.IsNullOrWhiteSpace(SimHubPathBox.Text)) app.AzomLive.SimHubExePath = SimHubPathBox.Text.Trim();
        _settingsStore.Save(app);
    }

    public async Task RefreshLiveFromRemoteAsync()
    {
        try
        {
            await ReadAndCompareAsync();
            LiveStatusText.Text = "Live AZOM refreshed after a remote change.";
        }
        catch (Exception ex)
        {
            // A remote refresh should never interrupt the user's desktop flow
            // with a modal dialog. Keep the error visible in the Live panel.
            LiveStatusText.Text = "Remote refresh unavailable: " + ex.Message;
        }
    }

    private void ShowLiveError(Exception ex)
    {
        LiveStatusText.Text = "Live AZOM unavailable: " + ex.Message;

        var detail = ex is UnauthorizedAccessException
            ? "\n\nPermission check: make sure SimHub and Atomic Drift Tuner are running under the same Windows user. " +
              "The v0.5.2 bridge explicitly grants local authenticated users read/write access to its read-only named pipe. " +
              "After installing the updated bridge, fully exit and restart SimHub."
            : "\n\nIf SimHub is running, verify that AZOM and the bundled Atomic Drift Tuner SimHub Bridge are installed/enabled.";

        MessageBox.Show(ex.Message + detail + "\n\nThe normal tuner still works without the bridge.",
            "Live AZOM", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private static string OnOff(bool x) => x ? "ON" : "OFF";
    private static AzomUserPreferences Clone(AzomUserPreferences x) => new()
    {
        ShiftIntensity = x.ShiftIntensity,
        VibrateOnNeutral = x.VibrateOnNeutral,
        ShiftDebounceMs = x.ShiftDebounceMs,
        HandsOffProtection = x.HandsOffProtection,
        RetainGameFfb = x.RetainGameFfb,
        ForceFeedbackReversal = x.ForceFeedbackReversal,
        StandbyMode = x.StandbyMode,
        StandbyAfter = x.StandbyAfter,
        BaseStatusLed = x.BaseStatusLed,
        Bluetooth = x.Bluetooth
    };
}
