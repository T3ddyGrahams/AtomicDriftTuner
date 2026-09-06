using System.Windows;
using System.Windows.Controls;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TuningAssistantWindow : Window
{
    private const double MinimumDriftSecondsForCalibration =
        5.0;

    private readonly TuneInput _input;

    private readonly TelemetrySessionStore _sessionStore =
        new();

    private readonly CarBehaviorProfileStore _behaviorStore =
        new();

    private readonly TelemetryTuningAssistantEngine _assistant =
        new();

    private readonly CalibrationStore _calibrationStore =
        new();

    private readonly CalibrationEngine _calibrationEngine =
        new();

    private readonly HashSet<long> _appliedSessionUtcTicks =
        [];

    private List<SavedTelemetrySession> _sessions =
        [];

    private CarBehaviorTarget _behavior =
        new();

    private TuningAssistantReport? _report;
    private SavedTelemetrySession? _reportSession;
    private CarSetupWindow? _guidedSetupWindow;

    private bool _closing;

    public bool CalibrationChanged { get; private set; }

    public TuningAssistantWindow(
        TuneInput input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        InitializeComponent();

        _input =
            input;

        SetupText.Text =
            $"{input.Hardware.Model} • " +
            $"{input.Wheel.Model} • " +
            $"{input.DriftPack.Name} • " +
            $"{input.Car.DisplayName} • " +
            $"{input.Intent.Name}";

        Closed +=
            TuningAssistantWindow_Closed;

        RefreshSessions();
    }

    private void RefreshSessions_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshSessions();
    }

    private void RefreshSessions()
    {
        if (_guidedSetupWindow is not null)
        {
            StatusText.Text =
                "Close the guided AC Setup window before refreshing telemetry sessions.";

            return;
        }

        long? selectedTicks =
            SessionBox.SelectedItem is SavedTelemetrySession selectedBefore
                ? selectedBefore.SessionUtc.Ticks
                : null;

        try
        {
            LoadBehavior();

            var sessions =
                _sessionStore.ListRecent(
                    _input,
                    30);

            _sessions =
                sessions ??
                [];

            SessionBox.ItemsSource =
                null;

            SessionBox.ItemsSource =
                _sessions;

            if (_sessions.Count == 0)
            {
                ClearReport(
                    "No saved telemetry session matches this exact wheelbase + wheel + pack + car.",
                    "Open the Telemetry Recorder, record a representative drift session, click Save Session, then return here.",
                    "No telemetry guidance available yet.",
                    "A saved telemetry session is required.");

                return;
            }

            var selectedIndex =
                0;

            if (selectedTicks is long ticks)
            {
                var preservedIndex =
                    _sessions.FindIndex(
                        session =>
                            session.SessionUtc.Ticks ==
                            ticks);

                if (preservedIndex >= 0)
                {
                    selectedIndex =
                        preservedIndex;
                }
            }

            SessionBox.SelectedIndex =
                selectedIndex;

            if (
                SessionBox.SelectedItem is SavedTelemetrySession selected &&
                !ReferenceEquals(
                    _reportSession,
                    selected))
            {
                BuildReportForSelection(
                    selected);
            }

            StatusText.Text =
                selectedIndex == 0
                    ? $"Loaded {_sessions.Count} matching saved session(s). Newest session selected."
                    : $"Loaded {_sessions.Count} matching saved session(s). Previous selection preserved.";
        }
        catch (Exception ex)
        {
            _sessions =
                [];

            SessionBox.ItemsSource =
                null;

            ClearReport(
                "ADT could not load matching telemetry sessions.",
                ex.Message,
                "Telemetry guidance is unavailable until the session list loads successfully.",
                "Tuning Assistant refresh failed.");

            MessageBox.Show(
                ex.Message,
                "Tuning Assistant",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SessionBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_guidedSetupWindow is not null)
        {
            return;
        }

        if (
            SessionBox.SelectedItem is not SavedTelemetrySession selected)
        {
            ClearReport(
                "Select a saved telemetry session to analyze it.",
                string.Empty,
                "No telemetry guidance is selected.",
                "Select a telemetry session.");

            return;
        }

        BuildReportForSelection(
            selected);
    }

    private void BuildReportForSelection(
        SavedTelemetrySession selected)
    {
        try
        {
            LoadBehavior();

            var previous =
                FindPreviousSession(
                    selected);

            var report =
                _assistant.Build(
                    _input,
                    _behavior,
                    selected,
                    previous);

            _report =
                report;

            _reportSession =
                selected;

            RenderReport(
                report,
                selected,
                previous);
        }
        catch (Exception ex)
        {
            _report =
                null;

            _reportSession =
                null;

            AssessmentGrid.ItemsSource =
                null;

            RecommendationGrid.ItemsSource =
                null;

            ComparisonGrid.ItemsSource =
                null;

            OverallText.Text =
                "ADT could not build a tuning report for the selected session.";

            ConfidenceText.Text =
                ex.Message;

            BehaviorGuidanceText.Text =
                "No temporary setup guidance is available from this failed report.";

            ApplyCalibrationButton.IsEnabled =
                false;

            OpenSetupButton.IsEnabled =
                false;

            StatusText.Text =
                "Selected-session analysis failed.";

            MessageBox.Show(
                ex.Message,
                "Tuning Assistant Analysis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private SavedTelemetrySession? FindPreviousSession(
        SavedTelemetrySession selected)
    {
        var index =
            _sessions.IndexOf(
                selected);

        return
            index >= 0 &&
            index + 1 < _sessions.Count
                ? _sessions[index + 1]
                : null;
    }

    private void LoadBehavior()
    {
        _behavior =
            _behaviorStore.Load(
                _input);

        _behavior.Normalize();

        RenderDesiredBehavior();
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
            (
                _behavior.IsNeutral
                    ? " • Neutral per-car behavior target."
                    : $" • {_behavior.ActiveBiasCount} active per-car behavior bias(es)."
            );
    }

    private void RenderReport(
        TuningAssistantReport report,
        SavedTelemetrySession selected,
        SavedTelemetrySession? previous)
    {
        AssessmentGrid.ItemsSource =
            null;

        AssessmentGrid.ItemsSource =
            report.Assessments;

        RecommendationGrid.ItemsSource =
            null;

        RecommendationGrid.ItemsSource =
            report.Recommendations;

        ComparisonGrid.ItemsSource =
            null;

        ComparisonGrid.ItemsSource =
            report.Comparison;

        OverallText.Text =
            report.OverallAssessment;

        ConfidenceText.Text =
            $"Overall confidence: {report.OverallConfidence.ToString().ToUpperInvariant()} • " +
            report.ConfidenceReason;

        BehaviorGuidanceText.Text =
            report.SuggestedBehaviorSummary;

        if (previous is null)
        {
            ComparisonHeaderText.Text =
                "No earlier matching saved session exists yet. " +
                "Save another run after testing a recommendation to unlock before/after comparison.";
        }
        else
        {
            ComparisonHeaderText.Text =
                $"Current: {selected.SessionUtc.ToLocalTime():g} • " +
                $"Previous: {previous.SessionUtc.ToLocalTime():g}. " +
                "Comparison is most useful when the same car, track, conditions, and driving task were used.";
        }

        UpdateActionAvailability(
            selected);

        StatusText.Text =
            $"Session analyzed: {selected.Analysis.DriftTimeSeconds:0}s detected drift • " +
            $"{selected.Analysis.TransitionCount} transition(s) • " +
            $"{report.Recommendations.Count} recommendation row(s).";
    }

    private void UpdateActionAvailability(
        SavedTelemetrySession selected)
    {
        var currentReport =
            _report is not null &&
            ReferenceEquals(
                _reportSession,
                selected);

        var alreadyApplied =
            _appliedSessionUtcTicks.Contains(
                selected.SessionUtc.Ticks);

        ApplyCalibrationButton.IsEnabled =
            _guidedSetupWindow is null &&
            currentReport &&
            !alreadyApplied &&
            !_report!.ProposedCalibration.IsNeutral &&
            selected.Analysis.DriftTimeSeconds >=
            MinimumDriftSecondsForCalibration;

        OpenSetupButton.IsEnabled =
            _guidedSetupWindow is null &&
            currentReport &&
            _report!.HasSuggestedBehaviorChange &&
            _input.Car.IsInstalled &&
            !string.IsNullOrWhiteSpace(
                _input.Car.SourceFolderName);
    }

    private void ClearReport(
        string overall,
        string confidence,
        string guidance,
        string status)
    {
        _report =
            null;

        _reportSession =
            null;

        AssessmentGrid.ItemsSource =
            null;

        RecommendationGrid.ItemsSource =
            null;

        ComparisonGrid.ItemsSource =
            null;

        OverallText.Text =
            overall;

        ConfidenceText.Text =
            confidence;

        BehaviorGuidanceText.Text =
            guidance;

        ComparisonHeaderText.Text =
            "ADT compares a selected session with the previous matching saved session when one exists.";

        ApplyCalibrationButton.IsEnabled =
            false;

        OpenSetupButton.IsEnabled =
            false;

        StatusText.Text =
            status;
    }

    private void ApplyCalibration_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            SessionBox.SelectedItem is not SavedTelemetrySession selected ||
            _report is null ||
            !ReferenceEquals(
                _reportSession,
                selected))
        {
            MessageBox.Show(
                "Select a telemetry session and wait for ADT to build its current report before applying calibration.",
                "Apply Telemetry Calibration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (_guidedSetupWindow is not null)
        {
            MessageBox.Show(
                "Close the guided AC Setup window before applying a telemetry calibration recommendation. " +
                "The report will refresh after that window closes.",
                "Apply Telemetry Calibration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        if (
            _report.ProposedCalibration.IsNeutral ||
            selected.Analysis.DriftTimeSeconds <
            MinimumDriftSecondsForCalibration)
        {
            ApplyCalibrationButton.IsEnabled =
                false;

            return;
        }

        if (
            _appliedSessionUtcTicks.Contains(
                selected.SessionUtc.Ticks))
        {
            ApplyCalibrationButton.IsEnabled =
                false;

            StatusText.Text =
                "This saved session's calibration recommendation has already been applied during this Tuning Assistant session.";

            return;
        }

        try
        {
            var suggestion =
                _report.ProposedCalibration;

            var answer =
                MessageBox.Show(
                    "Apply this telemetry recommendation to ADT's saved calibration for:\n\n" +
                    $"{_input.Hardware.Model} + {_input.Wheel.Model}\n" +
                    $"{_input.DriftPack.Name} • {_input.Car.DisplayName}\n" +
                    $"Saved session: {selected.SessionUtc.ToLocalTime():g}\n\n" +
                    $"Qualified drift evidence: {selected.Analysis.DriftTimeSeconds:0.0} s\n" +
                    $"Assistant confidence: {_report.OverallConfidence.ToString().ToUpperInvariant()}\n\n" +
                    $"Wheel speed {Signed(suggestion.WheelSpeedDelta)}\n" +
                    $"Wheel damper {Signed(suggestion.DampingDelta)}\n" +
                    $"Wheel friction {Signed(suggestion.FrictionDelta)}\n" +
                    $"High-speed damping {Signed(suggestion.SpeedDampingDelta)}\n" +
                    $"Base torque {Signed(suggestion.TorqueLimitDelta)}\n" +
                    $"AC gain {Signed(suggestion.AcGainDelta)}\n" +
                    $"Interpolation {Signed(suggestion.InterpolationDelta)}\n\n" +
                    "This updates ADT calibration only. It does NOT directly write AZOM or the wheelbase.\n\n" +
                    "Continue?",
                    "Apply Telemetry Calibration",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (answer !=
                MessageBoxResult.Yes)
            {
                return;
            }

            var key =
                _calibrationEngine.BuildKey(
                    _input);

            var existing =
                _calibrationStore.Get(
                    key);

            var next =
                _calibrationEngine.ApplyTelemetrySuggestion(
                    _input,
                    existing,
                    suggestion);

            _calibrationStore.Upsert(
                next);

            CalibrationChanged =
                true;

            _appliedSessionUtcTicks.Add(
                selected.SessionUtc.Ticks);

            UpdateActionAvailability(
                selected);

            StatusText.Text =
                "Telemetry recommendation saved to this ADT calibration. " +
                "No wheelbase setting was written. Return to the Dashboard and Generate Tune again to use the updated calibration.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Apply Telemetry Calibration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenSetupWithGuidance_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_guidedSetupWindow is not null)
        {
            RestoreAndActivateGuidedSetup();
            return;
        }

        if (
            SessionBox.SelectedItem is not SavedTelemetrySession selected ||
            _report is null ||
            !ReferenceEquals(
                _reportSession,
                selected) ||
            !_report.HasSuggestedBehaviorChange)
        {
            return;
        }

        if (
            !_input.Car.IsInstalled ||
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

        try
        {
            var window =
                new CarSetupWindow(
                    _input,
                    _report.SuggestedBehaviorTarget,
                    _report.SuggestedBehaviorSummary);

            var owner =
                ResolveVisibleOwner();

            if (owner is not null)
            {
                window.Owner =
                    owner;
            }

            _guidedSetupWindow =
                window;

            SetGuidedSetupState(
                isOpen: true);

            window.Closed +=
                GuidedSetupWindow_Closed;

            window.Show();
            window.Activate();

            StatusText.Text =
                "Guided AC Setup is open. Session selection and calibration apply are locked until it closes so this report cannot silently change underneath it.";
        }
        catch (Exception ex)
        {
            _guidedSetupWindow =
                null;

            SetGuidedSetupState(
                isOpen: false);

            MessageBox.Show(
                ex.Message,
                "Open Guided AC Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private Window? ResolveVisibleOwner()
    {
        if (
            Owner is Window owner &&
            owner.IsVisible)
        {
            return owner;
        }

        var main =
            Application.Current?.MainWindow;

        return
            main is not null &&
            main.IsVisible
                ? main
                : null;
    }

    private void RestoreAndActivateGuidedSetup()
    {
        var window =
            _guidedSetupWindow;

        if (window is null)
        {
            return;
        }

        if (
            window.WindowState ==
            WindowState.Minimized)
        {
            window.WindowState =
                WindowState.Normal;
        }

        window.Activate();
    }

    private void SetGuidedSetupState(
        bool isOpen)
    {
        SessionBox.IsEnabled =
            !isOpen;

        RefreshSessionsButton.IsEnabled =
            !isOpen;

        if (isOpen)
        {
            ApplyCalibrationButton.IsEnabled =
                false;

            OpenSetupButton.IsEnabled =
                false;

            return;
        }

        if (
            SessionBox.SelectedItem is SavedTelemetrySession selected &&
            _report is not null &&
            ReferenceEquals(
                _reportSession,
                selected))
        {
            UpdateActionAvailability(
                selected);
        }
    }

    private void GuidedSetupWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (
            sender is CarSetupWindow window)
        {
            window.Closed -=
                GuidedSetupWindow_Closed;
        }

        _guidedSetupWindow =
            null;

        if (_closing)
        {
            return;
        }

        SetGuidedSetupState(
            isOpen: false);

        if (
            SessionBox.SelectedItem is not SavedTelemetrySession selected)
        {
            return;
        }

        // The user may have explicitly saved the temporary guidance as this
        // car's Desired Behavior profile. Reload the profile and rebuild the
        // report against the same telemetry session before allowing another
        // action.
        BuildReportForSelection(
            selected);
    }

    private void TuningAssistantWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _closing =
            true;

        Closed -=
            TuningAssistantWindow_Closed;

        var guided =
            _guidedSetupWindow;

        _guidedSetupWindow =
            null;

        if (guided is null)
        {
            return;
        }

        try
        {
            guided.Closed -=
                GuidedSetupWindow_Closed;

            guided.Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing/closed.
        }
    }

    private static string Signed(
        int value) =>
        value >= 0
            ? $"+{value}"
            : value.ToString();
}
