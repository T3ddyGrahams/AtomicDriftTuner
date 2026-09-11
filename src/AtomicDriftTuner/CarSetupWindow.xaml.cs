using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class CarSetupWindow : Window
{
    public event Action<string>? SetupFileSaved;
    private readonly TuneInput _input;
    private readonly AssettoCorsaSetupService _service = new();
    private readonly CarSetupTuningEngine _engine = new();
    private readonly CarBehaviorProfileStore _behaviorStore = new();
    private CarSetupAnalysis? _analysis;
    private readonly List<string> _savedSetups = [];
    private CarBehaviorTarget _behavior = new();
    private bool _uiReady;
    private bool _loadingBehavior;
    private string? _generatedSignature;

    public CarSetupWindow(
        TuneInput input,
        CarBehaviorTarget? assistantBehaviorOverride = null,
        string? assistantGuidanceNote = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        InitializeComponent();
        _input = input;

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

        _uiReady = true;

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
            _behavior =
                CloneBehaviorTarget(
                    target);

            _behavior.Normalize();

            ApplyBehaviorToControls(
                _behavior);

            BehaviorPresetBox.SelectedItem =
                MatchPreset(
                    _behavior);

            UpdateBehaviorLabels();

            BehaviorStatusText.Text =
                "Telemetry Assistant guidance loaded TEMPORARILY. Generate uses these values immediately, but they are not saved for this car unless you click Save Desired Behavior." +
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
        string? previousPath =
            TryGetSelectedPath();

        var discovered =
            _service.FindSavedSetups(
                _input.Car);

        _savedSetups.Clear();
        _savedSetups.AddRange(
            discovered.Distinct(
                StringComparer.OrdinalIgnoreCase));

        var choices =
            _savedSetups
                .Select(
                    path =>
                        new SetupChoice(
                            path))
                .ToList();

        BaselineBox.ItemsSource =
            choices;

        int selectedIndex =
            previousPath is null
                ? -1
                : choices.FindIndex(
                    choice =>
                        string.Equals(
                            choice.Path,
                            previousPath,
                            StringComparison.OrdinalIgnoreCase));

        if (selectedIndex < 0 &&
            choices.Count > 0)
        {
            selectedIndex =
                0;
        }

        BaselineBox.SelectedIndex =
            selectedIndex;

        if (choices.Count > 0)
        {
            SetupStatusText.Text =
                $"Found {choices.Count} saved setup(s) for {_input.Car.SourceFolderName}.";
        }
        else
        {
            ClearAnalysis(
                clearGrid: true);

            SetupStatusText.Text =
                "No saved setup was auto-detected. Save a setup in Assetto Corsa once, or use Browse Setup.";
        }
    }

    private void RefreshSetups_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            RefreshSavedSetups();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Refresh AC Setups",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BrowseSetup_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var dialog =
                new OpenFileDialog
                {
                    Filter =
                        "Assetto Corsa setup (*.ini)|*.ini|INI (*.ini)|*.ini",
                    Title =
                        $"Choose a baseline setup for {_input.Car.DisplayName}",
                    CheckFileExists =
                        true,
                    Multiselect =
                        false
                };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var selectedPath =
                Path.GetFullPath(
                    dialog.FileName);

            if (!string.Equals(
                    Path.GetExtension(
                        selectedPath),
                    ".ini",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The selected baseline must be an Assetto Corsa .ini setup file.");
            }

            bool discoveredForCurrentCar =
                _savedSetups.Contains(
                    selectedPath,
                    StringComparer.OrdinalIgnoreCase);

            if (!discoveredForCurrentCar)
            {
                var answer =
                    MessageBox.Show(
                        $"ADT did not auto-discover this file under the saved setups for {_input.Car.DisplayName}. " +
                        "Assetto Corsa setup files do not contain a reliable car identity, so ADT cannot prove from the file alone that it belongs to the selected car.\n\n" +
                        "Continue using it as the baseline for this car?",
                        "Confirm External Baseline",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (!discoveredForCurrentCar)
            {
                _savedSetups.Insert(
                    0,
                    selectedPath);
            }

            var choices =
                _savedSetups
                    .Select(
                        path =>
                            new SetupChoice(
                                path))
                    .ToList();

            BaselineBox.ItemsSource =
                choices;

            BaselineBox.SelectedIndex =
                choices.FindIndex(
                    choice =>
                        string.Equals(
                            choice.Path,
                            selectedPath,
                            StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Browse AC Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadBaseline_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var path =
                SelectedPath();

            _analysis =
                _service.LoadBaseline(
                    path,
                    _input.Car);

            _generatedSignature =
                null;

            SaveGeneratedButton.IsEnabled =
                false;

            SetupGrid.ItemsSource =
                _analysis.Parameters;

            RangeStatusText.Text =
                _analysis.RangeSummary;

            SetupStatusText.Text =
                $"Loaded {Path.GetFileName(path)} with {_analysis.Parameters.Count} adjustable saved values. Generate a car setup to see recommendations.";

            BehaviorBlendResultText.Text =
                "Generate a setup to see the actual parameter-by-parameter blend audit for this car.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Car Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void GenerateSetup_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var baselinePath =
                SelectedPath();

            var mode =
                AggressivenessBox.SelectedItem is SetupAggressiveness selectedMode
                    ? selectedMode
                    : SetupAggressiveness.Balanced;

            var behavior =
                ReadBehaviorFromControls();

            // Always reload the baseline before generation. This makes repeated
            // Generate clicks deterministic and prevents recommendations from
            // ever being based on a previously generated result.
            var baseline =
                _service.LoadBaseline(
                    baselinePath,
                    _input.Car);

            _behavior =
                behavior;

            _analysis =
                _engine.Generate(
                    _input,
                    baseline,
                    mode,
                    _behavior);

            _generatedSignature =
                BuildGenerationSignature(
                    baselinePath,
                    mode,
                    _behavior);

            SetupGrid.ItemsSource =
                null;

            SetupGrid.ItemsSource =
                _analysis.Parameters;

            RangeStatusText.Text =
                _analysis.RangeSummary;

            SaveGeneratedButton.IsEnabled =
                _analysis.ChangedCount > 0;

            var behaviorSummary =
                _behavior.IsNeutral
                    ? "neutral per-car behavior"
                    : $"{_behavior.ActiveBiasCount} per-car behavior bias(es)";

            SetupStatusText.Text =
                _analysis.ChangedCount == 0
                    ? $"Generated with {mode} tuning + {behaviorSummary}, but no setup changes are currently recommended."
                    : $"Generated {_analysis.ChangedCount} recommended change(s) using {mode} tuning + {behaviorSummary}. Review the Blend and Reason columns before saving.";

            var blend =
                _analysis.BehaviorBlend;

            var topNotices =
                blend.Notices
                    .Where(
                        notice =>
                            notice.Kind.Contains(
                                "compromise",
                                StringComparison.OrdinalIgnoreCase))
                    .Take(
                        3)
                    .Select(
                        notice =>
                            $"{notice.Parameter}: {notice.Kind}")
                    .ToList();

            BehaviorBlendResultText.Text =
                "Generated blend: " +
                blend.Summary +
                (
                    topNotices.Count == 0
                        ? ""
                        : " Key compromises: " +
                          string.Join(
                              " • ",
                              topNotices) +
                          "."
                );
        }
        catch (Exception ex)
        {
            ClearGeneratedSignature();

            MessageBox.Show(
                ex.Message,
                "Car Setup Generation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveGenerated_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (_analysis is null ||
                _generatedSignature is null)
            {
                throw new InvalidOperationException(
                    "Generate the setup before saving.");
            }

            if (_analysis.ChangedCount == 0)
            {
                throw new InvalidOperationException(
                    "The current generation has no recommended setup changes to save.");
            }

            var baselinePath =
                SelectedPath();

            var mode =
                AggressivenessBox.SelectedItem is SetupAggressiveness selectedMode
                    ? selectedMode
                    : SetupAggressiveness.Balanced;

            var currentBehavior =
                ReadBehaviorFromControls();

            var currentSignature =
                BuildGenerationSignature(
                    baselinePath,
                    mode,
                    currentBehavior);

            if (!string.Equals(
                    currentSignature,
                    _generatedSignature,
                    StringComparison.Ordinal))
            {
                InvalidateGeneratedRecommendations(
                    "The baseline, aggressiveness, or Desired Behavior changed after the last generation.");

                throw new InvalidOperationException(
                    "The setup controls changed after the last generation. Generate again before saving so the file matches what the UI currently shows.");
            }

            var sourceDir =
                Path.GetDirectoryName(
                    _analysis.BaselinePath);

            if (string.IsNullOrWhiteSpace(
                    sourceDir) ||
                !Directory.Exists(
                    sourceDir))
            {
                sourceDir =
                    _service.GetDefaultSetupsRoot();
            }

            var safeStyle =
                new string(
                    _input.Intent.Name
                        .Where(
                            ch =>
                                char.IsLetterOrDigit(ch) ||
                                ch == '-')
                        .ToArray());

            if (string.IsNullOrWhiteSpace(
                    safeStyle))
            {
                safeStyle =
                    "Drift";
            }

            var dialog =
                new SaveFileDialog
                {
                    Filter =
                        "Assetto Corsa setup (*.ini)|*.ini",
                    DefaultExt =
                        ".ini",
                    AddExtension =
                        true,
                    OverwritePrompt =
                        true,
                    InitialDirectory =
                        sourceDir,
                    FileName =
                        $"ADT_{safeStyle}_{DateTime.Now:yyyyMMdd_HHmm}.ini"
                };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var baselineFullPath =
                Path.GetFullPath(
                    _analysis.BaselinePath);

            var destinationFullPath =
                Path.GetFullPath(
                    dialog.FileName);

            if (string.Equals(
                    baselineFullPath,
                    destinationFullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "ADT will not overwrite the loaded baseline setup. Choose a different filename so the original remains available for comparison and recovery.");
            }

            var written =
                _service.WriteGenerated(
                    _analysis,
                    destinationFullPath);

            SetupStatusText.Text =
                $"Saved ADT setup: {written}";
            SetupFileSaved?.Invoke(written);

            MessageBox.Show(
                "ADT setup saved as a separate file. Load it from Assetto Corsa's Setup menu and test it before further calibration.",
                "Car Setup Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Save Car Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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

    private void SaveBehavior_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _behavior =
                ReadBehaviorFromControls();

            _behaviorStore.Save(
                _input,
                _behavior);

            _loadingBehavior =
                true;

            try
            {
                BehaviorPresetBox.SelectedItem =
                    MatchPreset(
                        _behavior);
            }
            finally
            {
                _loadingBehavior =
                    false;
            }

            BehaviorStatusText.Text =
                _behavior.IsNeutral
                    ? "Neutral behavior target saved for this car."
                    : $"Saved {_behavior.ActiveBiasCount} desired-behavior bias(es) for {_input.Car.DisplayName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Save Car Behavior",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ResetBehavior_Click(
        object sender,
        RoutedEventArgs e)
    {
        _loadingBehavior =
            true;

        try
        {
            _behavior =
                new CarBehaviorTarget();

            ApplyBehaviorToControls(
                _behavior);

            BehaviorPresetBox.SelectedItem =
                "Neutral";

            UpdateBehaviorLabels();

            _behavior =
                ReadBehaviorFromControls();

            _behaviorStore.Save(
                _input,
                _behavior);

            InvalidateGeneratedRecommendations(
                "Desired Behavior was reset to neutral.");

            BehaviorStatusText.Text =
                "Behavior target reset to neutral and saved for this car.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Reset Car Behavior",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _loadingBehavior =
                false;
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

            InvalidateGeneratedRecommendations(
                $"Desired Behavior preset changed to {preset}.");

            BehaviorStatusText.Text =
                $"{preset} preset loaded. Generate uses it immediately; click Save Desired Behavior to persist it.";
        }
        finally
        {
            _loadingBehavior = false;
        }
    }

    private void BehaviorSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady)
        {
            return;
        }

        UpdateBehaviorLabels();

        if (_loadingBehavior)
        {
            return;
        }

        _loadingBehavior =
            true;

        try
        {
            BehaviorPresetBox.SelectedItem =
                "Custom";

            InvalidateGeneratedRecommendations(
                "Desired Behavior changed after the last generation.");

            BehaviorStatusText.Text =
                "Custom behavior target has unsaved changes. Generate will use the current slider values; Save Desired Behavior makes them persistent.";
        }
        finally
        {
            _loadingBehavior =
                false;
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
        var path =
            SelectedPath();

        _analysis =
            _service.LoadBaseline(
                path,
                _input.Car);

        _generatedSignature =
            null;

        SaveGeneratedButton.IsEnabled =
            false;

        RangeStatusText.Text =
            _analysis.RangeSummary;
    }

    private void AggressivenessBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_uiReady)
        {
            return;
        }

        InvalidateGeneratedRecommendations(
            "Tuning aggressiveness changed after the last generation.");
    }

    private void InvalidateGeneratedRecommendations(
        string reason)
    {
        if (_generatedSignature is null)
        {
            SaveGeneratedButton.IsEnabled =
                false;

            return;
        }

        _generatedSignature =
            null;

        SaveGeneratedButton.IsEnabled =
            false;

        SetupStatusText.Text =
            $"{reason} Generate again before saving.";

        BehaviorBlendResultText.Text =
            "The displayed generated blend is now stale because setup controls changed. Generate again to refresh it.";
    }

    private void ClearGeneratedSignature()
    {
        _generatedSignature =
            null;

        SaveGeneratedButton.IsEnabled =
            false;
    }

    private void ClearAnalysis(
        bool clearGrid)
    {
        _analysis =
            null;

        ClearGeneratedSignature();

        if (clearGrid)
        {
            SetupGrid.ItemsSource =
                null;
        }

        RangeStatusText.Text =
            string.Empty;

        BehaviorBlendResultText.Text =
            "Generate a setup to see the actual parameter-by-parameter blend audit for this car.";
    }

    private string BuildGenerationSignature(
        string baselinePath,
        SetupAggressiveness mode,
        CarBehaviorTarget behavior)
    {
        var normalizedPath =
            Path.GetFullPath(
                    baselinePath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var baselineInfo =
            new FileInfo(
                normalizedPath);

        if (!baselineInfo.Exists)
        {
            throw new FileNotFoundException(
                "The selected baseline setup no longer exists. Refresh or choose another baseline.",
                normalizedPath);
        }

        return string.Join(
            "\u001F",
            normalizedPath.ToUpperInvariant(),
            baselineInfo.Length.ToString(),
            baselineInfo.LastWriteTimeUtc.Ticks.ToString(),
            ((int)mode).ToString(),
            behavior.FrontEndBite.ToString(),
            behavior.RearGrip.ToString(),
            behavior.SelfSteerSpeed.ToString(),
            behavior.TransitionSpeed.ToString(),
            behavior.AngleStability.ToString(),
            behavior.ThrottleSteering.ToString(),
            behavior.InitiationSharpness.ToString());
    }

    private string? TryGetSelectedPath()
    {
        return BaselineBox.SelectedItem is SetupChoice choice
            ? choice.Path
            : null;
    }

    private static CarBehaviorTarget CloneBehaviorTarget(
        CarBehaviorTarget source)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        return new CarBehaviorTarget
        {
            Key =
                source.Key,
            DisplayName =
                source.DisplayName,
            UpdatedUtc =
                source.UpdatedUtc,
            FrontEndBite =
                source.FrontEndBite,
            RearGrip =
                source.RearGrip,
            SelfSteerSpeed =
                source.SelfSteerSpeed,
            TransitionSpeed =
                source.TransitionSpeed,
            AngleStability =
                source.AngleStability,
            ThrottleSteering =
                source.ThrottleSteering,
            InitiationSharpness =
                source.InitiationSharpness
        };
    }

    private string SelectedPath()
    {
        if (BaselineBox.SelectedItem is SetupChoice choice) return choice.Path;
        throw new InvalidOperationException("Choose or browse to a baseline Assetto Corsa setup first.");
    }

    private void BaselineBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        ClearAnalysis(
            clearGrid: true);

        if (BaselineBox.SelectedItem is SetupChoice choice)
        {
            SetupStatusText.Text =
                $"Selected baseline: {choice.DisplayName}. Load or Generate to continue.";
        }
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
