using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TelemetryWindow : Window
{
    private const double MinimumDriftSecondsForCalibration =
        2.0;

    private readonly TuneInput _input;
    private readonly TelemetryHubService _telemetry;

    private readonly TelemetryAnalyzer _analyzer =
        new();

    private readonly TelemetrySessionStore _sessionStore =
        new();

    private readonly CalibrationStore _calibrationStore =
        new();

    private readonly CalibrationEngine _calibrationEngine =
        new();

    private readonly DispatcherTimer _timer =
        new()
        {
            Interval =
                TimeSpan.FromMilliseconds(
                    20)
        };

    private readonly Stopwatch _clock =
        new();

    private TelemetrySession _session;
    private TelemetryAnalysis? _analysis;

    private bool _recording;
    private bool _sessionSaved = true;
    private bool _sessionInterrupted;
    private bool _suggestionApplied;

    private int? _lastPacketId;

    public bool CalibrationChanged { get; private set; }

    public TelemetryWindow(
        TuneInput input,
        TelemetryHubService telemetry)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            telemetry);

        InitializeComponent();

        _input =
            input;

        _telemetry =
            telemetry;

        _session =
            NewSession();

        SetupText.Text =
            $"{input.Hardware.Model} • " +
            $"{input.Wheel.Model} • " +
            $"{input.DriftPack.Name} • " +
            $"{input.Car.DisplayName} • " +
            $"{input.Intent.Name}";

        StatusText.Text =
            "Start Assetto Corsa and enter an on-track session, then connect.";

        _timer.Tick +=
            Timer_Tick;

        Closing +=
            TelemetryWindow_Closing;

        Closed +=
            TelemetryWindow_Closed;
    }

    private TelemetrySession NewSession() =>
        new()
        {
            CarName =
                _input.Car.DisplayName,

            CarFolder =
                _input.Car.SourceFolderName,

            DriftPack =
                _input.DriftPack.Name,

            Wheelbase =
                _input.Hardware.Model,

            SteeringWheel =
                _input.Wheel.Model,

            DriftTarget =
                _input.Intent.Name,

            RequestedSampleRateHz =
                50
        };

    private void Connect_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!_telemetry.TryConnect())
            {
                RecordButton.IsEnabled =
                    false;

                LiveText.Text =
                    "Not connected.";

                MessageBox.Show(
                    "Assetto Corsa shared memory is not available yet. " +
                    "Start AC and enter an on-track session, then try again.",
                    "Telemetry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            RecordButton.IsEnabled =
                !_recording;

            StatusText.Text =
                "Connected to Assetto Corsa shared memory. Live telemetry is active.";

            _timer.Start();
        }
        catch (Exception ex)
        {
            RecordButton.IsEnabled =
                false;

            LiveText.Text =
                "Not connected.";

            MessageBox.Show(
                ex.Message,
                "Telemetry Connection",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Record_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (
                _analysis is not null &&
                _session.Samples.Count > 0 &&
                !_sessionSaved)
            {
                var answer =
                    MessageBox.Show(
                        "The previous analyzed telemetry session has not been saved.\n\n" +
                        "Starting a new recording will replace it in this window. Continue without saving it?",
                        "Unsaved Telemetry Session",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                if (answer !=
                    MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (
                !_telemetry.IsConnected &&
                !_telemetry.TryConnect())
            {
                MessageBox.Show(
                    "Connect to Assetto Corsa first.",
                    "Telemetry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _session =
                NewSession();

            _session.StartedUtc =
                DateTime.UtcNow;

            _analysis =
                null;

            _lastPacketId =
                null;

            _sessionSaved =
                false;

            _sessionInterrupted =
                false;

            _suggestionApplied =
                false;

            _telemetry.ResetDerivativeState();

            _clock.Restart();

            _recording =
                true;

            ConnectButton.IsEnabled =
                false;

            RecordButton.IsEnabled =
                false;

            StopButton.IsEnabled =
                true;

            SaveButton.IsEnabled =
                false;

            ApplyButton.IsEnabled =
                false;

            AnalysisText.Text =
                "Recording...";

            AssessmentText.Text =
                string.Empty;

            SuggestionText.Text =
                string.Empty;

            RecordingText.Text =
                "Waiting for unique Assetto Corsa physics frames...";

            StatusText.Text =
                "Recording at a requested 50 Hz. Keep ADT open while you drive.";

            _timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Telemetry Recording",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Stop_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        FinalizeRecording(
            interrupted: false,
            "Recording stopped.");

        if (
            _analysis is not null &&
            _session.Samples.Count > 0)
        {
            StatusText.Text =
                $"Analysis complete: {_session.Samples.Count} unique physics frames recorded. " +
                "Save the session if you want to keep it for later comparison.";
        }
    }

    private void Timer_Tick(
        object? sender,
        EventArgs e)
    {
        try
        {
            var hub =
                _telemetry.GetSnapshot();

            if (
                !hub.Connected ||
                hub.Sample is null)
            {
                throw new InvalidOperationException(
                    hub.Error ??
                    "Assetto Corsa telemetry is unavailable.");
            }

            var sample =
                hub.Sample;

            RenderLiveTelemetry(
                sample);

            if (
                _recording &&
                _lastPacketId !=
                sample.PacketId)
            {
                _session.Samples.Add(
                    sample);

                _lastPacketId =
                    sample.PacketId;

                RecordingText.Text =
                    $"Elapsed             {_clock.Elapsed.TotalSeconds,7:0.0} s\n" +
                    $"Samples             {_session.Samples.Count,7}\n" +
                    $"Current packet      {sample.PacketId,7}\n" +
                    $"Current drift       {(IsCurrentDrift(sample) ? "YES" : "no")}";
            }
        }
        catch (Exception ex)
        {
            HandleTelemetryDisconnect(
                ex);
        }
    }

    private void RenderLiveTelemetry(
        TelemetrySample sample)
    {
        LiveText.Text =
            $"Speed               {sample.SpeedKmh,7:0.0} km/h\n" +
            $"Body slip angle     {sample.SlipAngleDeg,7:0.0}°\n" +
            $"Steering angle      {sample.SteeringAngleDeg,7:0.0}°\n" +
            $"Steering rate       {sample.SteeringRateDegPerSec,7:0} °/s\n" +
            $"Yaw rate            {sample.YawRateDegPerSec,7:0.0}°/s\n" +
            $"Throttle            {sample.Throttle * 100,7:0}%\n" +
            $"Rear wheel slip     {sample.RearWheelSlipAvg,7:0.00}\n" +
            $"Final FFB           {sample.FinalFfb,7:0.000}";
    }

    private void HandleTelemetryDisconnect(
        Exception exception)
    {
        _timer.Stop();

        LiveText.Text =
            "Disconnected.";

        ConnectButton.IsEnabled =
            true;

        RecordButton.IsEnabled =
            false;

        StopButton.IsEnabled =
            false;

        if (_recording)
        {
            FinalizeRecording(
                interrupted: true,
                "Telemetry disconnected while recording: " +
                exception.Message);

            if (
                _analysis is not null &&
                _session.Samples.Count > 0)
            {
                StatusText.Text =
                    "Telemetry disconnected while recording. " +
                    $"ADT preserved and analyzed {_session.Samples.Count} captured frames. " +
                    "You may save this partial session, but calibration apply is disabled because the capture ended unexpectedly. " +
                    "Reconnect and record a clean representative session before applying a correction.";
            }

            return;
        }

        StatusText.Text =
            "Telemetry disconnected: " +
            exception.Message;
    }

    private void FinalizeRecording(
        bool interrupted,
        string statusPrefix)
    {
        _recording =
            false;

        _clock.Stop();

        _session.EndedUtc =
            DateTime.UtcNow;

        _sessionInterrupted =
            interrupted;

        ConnectButton.IsEnabled =
            true;

        StopButton.IsEnabled =
            false;

        RecordButton.IsEnabled =
            !interrupted &&
            _telemetry.IsConnected;

        ApplyButton.IsEnabled =
            false;

        if (_session.Samples.Count == 0)
        {
            _analysis =
                null;

            SaveButton.IsEnabled =
                false;

            _sessionSaved =
                true;

            AnalysisText.Text =
                "No unique physics frames were captured.";

            AssessmentText.Text =
                string.Empty;

            SuggestionText.Text =
                "No calibration correction can be evaluated without recorded telemetry.";

            RecordingText.Text =
                "Recording stopped with 0 captured frames.";

            StatusText.Text =
                statusPrefix +
                " No unique Assetto Corsa physics frames were captured.";

            return;
        }

        try
        {
            _analysis =
                _analyzer.Analyze(
                    _session);

            RenderAnalysis(
                _analysis);

            SaveButton.IsEnabled =
                true;

            _sessionSaved =
                false;

            ApplyButton.IsEnabled =
                CanApplyCurrentSuggestion();

            RecordingText.Text =
                $"Stopped              {_clock.Elapsed.TotalSeconds,7:0.0} s\n" +
                $"Samples              {_session.Samples.Count,7}\n" +
                $"Capture status       {(interrupted ? "INTERRUPTED" : "complete")}";
        }
        catch (Exception ex)
        {
            _analysis =
                null;

            SaveButton.IsEnabled =
                false;

            ApplyButton.IsEnabled =
                false;

            AnalysisText.Text =
                "Session analysis failed.";

            AssessmentText.Text =
                ex.Message;

            SuggestionText.Text =
                "No calibration correction was produced.";

            StatusText.Text =
                statusPrefix +
                " ADT could not analyze the captured session: " +
                ex.Message;
        }
    }

    private bool CanApplyCurrentSuggestion()
    {
        return
            !_recording &&
            !_sessionInterrupted &&
            !_suggestionApplied &&
            _analysis is not null &&
            !_analysis.CalibrationSuggestion.IsNeutral &&
            _analysis.DriftTimeSeconds >=
            MinimumDriftSecondsForCalibration;
    }

    private static bool IsCurrentDrift(
        TelemetrySample sample)
    {
        return
            sample.SpeedKmh >=
            20 &&
            Math.Abs(
                sample.SlipAngleDeg) >=
            10;
    }

    private void RenderAnalysis(
        TelemetryAnalysis analysis)
    {
        AnalysisText.Text =
            $"Duration             {analysis.DurationSeconds,7:0.0} s\n" +
            $"Samples              {analysis.SampleCount,7}\n" +
            $"Effective rate       {analysis.EffectiveSampleRateHz,7:0.0} Hz\n" +
            $"Drift time           {analysis.DriftTimeSeconds,7:0.0} s ({analysis.DriftTimePct:0}%)\n" +
            $"Drift entries        {analysis.DriftEntries,7}\n" +
            $"Average angle        {analysis.AverageDriftAngleDeg,7:0.0}°\n" +
            $"Peak angle           {analysis.PeakDriftAngleDeg,7:0.0}°\n" +
            $"Avg steer rate       {analysis.AverageSteeringRateDegPerSec,7:0} °/s\n" +
            $"Peak steer rate      {analysis.PeakSteeringRateDegPerSec,7:0} °/s\n" +
            $"Avg yaw rate         {analysis.AverageYawRateDegPerSec,7:0.0}°/s\n" +
            $"Avg drift speed      {analysis.AverageSpeedWhileDriftingKmh,7:0.0} km/h\n" +
            $"Avg |FFB| drift      {analysis.AverageFfbAbsWhileDrifting,7:0.000}\n" +
            $"FFB clipping         {analysis.FfbClippingPctWhileDrifting,7:0.0}%\n" +
            $"Transitions          {analysis.TransitionCount,7}\n" +
            $"Avg transition       {analysis.AverageTransitionSeconds,7:0.00} s\n" +
            $"Oscillation events   {analysis.OscillationEvents,7}\n" +
            $"Extreme-angle events {analysis.SpinEvents,7}";

        AssessmentText.Text =
            analysis.Assessment +
            (
                analysis.Findings.Count > 0
                    ? "\n\n• " +
                      string.Join(
                          "\n• ",
                          analysis.Findings)
                    : string.Empty
            );

        var suggestion =
            analysis.CalibrationSuggestion;

        SuggestionText.Text =
            suggestion.IsNeutral
                ? "No automatic correction proposed."
                : $"Wheel Speed          {Signed(suggestion.WheelSpeedDelta)}\n" +
                  $"Wheel Damper         {Signed(suggestion.DampingDelta)}\n" +
                  $"Wheel Friction       {Signed(suggestion.FrictionDelta)}\n" +
                  $"High-Speed Damping   {Signed(suggestion.SpeedDampingDelta)}\n" +
                  $"Base Torque          {Signed(suggestion.TorqueLimitDelta)}\n" +
                  $"AC Gain              {Signed(suggestion.AcGainDelta)}\n" +
                  $"Interpolation        {Signed(suggestion.InterpolationDelta)}" +
                  (
                      suggestion.Reasons.Count > 0
                          ? "\n\n" +
                            string.Join(
                                "\n",
                                suggestion.Reasons.Select(
                                    reason =>
                                        "• " +
                                        reason))
                          : string.Empty
                  );
    }

    private void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (
            _analysis is null ||
            _session.Samples.Count == 0)
        {
            return;
        }

        try
        {
            var paths =
                _sessionStore.Save(
                    _session,
                    _analysis);

            _sessionSaved =
                true;

            StatusText.Text =
                $"Saved session JSON and CSV to: {Path.GetDirectoryName(paths.JsonPath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Save Telemetry Session",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplySuggestion_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!CanApplyCurrentSuggestion())
        {
            if (_sessionInterrupted)
            {
                MessageBox.Show(
                    "This recording ended unexpectedly. Save it if it is useful for review, " +
                    "but record a clean representative session before applying an automatic calibration correction.",
                    "Telemetry Correction",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        try
        {
            var analysis =
                _analysis!;

            var answer =
                MessageBox.Show(
                    "Apply this telemetry correction to ADT's saved calibration for:\n\n" +
                    $"{_input.Hardware.Model} + {_input.Wheel.Model}\n" +
                    $"{_input.DriftPack.Name} • {_input.Car.DisplayName}\n\n" +
                    $"Evidence: {analysis.DriftTimeSeconds:0.0} seconds of qualified drift in this session.\n\n" +
                    "This affects future ADT recommendations for this exact hardware/car context. " +
                    "It does NOT directly write the wheelbase.\n\nContinue?",
                    "Apply Telemetry Correction",
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
                    analysis.CalibrationSuggestion);

            _calibrationStore.Upsert(
                next);

            CalibrationChanged =
                true;

            _suggestionApplied =
                true;

            ApplyButton.IsEnabled =
                false;

            StatusText.Text =
                "Telemetry correction saved to this ADT calibration. " +
                "No wheelbase setting was written. Return to the Dashboard and Generate Tune again to use the updated calibration.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Apply Telemetry Correction",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void TelemetryWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        // A context replacement or application shutdown must not silently
        // discard an active capture. Finalize it as interrupted and make a
        // best-effort recovery save. Interrupted sessions remain ineligible
        // for automatic calibration apply.
        FinalizeRecording(
            interrupted: true,
            "Recording ended because the telemetry workspace is closing.");

        if (
            _analysis is null ||
            _session.Samples.Count == 0 ||
            _sessionSaved)
        {
            return;
        }

        try
        {
            _sessionStore.Save(
                _session,
                _analysis);

            _sessionSaved =
                true;
        }
        catch
        {
            // Closing must remain reliable. The normal Save Session button
            // provides surfaced error handling while the workspace is open.
        }
    }

    private void TelemetryWindow_Closed(
        object? sender,
        EventArgs e)
    {
        _recording =
            false;

        _clock.Stop();
        _timer.Stop();

        _timer.Tick -=
            Timer_Tick;

        Closing -=
            TelemetryWindow_Closing;

        Closed -=
            TelemetryWindow_Closed;
    }

    private static string Signed(
        int value) =>
        value >= 0
            ? $"+{value}"
            : value.ToString();
}
