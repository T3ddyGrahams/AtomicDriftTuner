using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class CarSetupWindow : Window
{
    private readonly TuneInput _input;
    private readonly AssettoCorsaSetupService _service = new();
    private readonly CarSetupTuningEngine _engine = new();
    private readonly CarBehaviorProfileStore _behaviorStore = new();
    private CarSetupAnalysis? _analysis;
    private readonly List<string> _savedSetups = [];
    private CarBehaviorTarget _behavior = new();
    private bool _uiReady;
    private bool _loadingBehavior;

    public CarSetupWindow(
        TuneInput input,
        CarBehaviorTarget? assistantBehaviorOverride = null,
        string? assistantGuidanceNote = null)
    {
        InitializeComponent();
        _input = input;
        _uiReady = true;

        AggressivenessBox.ItemsSource = Enum.GetValues<SetupAggressiveness>();
        AggressivenessBox.SelectedItem = SetupAggressiveness.Balanced;

        BehaviorPresetBox.ItemsSource = new[]
        {
            "Neutral",
            "Stable & Forgiving",
            "Fast Tandem",
            "Fast + Stable",
            "Aggressive Rotation",
            "Custom"
        };

        CarSummaryText.Text = $"{input.DriftPack.Name} • {input.Car.DisplayName} • session intent: {input.Intent.Name}";
        LoadBehaviorTarget();

        if (assistantBehaviorOverride is not null)
            ApplyAssistantBehaviorGuidance(
                assistantBehaviorOverride,
                assistantGuidanceNote);

        RefreshSavedSetups();
    }

    private void ApplyAssistantBehaviorGuidance(
        CarBehaviorTarget target,
        string? note)
    {
        _loadingBehavior = true;

        try
        {
            target.Normalize();

            _behavior =
                new CarBehaviorTarget
                {
                    Key = target.Key,
                    DisplayName = target.DisplayName,
                    UpdatedUtc = target.UpdatedUtc,
                    FrontEndBite = target.FrontEndBite,
                    RearGrip = target.RearGrip,
                    SelfSteerSpeed = target.SelfSteerSpeed,
                    TransitionSpeed = target.TransitionSpeed,
                    AngleStability = target.AngleStability,
                    ThrottleSteering = target.ThrottleSteering,
                    InitiationSharpness = target.InitiationSharpness
                };

            ApplyBehaviorToControls(
                _behavior);

            BehaviorPresetBox.SelectedItem =
                MatchPreset(
                    _behavior);

            UpdateBehaviorLabels();

            BehaviorStatusText.Text =
                "Telemetry Assistant guidance loaded TEMPORARILY. Generate uses these values immediately, but they are not saved for this car unless you click Save for This Car." +
                (string.IsNullOrWhiteSpace(note)
                    ? ""
                    : " " + note);
        }
        finally
        {
            _loadingBehavior = false;
        }
    }

    private void RefreshSavedSetups()
    {
        _savedSetups.Clear();
        _savedSetups.AddRange(_service.FindSavedSetups(_input.Car));
        BaselineBox.ItemsSource = null;
        BaselineBox.ItemsSource = _savedSetups.Select(x => new SetupChoice(x)).ToList();
        if (_savedSetups.Count > 0)
        {
            BaselineBox.SelectedIndex = 0;
            SetupStatusText.Text = $"Found {_savedSetups.Count} saved setup(s) for {_input.Car.SourceFolderName}.";
        }
        else
        {
            SetupStatusText.Text = "No saved setup was auto-detected. Save a setup in Assetto Corsa once, or use Browse Setup.";
        }
    }

    private void RefreshSetups_Click(object sender, RoutedEventArgs e) => RefreshSavedSetups();

    private void BrowseSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Assetto Corsa setup (*.ini)|*.ini|INI (*.ini)|*.ini" };
        if (dialog.ShowDialog() == true)
        {
            if (!_savedSetups.Contains(dialog.FileName, StringComparer.OrdinalIgnoreCase)) _savedSetups.Insert(0, dialog.FileName);
            BaselineBox.ItemsSource = _savedSetups.Select(x => new SetupChoice(x)).ToList();
            BaselineBox.SelectedIndex = 0;
        }
    }

    private void LoadBaseline_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = SelectedPath();
            _analysis = _service.LoadBaseline(path, _input.Car);
            SetupGrid.ItemsSource = _analysis.Parameters;
            RangeStatusText.Text = _analysis.RangeSummary;
            SetupStatusText.Text = $"Loaded {Path.GetFileName(path)} with {_analysis.Parameters.Count} adjustable saved values. Generate a car setup to see recommendations.";
            BehaviorBlendResultText.Text = "Generate a setup to see the actual parameter-by-parameter blend audit for this car.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Car Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void GenerateSetup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_analysis is null) LoadAnalysisFromSelection();
            var mode = AggressivenessBox.SelectedItem is SetupAggressiveness a ? a : SetupAggressiveness.Balanced;
            _behavior = ReadBehaviorFromControls();
            _analysis = _engine.Generate(_input, _analysis!, mode, _behavior);
            SetupGrid.ItemsSource = null;
            SetupGrid.ItemsSource = _analysis.Parameters;

            var behaviorSummary = _behavior.IsNeutral
                ? "neutral per-car behavior"
                : $"{_behavior.ActiveBiasCount} per-car behavior bias(es)";

            SetupStatusText.Text = $"Generated {_analysis.ChangedCount} recommended change(s) using {mode} tuning + {behaviorSummary}. Review the Blend and Reason columns before saving.";

            var blend = _analysis.BehaviorBlend;
            var topNotices = blend.Notices
                .Where(x => x.Kind.Contains("compromise", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .Select(x => $"{x.Parameter}: {x.Kind}")
                .ToList();

            BehaviorBlendResultText.Text =
                "Generated blend: " +
                blend.Summary +
                (topNotices.Count == 0
                    ? ""
                    : " Key compromises: " + string.Join(" • ", topNotices) + ".");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Car Setup Generation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveGenerated_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_analysis is null) throw new InvalidOperationException("Load and generate a setup first.");
            if (_analysis.ChangedCount == 0) throw new InvalidOperationException("Generate recommendations before saving.");

            var sourceDir = Path.GetDirectoryName(_analysis.BaselinePath) ?? _service.GetDefaultSetupsRoot();
            var safeStyle = new string(_input.Intent.Name.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
            if (string.IsNullOrWhiteSpace(safeStyle)) safeStyle = "Drift";

            var dialog = new SaveFileDialog
            {
                Filter = "Assetto Corsa setup (*.ini)|*.ini",
                InitialDirectory = sourceDir,
                FileName = $"Atomic_{safeStyle}_{DateTime.Now:yyyyMMdd_HHmm}.ini"
            };
            if (dialog.ShowDialog() != true) return;

            var written = _service.WriteGenerated(_analysis, dialog.FileName);
            SetupStatusText.Text = $"Saved: {written}";
            MessageBox.Show("Atomic setup saved. Load it from Assetto Corsa's Setup menu and test it before further calibration.", "Car Setup Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Car Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadBehaviorTarget()
    {
        _loadingBehavior = true;
        try
        {
            _behavior = _behaviorStore.Load(_input);
            ApplyBehaviorToControls(_behavior);
            BehaviorPresetBox.SelectedItem = MatchPreset(_behavior);
            UpdateBehaviorLabels();

            BehaviorStatusText.Text = _behavior.IsNeutral
                ? "No saved behavior bias for this car yet; neutral handling target is active."
                : $"Loaded saved behavior target for {_behavior.DisplayName} • {_behavior.ActiveBiasCount} active bias(es) • updated {_behavior.UpdatedUtc.ToLocalTime():g}.";
        }
        finally
        {
            _loadingBehavior = false;
        }
    }

    private void SaveBehavior_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _behavior = ReadBehaviorFromControls();
            _behaviorStore.Save(_input, _behavior);
            BehaviorPresetBox.SelectedItem = MatchPreset(_behavior);
            BehaviorStatusText.Text = _behavior.IsNeutral
                ? "Neutral behavior target saved for this car."
                : $"Saved {_behavior.ActiveBiasCount} desired-behavior bias(es) for {_input.Car.DisplayName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Car Behavior", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetBehavior_Click(object sender, RoutedEventArgs e)
    {
        _loadingBehavior = true;
        try
        {
            _behavior = new CarBehaviorTarget();
            ApplyBehaviorToControls(_behavior);
            BehaviorPresetBox.SelectedItem = "Neutral";
            UpdateBehaviorLabels();
            _behaviorStore.Save(_input, ReadBehaviorFromControls());
            BehaviorStatusText.Text = "Behavior target reset to neutral and saved for this car.";
        }
        finally
        {
            _loadingBehavior = false;
        }
    }

    private void BehaviorPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _loadingBehavior || BehaviorPresetBox.SelectedItem is not string preset)
            return;

        if (preset == "Custom")
            return;

        _loadingBehavior = true;
        try
        {
            var target = preset switch
            {
                "Stable & Forgiving" => new CarBehaviorTarget
                {
                    FrontEndBite = -1,
                    RearGrip = 2,
                    SelfSteerSpeed = -1,
                    TransitionSpeed = -1,
                    AngleStability = 2,
                    ThrottleSteering = -1,
                    InitiationSharpness = -1
                },
                "Fast Tandem" => new CarBehaviorTarget
                {
                    FrontEndBite = 1,
                    RearGrip = 1,
                    SelfSteerSpeed = 1,
                    TransitionSpeed = 1,
                    AngleStability = 1,
                    ThrottleSteering = 0,
                    InitiationSharpness = 1
                },
                "Fast + Stable" => new CarBehaviorTarget
                {
                    FrontEndBite = 1,
                    RearGrip = 1,
                    SelfSteerSpeed = 1,
                    TransitionSpeed = 2,
                    AngleStability = 2,
                    ThrottleSteering = 0,
                    InitiationSharpness = 1
                },
                "Aggressive Rotation" => new CarBehaviorTarget
                {
                    FrontEndBite = 2,
                    RearGrip = -1,
                    SelfSteerSpeed = 2,
                    TransitionSpeed = 2,
                    AngleStability = -1,
                    ThrottleSteering = 2,
                    InitiationSharpness = 2
                },
                _ => new CarBehaviorTarget()
            };

            ApplyBehaviorToControls(target);
            UpdateBehaviorLabels();
            BehaviorStatusText.Text = $"{preset} preset loaded. Generate uses it immediately; click Save for This Car to persist it.";
        }
        finally
        {
            _loadingBehavior = false;
        }
    }

    private void BehaviorSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady)
            return;

        UpdateBehaviorLabels();

        if (_loadingBehavior)
            return;

        _loadingBehavior = true;
        try
        {
            BehaviorPresetBox.SelectedItem = "Custom";
            BehaviorStatusText.Text = "Custom behavior target has unsaved changes. Generate will use the current slider values; Save for This Car makes them persistent.";
        }
        finally
        {
            _loadingBehavior = false;
        }
    }

    private CarBehaviorTarget ReadBehaviorFromControls()
    {
        var target = new CarBehaviorTarget
        {
            FrontEndBite = SliderInt(FrontEndBiteSlider),
            RearGrip = SliderInt(RearGripSlider),
            SelfSteerSpeed = SliderInt(SelfSteerSlider),
            TransitionSpeed = SliderInt(TransitionSlider),
            AngleStability = SliderInt(AngleStabilitySlider),
            ThrottleSteering = SliderInt(ThrottleSteeringSlider),
            InitiationSharpness = SliderInt(InitiationSlider)
        };
        target.Normalize();
        return target;
    }

    private void ApplyBehaviorToControls(CarBehaviorTarget target)
    {
        target.Normalize();
        FrontEndBiteSlider.Value = target.FrontEndBite;
        RearGripSlider.Value = target.RearGrip;
        SelfSteerSlider.Value = target.SelfSteerSpeed;
        TransitionSlider.Value = target.TransitionSpeed;
        AngleStabilitySlider.Value = target.AngleStability;
        ThrottleSteeringSlider.Value = target.ThrottleSteering;
        InitiationSlider.Value = target.InitiationSharpness;
    }

    private void UpdateBehaviorLabels()
    {
        if (!_uiReady) return;
        FrontEndBiteValueText.Text = Describe(SliderInt(FrontEndBiteSlider), "calmer", "more aggressive");
        RearGripValueText.Text = Describe(SliderInt(RearGripSlider), "looser", "more planted");
        SelfSteerValueText.Text = Describe(SliderInt(SelfSteerSlider), "slower", "faster");
        TransitionValueText.Text = Describe(SliderInt(TransitionSlider), "smoother", "quicker");
        AngleStabilityValueText.Text = Describe(SliderInt(AngleStabilitySlider), "more lively", "more stable");
        ThrottleSteeringValueText.Text = Describe(SliderInt(ThrottleSteeringSlider), "less throttle rotation", "more throttle rotation");
        InitiationValueText.Text = Describe(SliderInt(InitiationSlider), "more progressive", "sharper");
        UpdateBehaviorBlendPreview();
    }

    private void UpdateBehaviorBlendPreview()
    {
        if (!_uiReady)
            return;

        var target = ReadBehaviorFromControls();
        var preview = _engine.PreviewBehaviorBlend(target);

        var detail = preview.Details
            .Where(x => x.Contains("opposite", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        BehaviorBlendPreviewText.Text =
            "Preview: " +
            preview.Summary +
            (detail.Count == 0
                ? ""
                : " Likely compromises: " + string.Join(" • ", detail));

        BehaviorBlendPreviewText.ToolTip =
            preview.Details.Count == 0
                ? null
                : string.Join(Environment.NewLine, preview.Details);
    }

    private static int SliderInt(Slider slider) =>
        Math.Clamp((int)Math.Round(slider.Value), -2, 2);

    private static string Describe(int value, string negative, string positive) => value switch
    {
        -2 => $"Strong: {negative}",
        -1 => $"Mild: {negative}",
        1 => $"Mild: {positive}",
        2 => $"Strong: {positive}",
        _ => "Neutral"
    };

    private static string MatchPreset(CarBehaviorTarget target)
    {
        if (target.IsNeutral) return "Neutral";
        if (Same(target, -1, 2, -1, -1, 2, -1, -1)) return "Stable & Forgiving";
        if (Same(target, 1, 1, 1, 1, 1, 0, 1)) return "Fast Tandem";
        if (Same(target, 1, 1, 1, 2, 2, 0, 1)) return "Fast + Stable";
        if (Same(target, 2, -1, 2, 2, -1, 2, 2)) return "Aggressive Rotation";
        return "Custom";
    }

    private static bool Same(
        CarBehaviorTarget t,
        int front,
        int rear,
        int selfSteer,
        int transition,
        int stability,
        int throttle,
        int initiation) =>
        t.FrontEndBite == front &&
        t.RearGrip == rear &&
        t.SelfSteerSpeed == selfSteer &&
        t.TransitionSpeed == transition &&
        t.AngleStability == stability &&
        t.ThrottleSteering == throttle &&
        t.InitiationSharpness == initiation;

    private void LoadAnalysisFromSelection()
    {
        _analysis = _service.LoadBaseline(SelectedPath(), _input.Car);
        RangeStatusText.Text = _analysis.RangeSummary;
    }

    private string SelectedPath()
    {
        if (BaselineBox.SelectedItem is SetupChoice choice) return choice.Path;
        throw new InvalidOperationException("Choose or browse to a baseline Assetto Corsa setup first.");
    }

    private void BaselineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _analysis = null;
        SetupGrid.ItemsSource = null;
        if (BaselineBox.SelectedItem is SetupChoice choice)
            SetupStatusText.Text = $"Selected baseline: {choice.DisplayName}";
    }

    private sealed class SetupChoice
    {
        public SetupChoice(string path) { Path = path; DisplayName = BuildDisplay(path); }
        public string Path { get; }
        public string DisplayName { get; }
        public override string ToString() => DisplayName;
        private static string BuildDisplay(string path)
        {
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            var parent = Directory.GetParent(path)?.Name;
            return string.IsNullOrWhiteSpace(parent) ? file : $"{parent} / {file}";
        }
    }
}
