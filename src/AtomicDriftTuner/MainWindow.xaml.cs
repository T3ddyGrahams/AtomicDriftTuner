using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class MainWindow : Window
{
    private readonly List<HardwareProfile> _hardware = BuiltInProfiles.Hardware();
    private readonly List<SteeringWheelProfile> _wheels = BuiltInProfiles.Wheels();
    private readonly List<DriftPackProfile> _packs = BuiltInProfiles.DriftPacks();
    private readonly List<CarProfile> _builtInCars = BuiltInProfiles.Cars();
    private readonly List<DriftIntent> _intents = BuiltInProfiles.Intents();
    private readonly TuningEngine _engine = new();
    private readonly CalibrationEngine _calibrationEngine = new();
    private readonly CalibrationStore _calibrationStore = new();
    private readonly ProfileStore _store = new();
    private readonly AssettoCorsaScanner _scanner = new();
    private readonly AppSettingsStore _appSettingsStore = new();

    private List<CarProfile> _installedCars = [];
    private List<CarProfile> _visibleCars = [];
    private TuneResult? _lastResult;
    private CalibrationProfile? _currentCalibration;
    private AzomUserPreferences _azomPreferences = new();

    private AzomSettingsWindow? _azomSettingsWindow;
    private CarSetupWindow? _carSetupWindow;
    private ThemeWindow? _themeWindow;
    private TelemetryWindow? _telemetryWindow;
    private TuningAssistantWindow? _tuningAssistantWindow;
    private DiagnosticsWindow? _diagnosticsWindow;
    private SetupWizardWindow? _setupWizardWindow;

    public MainWindow()
    {
        InitializeComponent();

        HardwareBox.ItemsSource = _hardware;
        WheelBox.ItemsSource = _wheels;
        PackBox.ItemsSource = _packs;
        IntentBox.ItemsSource = _intents;
        GripBox.ItemsSource = Enum.GetValues<GripLevel>();

        HardwareBox.SelectedIndex = Math.Max(0, _hardware.FindIndex(x => x.Id == "moza-r12"));
        WheelBox.SelectedIndex = Math.Max(0, _wheels.FindIndex(x => x.Id == "moza-cs-pro"));
        PackBox.SelectedIndex = Math.Max(0, _packs.FindIndex(x => x.Id == "gravy"));
        IntentBox.SelectedIndex = Math.Max(0, _intents.FindIndex(x => x.Kind == DriftStyleKind.Realistic));

        PopulateHardware();
        PopulateWheel();
        RefreshCarsForPack();

        var settings = _appSettingsStore.Load();
        _azomPreferences = settings.AzomPreferences ?? new AzomUserPreferences();
        if (!string.IsNullOrWhiteSpace(settings.AssettoCorsaRoot))
            AcRootBox.Text = settings.AssettoCorsaRoot;
        else
            TryAutoDetect(showMessage: false);

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        var settings = _appSettingsStore.Load();
        if (!settings.FirstRunCompleted)
            OpenSetupWizard(firstRun: true);
    }

    private void OpenSetupWizard(bool firstRun)
    {
        if (!firstRun && _setupWizardWindow is not null)
        {
            RestoreAndActivate(_setupWizardWindow);
            return;
        }

        var window =
            new SetupWizardWindow(firstRun)
            {
                Owner = this
            };

        void ApplySettingsIfChanged()
        {
            if (!window.SettingsChanged)
                return;

            var settings = _appSettingsStore.Load();

            _azomPreferences =
                settings.AzomPreferences ??
                new AzomUserPreferences();

            if (!string.IsNullOrWhiteSpace(settings.AssettoCorsaRoot))
                AcRootBox.Text = settings.AssettoCorsaRoot;

            ScanStatusText.Text =
                "Machine configuration updated. Scan installed cars when ready.";
        }

        if (firstRun)
        {
            // First run remains intentionally modal because the machine paths
            // are a prerequisite for a predictable initial app state.
            window.ShowDialog();
            ApplySettingsIfChanged();
            return;
        }

        _setupWizardWindow = window;
        window.Closed += (_, _) =>
        {
            ApplySettingsIfChanged();
            _setupWizardWindow = null;
        };
        window.Show();
    }

    private static double Number(string value, string label)
    {
        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return number;
        }

        throw new InvalidOperationException(
            $"{label} must be a number. Use a decimal point if needed.");
    }

    private void PopulateHardware()
    {
        if (HardwareBox.SelectedItem is not HardwareProfile h) return;
        PeakTorqueBox.Text = h.PeakTorqueNm.ToString("0.##", CultureInfo.InvariantCulture);
        UpdateCalibrationStatusSafely();
    }

    private void PopulateWheel()
    {
        if (WheelBox.SelectedItem is not SteeringWheelProfile w) return;
        WheelDiameterBox.Text = w.DiameterMm.ToString("0.##", CultureInfo.InvariantCulture);
        WheelInertiaBox.Text = w.InertiaFactor.ToString("0.00", CultureInfo.InvariantCulture);
        UpdateCalibrationStatusSafely();
    }

    private void RefreshCarsForPack(string? preferredCarId = null)
    {
        if (PackBox.SelectedItem is not DriftPackProfile pack) return;

        var installedForPack = _installedCars
            .Where(x => x.PackId == pack.Id)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (installedForPack.Count > 0)
        {
            _visibleCars = installedForPack;
            PackInfoText.Text = $"{pack.Category} baseline • {installedForPack.Count} installed car(s) detected for this pack.";
        }
        else
        {
            _visibleCars = _builtInCars.Where(x => x.PackId == pack.Id).ToList();
            if (_visibleCars.Count == 0)
            {
                _visibleCars =
                [
                    new CarProfile
                    {
                        Id = $"{pack.Id}-custom",
                        PackId = pack.Id,
                        DisplayName = $"{pack.Name} - Custom Car",
                        IsCustom = true
                    }
                ];
            }

            string scanHint = _installedCars.Count > 0
                ? " No installed matches were inferred; showing fallback templates."
                : " Scan Assetto Corsa to replace templates with installed cars.";
            PackInfoText.Text = $"{pack.Category} baseline.{scanHint}";
        }

        CarBox.ItemsSource = null;
        CarBox.ItemsSource = _visibleCars;

        int index = preferredCarId is null ? 0 : _visibleCars.FindIndex(x => x.Id == preferredCarId);
        CarBox.SelectedIndex = index >= 0 ? index : 0;
        PopulateCar();
    }

    private void PopulateCar()
    {
        if (CarBox.SelectedItem is not CarProfile c) return;
        CarMassBox.Text = c.MassKg.ToString("0.##", CultureInfo.InvariantCulture);
        CarPowerBox.Text = c.PowerHp.ToString("0.##", CultureInfo.InvariantCulture);
        CasterBox.Text = c.CasterDeg.ToString("0.##", CultureInfo.InvariantCulture);
        LockBox.Text = c.SteeringLockPerSideDeg.ToString("0.##", CultureInfo.InvariantCulture);
        FrontTireBox.Text = c.FrontTireWidthMm.ToString("0.##", CultureInfo.InvariantCulture);
        GripBox.SelectedItem = c.Grip;

        if (c.IsInstalled)
        {
            var parts = new List<string> { $"Installed folder: {c.SourceFolderName}" };
            if (!string.IsNullOrWhiteSpace(c.Author)) parts.Add($"Author: {c.Author}");
            if (!string.IsNullOrWhiteSpace(c.DataSourceSummary)) parts.Add($"Read: {c.DataSourceSummary}");
            CarSourceText.Text = string.Join(" • ", parts);
        }
        else
        {
            CarSourceText.Text = "Built-in editable template. Verify values before relying heavily on the result.";
        }

        RenderConfidence(c.Confidence);
        UpdateCalibrationStatusSafely();
    }

    private TuneInput BuildInput()
    {
        var hb = HardwareBox.SelectedItem as HardwareProfile
                 ?? throw new InvalidOperationException("Select a wheelbase.");
        var wb = WheelBox.SelectedItem as SteeringWheelProfile
                 ?? throw new InvalidOperationException("Select a steering wheel.");
        var pb = PackBox.SelectedItem as DriftPackProfile
                 ?? throw new InvalidOperationException("Select a drift pack.");
        var cb = CarBox.SelectedItem as CarProfile
                 ?? throw new InvalidOperationException("Select a car.");
        var intent = IntentBox.SelectedItem as DriftIntent
                     ?? throw new InvalidOperationException("Select a drift target.");

        return new TuneInput
        {
            Hardware = new HardwareProfile
            {
                Id = hb.Id,
                Manufacturer = hb.Manufacturer,
                Model = hb.Model,
                PeakTorqueNm = Number(PeakTorqueBox.Text, "Peak torque"),
                MaxRotationDeg = hb.MaxRotationDeg,
                IsCustom = hb.IsCustom
            },
            Wheel = new SteeringWheelProfile
            {
                Id = wb.Id,
                Manufacturer = wb.Manufacturer,
                Model = wb.Model,
                DiameterMm = Number(WheelDiameterBox.Text, "Wheel diameter"),
                InertiaFactor = Number(WheelInertiaBox.Text, "Wheel inertia factor"),
                IsRound = wb.IsRound,
                IsCustom = wb.IsCustom
            },
            DriftPack = new DriftPackProfile
            {
                Id = pb.Id,
                Name = pb.Name,
                Category = pb.Category,
                GripBias = pb.GripBias,
                SelfSteerBias = pb.SelfSteerBias,
                DampingBias = pb.DampingBias,
                DetailBias = pb.DetailBias,
                IsCustom = pb.IsCustom
            },
            Car = new CarProfile
            {
                Id = cb.Id,
                PackId = pb.Id,
                DisplayName = cb.DisplayName,
                MassKg = Number(CarMassBox.Text, "Car mass"),
                PowerHp = Number(CarPowerBox.Text, "Power"),
                TorqueNm = cb.TorqueNm,
                Drivetrain = cb.Drivetrain,
                CasterDeg = Number(CasterBox.Text, "Caster"),
                SteeringLockPerSideDeg = Number(LockBox.Text, "Steering lock"),
                FrontTireWidthMm = Number(FrontTireBox.Text, "Front tire width"),
                RearTireWidthMm = cb.RearTireWidthMm,
                Grip = GripBox.SelectedItem is GripLevel g ? g : GripLevel.Medium,
                IsCustom = cb.IsCustom,
                IsInstalled = cb.IsInstalled,
                SourceFolderName = cb.SourceFolderName,
                SourceFolderPath = cb.SourceFolderPath,
                Author = cb.Author,
                DataSourceSummary = cb.DataSourceSummary,
                Confidence = CloneConfidence(cb.Confidence)
            },
            Intent = intent
        };
    }

    private static CarDataConfidence CloneConfidence(CarDataConfidence c) => new()
    {
        Mass = c.Mass,
        Power = c.Power,
        Caster = c.Caster,
        SteeringLock = c.SteeringLock,
        FrontTireWidth = c.FrontTireWidth,
        Grip = c.Grip
    };

    private void AutoDetectAc_Click(object sender, RoutedEventArgs e) => TryAutoDetect(showMessage: true);

    private void TryAutoDetect(bool showMessage)
    {
        var found = _scanner.TryFindInstall();
        if (!string.IsNullOrWhiteSpace(found))
        {
            AcRootBox.Text = found;
            ScanStatusText.Text = $"Detected Assetto Corsa: {found}";
            SaveAcRoot(found);
        }
        else if (showMessage)
        {
            MessageBox.Show("Assetto Corsa was not found in the usual Steam locations. Use Browse to select the game folder.", "Assetto Corsa Detection");
        }
    }

    private void BrowseAc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose your Assetto Corsa folder (the folder containing content\\cars).",
            Multiselect = false
        };

        if (Directory.Exists(AcRootBox.Text))
            dialog.InitialDirectory = AcRootBox.Text;

        if (dialog.ShowDialog() == true)
        {
            AcRootBox.Text = dialog.FolderName;
            ScanStatusText.Text = "Folder selected. Click Scan Installed Cars.";
        }
    }

    private void ScanCars_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _scanner.Scan(AcRootBox.Text);
            AcRootBox.Text = result.RootPath;
            _installedCars = result.Cars;
            SaveAcRoot(result.RootPath);

            var known = _installedCars.Count(x => x.PackId != "custom-pack");
            var unknown = _installedCars.Count - known;
            ScanStatusText.Text = $"Scanned {_installedCars.Count} installed cars. {known} matched a known drift pack; {unknown} are under Custom / Other Pack.";
            if (result.Warnings.Count > 0)
                ScanStatusText.Text += $" {result.Warnings.Count} folder(s) had metadata warnings.";

            RefreshCarsForPack();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Assetto Corsa Scan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void VerifyCarData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CarBox.SelectedItem is not CarProfile car)
                throw new InvalidOperationException("Select a car first.");

            car.MassKg = Number(CarMassBox.Text, "Car mass");
            car.PowerHp = Number(CarPowerBox.Text, "Power");
            car.CasterDeg = Number(CasterBox.Text, "Caster");
            car.SteeringLockPerSideDeg = Number(LockBox.Text, "Steering lock");
            car.FrontTireWidthMm = Number(FrontTireBox.Text, "Front tire width");
            car.Grip = GripBox.SelectedItem is GripLevel g ? g : GripLevel.Medium;
            car.Confidence = new CarDataConfidence
            {
                Mass = DataConfidence.High,
                Power = DataConfidence.High,
                Caster = DataConfidence.High,
                SteeringLock = DataConfidence.High,
                FrontTireWidth = DataConfidence.High,
                Grip = DataConfidence.High
            };
            car.DataSourceSummary = string.IsNullOrWhiteSpace(car.DataSourceSummary)
                ? "user verified"
                : car.DataSourceSummary + ", user verified edits";

            RenderConfidence(car.Confidence);
            CarSourceText.Text += " • Edited values marked verified.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Car Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            _currentCalibration = _calibrationStore.Get(_calibrationEngine.BuildKey(input));
            _lastResult = _engine.Generate(input, _currentCalibration, _azomPreferences);
            Render(input, _lastResult);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Atomic Drift Tuner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyCalibration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            string key = _calibrationEngine.BuildKey(input);
            var existing = _calibrationStore.Get(key);
            var feedback = new CalibrationFeedback
            {
                SelfSteer = (int)Math.Round(SelfSteerSlider.Value),
                FfbStrength = (int)Math.Round(StrengthSlider.Value),
                SteeringWeight = (int)Math.Round(WeightSlider.Value),
                DetailNoise = (int)Math.Round(DetailSlider.Value),
                Oscillation = (int)Math.Round(OscillationSlider.Value)
            };

            _currentCalibration = _calibrationEngine.ApplyFeedback(input, existing, feedback);
            _calibrationStore.Upsert(_currentCalibration);
            _lastResult = _engine.Generate(input, _currentCalibration, _azomPreferences);
            Render(input, _lastResult);
            ResetFeedbackControls();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Calibration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetCalibration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            string key = _calibrationEngine.BuildKey(input);
            _calibrationStore.Delete(key);
            _currentCalibration = null;
            _lastResult = _engine.Generate(input, null, _azomPreferences);
            Render(input, _lastResult);
            ResetFeedbackControls();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Calibration Reset", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetFeedbackControls()
    {
        SelfSteerSlider.Value = 0;
        StrengthSlider.Value = 0;
        WeightSlider.Value = 0;
        DetailSlider.Value = 0;
        OscillationSlider.Value = 0;
    }

    private void Render(TuneInput input, TuneResult result)
    {
        SummaryText.Text = $"{input.Hardware} • {input.Wheel.Model} • {input.DriftPack.Name} • {input.Car.DisplayName} • {input.Intent.Name}";

        var az = result.Azom;
        AzomText.Text =
            $"Rotation               {az.Core.WheelRotationAngleDeg,4}°\n" +
            $"Game FFB               {az.Core.GameFfbStrengthPct,3}%\n" +
            $"Base Torque            {az.Core.BaseTorqueOutputPct,3}%\n" +
            $"Maximum Wheel Speed    {az.Core.MaximumWheelSpeedPct,3}%\n" +
            $"Interpolation          {az.Core.Interpolation,3}\n" +
            $"Wheel Damper           {az.WheelbaseEffects.WheelDamperPct,3}%\n" +
            $"Wheel Friction         {az.WheelbaseEffects.WheelFrictionPct,3}%\n" +
            $"Natural Inertia        {az.WheelbaseEffects.NaturalInertia,3}\n" +
            $"High-Speed Damping     {az.HighSpeedDamping.DampingLevelPct,3}%\n" +
            $"Trigger Speed          {az.HighSpeedDamping.TriggerSpeedKph,3} kph\n" +
            $"EQ 10/15/25 Hz         {az.FfbEqualizer.Hz10}/{az.FfbEqualizer.Hz15}/{az.FfbEqualizer.Hz25}%\n" +
            $"EQ 40/60/100 Hz        {az.FfbEqualizer.Hz40}/{az.FfbEqualizer.Hz60}/{az.FfbEqualizer.Hz100}%\n" +
            $"Output Curve           {az.FfbOutputCurve.Preset}";

        AcText.Text =
            $"Gain                   {result.Ac.GainPct,3}%\n" +
            $"Filter                 {result.Ac.FilterPct,3}%\n" +
            $"Minimum Force          {result.Ac.MinimumForcePct,3}%\n" +
            $"Kerb                   {result.Ac.KerbPct,3}%\n" +
            $"Road                   {result.Ac.RoadPct,3}%\n" +
            $"Slip                   {result.Ac.SlipPct,3}%\n" +
            $"ABS                    {result.Ac.AbsPct,3}%";

        BehaviorText.Text =
            $"Self-steer             {result.SelfSteerScore,3}/100\n" +
            $"Stability              {result.StabilityScore,3}/100\n" +
            $"Detail                 {result.DetailScore,3}/100\n" +
            $"Est. peak torque       {result.EstimatedPeakWheelTorqueNm,5:0.0} Nm";

        RenderConfidence(input.Car.Confidence);
        RenderCalibrationStatus(_currentCalibration);

        var notes = new List<string>(result.Notes);
        if (input.Car.IsInstalled)
            notes.Insert(0, $"Installed AC car: {input.Car.SourceFolderName}. Scanner source: {input.Car.DataSourceSummary}.");
        NotesText.Text = "• " + string.Join("\n• ", notes);
    }

    private void RenderConfidence(CarDataConfidence c)
    {
        ConfidenceScoreText.Text = $"Overall confidence: {c.Score}/100";
        ConfidenceText.Text =
            $"Mass                  {ConfidenceName(c.Mass),-8}\n" +
            $"Power                 {ConfidenceName(c.Power),-8}\n" +
            $"Caster                {ConfidenceName(c.Caster),-8}\n" +
            $"Steering lock         {ConfidenceName(c.SteeringLock),-8}\n" +
            $"Front tire width      {ConfidenceName(c.FrontTireWidth),-8}\n" +
            $"Grip                  {ConfidenceName(c.Grip),-8}";
    }

    private static string ConfidenceName(DataConfidence value) => value.ToString().ToUpperInvariant();

    private void RenderCalibrationStatus(CalibrationProfile? calibration)
    {
        if (calibration is null || calibration.Samples == 0)
        {
            CalibrationStatusText.Text = "No saved calibration for this exact wheelbase + wheel + pack + car. Drive the generated setup, rate the feel below, then apply feedback.";
            return;
        }

        CalibrationStatusText.Text =
            $"Saved samples: {calibration.Samples} • wheel speed {Signed(calibration.WheelSpeedDelta)} • wheel damper {Signed(calibration.DampingDelta)} • " +
            $"wheel friction {Signed(calibration.FrictionDelta)} • base torque {Signed(calibration.TorqueLimitDelta)} • AC gain {Signed(calibration.AcGainDelta)} • interpolation {Signed(calibration.InterpolationDelta)}";
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private void UpdateCalibrationStatusSafely()
    {
        try
        {
            if (HardwareBox.SelectedItem is null || WheelBox.SelectedItem is null || PackBox.SelectedItem is null || CarBox.SelectedItem is null || IntentBox.SelectedItem is null)
                return;
            var input = BuildInput();
            _currentCalibration = _calibrationStore.Get(_calibrationEngine.BuildKey(input));
            RenderCalibrationStatus(_currentCalibration);
        }
        catch
        {
            // During selection changes some text fields may not be populated yet.
        }
    }

    private void SaveAcRoot(string root)
    {
        var app = _appSettingsStore.Load();
        app.AssettoCorsaRoot = root;
        app.AzomPreferences = _azomPreferences;
        _appSettingsStore.Save(app);
    }

    private void OpenAzomSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_azomSettingsWindow is not null)
            {
                RestoreAndActivate(_azomSettingsWindow);
                return;
            }

            var input = BuildInput();
            _currentCalibration = _calibrationStore.Get(_calibrationEngine.BuildKey(input));
            _lastResult = _engine.Generate(input, _currentCalibration, _azomPreferences);

            var window =
                new AzomSettingsWindow(input, _lastResult, _azomPreferences)
                {
                    Owner = this
                };

            _azomSettingsWindow = window;

            window.Closed += (_, _) =>
            {
                if (window.PreferencesChanged)
                {
                    _azomPreferences =
                        _appSettingsStore.Load().AzomPreferences ??
                        new AzomUserPreferences();

                    _lastResult =
                        _engine.Generate(
                            input,
                            _currentCalibration,
                            _azomPreferences);

                    Render(input, _lastResult);
                }

                _azomSettingsWindow = null;
            };

            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Full AZOM Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenCarSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_carSetupWindow is not null)
            {
                RestoreAndActivate(_carSetupWindow);
                return;
            }

            var input = BuildInput();

            if (!input.Car.IsInstalled ||
                string.IsNullOrWhiteSpace(input.Car.SourceFolderName))
            {
                MessageBox.Show(
                    "Scan Assetto Corsa and select an installed car before opening the car setup tuner. This lets Atomic Drift Tuner locate the correct saved-setup folder and setup definition.",
                    "Car Setup Tuner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var window =
                new CarSetupWindow(input)
                {
                    Owner = this
                };

            _carSetupWindow = window;
            window.Closed += (_, _) => _carSetupWindow = null;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Car Setup Tuner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (_themeWindow is not null)
        {
            RestoreAndActivate(_themeWindow);
            return;
        }

        // Modeless by design. ThemeWindow only updates Application-level
        // DynamicResource brushes (and persists AppSettings.Theme on Save).
        // It does not mutate tuning, telemetry, calibration, car setup or AZOM
        // state, so it is safe to leave open beside normal tool windows.
        var window =
            new ThemeWindow
            {
                Owner = this
            };

        _themeWindow = window;
        window.Closed += (_, _) => _themeWindow = null;
        window.Show();
    }

    private void OpenTelemetry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_telemetryWindow is not null)
            {
                RestoreAndActivate(_telemetryWindow);
                return;
            }

            var input = BuildInput();
            var window =
                new TelemetryWindow(input)
                {
                    Owner = this
                };

            _telemetryWindow = window;

            window.Closed += (_, _) =>
            {
                if (window.CalibrationChanged)
                {
                    _currentCalibration =
                        _calibrationStore.Get(
                            _calibrationEngine.BuildKey(input));

                    _lastResult =
                        _engine.Generate(
                            input,
                            _currentCalibration,
                            _azomPreferences);

                    Render(
                        input,
                        _lastResult);
                }

                _telemetryWindow = null;
            };

            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Telemetry Recorder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenTuningAssistant_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (_tuningAssistantWindow is not null)
            {
                RestoreAndActivate(_tuningAssistantWindow);
                return;
            }

            var input = BuildInput();

            var window =
                new TuningAssistantWindow(input)
                {
                    Owner = this
                };

            _tuningAssistantWindow = window;

            window.Closed += (_, _) =>
            {
                if (window.CalibrationChanged)
                {
                    _currentCalibration =
                        _calibrationStore.Get(
                            _calibrationEngine.BuildKey(input));

                    _lastResult =
                        _engine.Generate(
                            input,
                            _currentCalibration,
                            _azomPreferences);

                    Render(
                        input,
                        _lastResult);
                }

                _tuningAssistantWindow = null;
            };

            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Tuning Assistant",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenSetup_Click(object sender, RoutedEventArgs e) =>
        OpenSetupWizard(firstRun: false);

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnosticsWindow is not null)
        {
            RestoreAndActivate(_diagnosticsWindow);
            return;
        }

        var window =
            new DiagnosticsWindow
            {
                Owner = this
            };

        _diagnosticsWindow = window;
        window.Closed += (_, _) => _diagnosticsWindow = null;
        window.Show();
    }

    private static void RestoreAndActivate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var input = BuildInput();
            _currentCalibration = _calibrationStore.Get(_calibrationEngine.BuildKey(input));
            var result = _engine.Generate(input, _currentCalibration, _azomPreferences);
            var tune = new SavedTune
            {
                Name = $"{input.Hardware.Model} - {input.Wheel.Model} - {input.DriftPack.Name} - {input.Car.DisplayName}",
                Input = input,
                Result = result,
                Calibration = _currentCalibration
            };

            var dialog = new SaveFileDialog
            {
                Filter = "Atomic Drift Tune (*.adt.json)|*.adt.json|JSON (*.json)|*.json",
                FileName = "AtomicDriftTune.adt.json"
            };

            if (dialog.ShowDialog() == true)
                _store.Save(tune, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Atomic Drift Tune (*.adt.json;*.json)|*.adt.json;*.json|JSON (*.json)|*.json"
            };
            if (dialog.ShowDialog() != true) return;

            var tune = _store.Load(dialog.FileName);
            SelectOrAddHardware(tune.Input.Hardware);
            SelectOrAddWheel(tune.Input.Wheel);
            SelectOrAddPack(tune.Input.DriftPack);

            if (tune.Input.Car.IsInstalled && !_installedCars.Any(x => x.Id == tune.Input.Car.Id))
                _installedCars.Add(tune.Input.Car);

            RefreshCarsForPack(tune.Input.Car.Id);
            SelectOrAddCar(tune.Input.Car);

            int intentIndex = _intents.FindIndex(x => x.Kind == tune.Input.Intent.Kind);
            if (intentIndex >= 0) IntentBox.SelectedIndex = intentIndex;

            PeakTorqueBox.Text = tune.Input.Hardware.PeakTorqueNm.ToString("0.##", CultureInfo.InvariantCulture);
            WheelDiameterBox.Text = tune.Input.Wheel.DiameterMm.ToString("0.##", CultureInfo.InvariantCulture);
            WheelInertiaBox.Text = tune.Input.Wheel.InertiaFactor.ToString("0.00", CultureInfo.InvariantCulture);
            CarMassBox.Text = tune.Input.Car.MassKg.ToString("0.##", CultureInfo.InvariantCulture);
            CarPowerBox.Text = tune.Input.Car.PowerHp.ToString("0.##", CultureInfo.InvariantCulture);
            CasterBox.Text = tune.Input.Car.CasterDeg.ToString("0.##", CultureInfo.InvariantCulture);
            LockBox.Text = tune.Input.Car.SteeringLockPerSideDeg.ToString("0.##", CultureInfo.InvariantCulture);
            FrontTireBox.Text = tune.Input.Car.FrontTireWidthMm.ToString("0.##", CultureInfo.InvariantCulture);
            GripBox.SelectedItem = tune.Input.Car.Grip;

            if (tune.Calibration is not null)
                _calibrationStore.Upsert(tune.Calibration);

            var input = BuildInput();
            _currentCalibration = _calibrationStore.Get(_calibrationEngine.BuildKey(input));
            _lastResult = _engine.Generate(input, _currentCalibration, _azomPreferences);
            Render(input, _lastResult);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SelectOrAddHardware(HardwareProfile profile)
    {
        int index = _hardware.FindIndex(x => x.Id == profile.Id);
        if (index < 0) { _hardware.Add(profile); HardwareBox.Items.Refresh(); index = _hardware.Count - 1; }
        HardwareBox.SelectedIndex = index;
    }

    private void SelectOrAddWheel(SteeringWheelProfile profile)
    {
        int index = _wheels.FindIndex(x => x.Id == profile.Id);
        if (index < 0) { _wheels.Add(profile); WheelBox.Items.Refresh(); index = _wheels.Count - 1; }
        WheelBox.SelectedIndex = index;
    }

    private void SelectOrAddPack(DriftPackProfile profile)
    {
        int index = _packs.FindIndex(x => x.Id == profile.Id);
        if (index < 0) { _packs.Add(profile); PackBox.Items.Refresh(); index = _packs.Count - 1; }
        PackBox.SelectedIndex = index;
    }

    private void SelectOrAddCar(CarProfile profile)
    {
        int index = _visibleCars.FindIndex(x => x.Id == profile.Id);
        if (index < 0)
        {
            _visibleCars.Add(profile);
            CarBox.ItemsSource = null;
            CarBox.ItemsSource = _visibleCars;
            index = _visibleCars.Count - 1;
        }
        CarBox.SelectedIndex = index;
    }

    private void HardwareBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateHardware();
    private void WheelBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateWheel();
    private void PackBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshCarsForPack();
    private void CarBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => PopulateCar();
}
