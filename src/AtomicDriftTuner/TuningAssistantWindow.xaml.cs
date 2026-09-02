using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow : Window
{
    private readonly TuneInput _input;
    private readonly TelemetrySessionStore _sessionStore = new();
    private readonly CarBehaviorProfileStore _behaviorStore = new();
    private readonly TelemetryTuningAssistantEngine _assistant = new();
    private readonly CalibrationStore _calibrationStore = new();
    private readonly CalibrationEngine _calibrationEngine = new();

    private List<SavedTelemetrySession> _sessions = [];
    private CarBehaviorTarget _behavior = new();
    private TuningAssistantReport? _report;
    private CarSetupWindow? _guidedSetupWindow;

    public bool CalibrationChanged { get; private set; }

    public TuningAssistantWindow(TuneInput input)
    {
        InitializeComponent();
        _input = input;

        SetupText.Text =
            $"{input.Hardware.Model} • {input.Wheel.Model} • {input.DriftPack.Name} • {input.Car.DisplayName} • {input.Intent.Name}";

        _behavior =
            _behaviorStore.Load(
                input);

        RenderDesiredBehavior();
        RefreshSessions();
    }

    private void RefreshSessions_Click(
        object sender,
        RoutedEventArgs e) =>
        RefreshSessions();

    private void RefreshSessions()
    {
        _sessions =
            _sessionStore.ListRecent(
                _input,
                30);

        SessionBox.ItemsSource = null;
        SessionBox.ItemsSource = _sessions;

        if (_sessions.Count == 0)
        {
            _report = null;
            AssessmentGrid.ItemsSource = null;
            RecommendationGrid.ItemsSource = null;
            ComparisonGrid.ItemsSource = null;
            OverallText.Text =
                "No saved telemetry session matches this exact wheelbase + wheel + pack + car.";
            ConfidenceText.Text =
                "Open the Telemetry Recorder, record a representative drift session, click Save Session, then return here.";
            BehaviorGuidanceText.Text =
                "No telemetry guidance available yet.";
            StatusText.Text =
                "A saved telemetry session is required.";
            ApplyCalibrationButton.IsEnabled = false;
            OpenSetupButton.IsEnabled = false;
            return;
        }

        SessionBox.SelectedIndex = 0;
        StatusText.Text =
            $"Loaded {_sessions.Count} matching saved session(s). Newest session selected.";
    }

    private void SessionBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (SessionBox.SelectedItem is not SavedTelemetrySession selected)
            return;

        int index =
            _sessions.IndexOf(selected);

        SavedTelemetrySession? previous =
            index >= 0 &&
            index + 1 < _sessions.Count
                ? _sessions[index + 1]
                : null;

        _behavior =
            _behaviorStore.Load(
                _input);

        _report =
            _assistant.Build(
                _input,
                _behavior,
                selected,
                previous);

        RenderReport(
            _report,
            selected,
            previous);
    }

    private void RenderDesiredBehavior()
    {
        DesiredBehaviorText.Text =
            $"Front bite {Signed(_behavior.FrontEndBite)} • " +
            $"Rear grip {Signed(_behavior.RearGrip)} • " +
            $"Self-steer {Signed(_behavior.SelfSteerSpeed)} • " +
            $"Transition {Signed(_behavior.TransitionSpeed)} • " +
            $"Angle stability {Signed(_behavior.AngleStability)} • " +
            $"Throttle steering {Signed(_behavior.ThrottleSteering)} • " +
            $"Initiation {Signed(_behavior.InitiationSharpness)}" +
            (_behavior.IsNeutral
                ? " • Neutral per-car behavior target."
                : $" • {_behavior.ActiveBiasCount} active per-car behavior bias(es).");
    }

    private void RenderReport(
        TuningAssistantReport report,
        SavedTelemetrySession selected,
        SavedTelemetrySession? previous)
    {
        AssessmentGrid.ItemsSource = null;
        AssessmentGrid.ItemsSource = report.Assessments;

        RecommendationGrid.ItemsSource = null;
        RecommendationGrid.ItemsSource = report.Recommendations;

        ComparisonGrid.ItemsSource = null;
        ComparisonGrid.ItemsSource = report.Comparison;

        OverallText.Text =
            report.OverallAssessment;

        ConfidenceText.Text =
            $"Overall confidence: {report.OverallConfidence.ToString().ToUpperInvariant()} • {report.ConfidenceReason}";

        BehaviorGuidanceText.Text =
            report.SuggestedBehaviorSummary;

        if (previous is null)
        {
            ComparisonHeaderText.Text =
                "No earlier matching saved session exists yet. Save another run after testing a recommendation to unlock before/after comparison.";
        }
        else
        {
            ComparisonHeaderText.Text =
                $"Current: {selected.SessionUtc.ToLocalTime():g} • Previous: {previous.SessionUtc.ToLocalTime():g}. " +
                "Comparison is most useful when the same car/track/driving task was used.";
        }

        ApplyCalibrationButton.IsEnabled =
            !report.ProposedCalibration.IsNeutral &&
            selected.Analysis.DriftTimeSeconds >= 5;

        OpenSetupButton.IsEnabled =
            report.HasSuggestedBehaviorChange &&
            _input.Car.IsInstalled &&
            !string.IsNullOrWhiteSpace(
                _input.Car.SourceFolderName);

        StatusText.Text =
            $"Session analyzed: {selected.Analysis.DriftTimeSeconds:0}s detected drift • " +
            $"{selected.Analysis.TransitionCount} transition(s) • " +
            $"{report.Recommendations.Count} recommendation row(s).";
    }

    private void ApplyCalibration_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_report is null ||
            _report.ProposedCalibration.IsNeutral)
            return;

        var q = _report.ProposedCalibration;

        var answer =
            MessageBox.Show(
                "Apply this telemetry recommendation to Atomic's saved calibration for the current wheelbase + wheel + pack + car?\n\n" +
                $"Wheel speed {Signed(q.WheelSpeedDelta)}\n" +
                $"Wheel damper {Signed(q.DampingDelta)}\n" +
                $"Wheel friction {Signed(q.FrictionDelta)}\n" +
                $"High-speed damping {Signed(q.SpeedDampingDelta)}\n" +
                $"Base torque {Signed(q.TorqueLimitDelta)}\n" +
                $"AC gain {Signed(q.AcGainDelta)}\n" +
                $"Interpolation {Signed(q.InterpolationDelta)}\n\n" +
                "This updates Atomic's calibration only. It does not directly write AZOM hardware settings.",
                "Apply Telemetry Calibration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        string key =
            _calibrationEngine.BuildKey(
                _input);

        var existing =
            _calibrationStore.Get(
                key);

        var next =
            _calibrationEngine.ApplyTelemetrySuggestion(
                _input,
                existing,
                q);

        _calibrationStore.Upsert(
            next);

        CalibrationChanged = true;
        ApplyCalibrationButton.IsEnabled = false;
        StatusText.Text =
            "Telemetry recommendation saved to Atomic calibration. The main tune will regenerate after this window closes.";
    }

    private void OpenSetupWithGuidance_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_report is null ||
            !_report.HasSuggestedBehaviorChange)
            return;

        if (_guidedSetupWindow is not null)
        {
            if (_guidedSetupWindow.WindowState == WindowState.Minimized)
                _guidedSetupWindow.WindowState = WindowState.Normal;

            _guidedSetupWindow.Activate();
            return;
        }

        if (!_input.Car.IsInstalled ||
            string.IsNullOrWhiteSpace(
                _input.Car.SourceFolderName))
        {
            MessageBox.Show(
                "Select an installed Assetto Corsa car before opening the AC setup tuner.",
                "Tuning Assistant",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window =
            new CarSetupWindow(
                _input,
                _report.SuggestedBehaviorTarget,
                _report.SuggestedBehaviorSummary)
            {
                Owner = this
            };

        _guidedSetupWindow = window;

        window.Closed += (_, _) =>
        {
            _guidedSetupWindow = null;

            _behavior =
                _behaviorStore.Load(
                    _input);

            RenderDesiredBehavior();

            // Re-run the report in case the user explicitly saved the guided
            // Desired Behavior profile while inside the setup tuner.
            if (SessionBox.SelectedItem is SavedTelemetrySession selected)
            {
                int index =
                    _sessions.IndexOf(selected);

                SavedTelemetrySession? previous =
                    index >= 0 &&
                    index + 1 < _sessions.Count
                        ? _sessions[index + 1]
                        : null;

                _report =
                    _assistant.Build(
                        _input,
                        _behavior,
                        selected,
                        previous);

                RenderReport(
                    _report,
                    selected,
                    previous);
            }
        };

        window.Show();
    }

    private static string Signed(int value) =>
        value >= 0
            ? $"+{value}"
            : value.ToString();
}
