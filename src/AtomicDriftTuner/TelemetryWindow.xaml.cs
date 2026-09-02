using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner;

public partial class TelemetryWindow : Window
{
    private readonly TuneInput _input;
    private readonly AssettoCorsaTelemetryReader _reader = new();
    private readonly TelemetryAnalyzer _analyzer = new();
    private readonly TelemetrySessionStore _sessionStore = new();
    private readonly CalibrationStore _calibrationStore = new();
    private readonly CalibrationEngine _calibrationEngine = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly Stopwatch _clock = new();
    private TelemetrySession _session;
    private TelemetryAnalysis? _analysis;
    private bool _recording;
    private int _lastPacketId = -1;

    public bool CalibrationChanged { get; private set; }

    public TelemetryWindow(TuneInput input)
    {
        InitializeComponent();
        _input = input;
        _session = NewSession();
        SetupText.Text = $"{input.Hardware.Model} • {input.Wheel.Model} • {input.DriftPack.Name} • {input.Car.DisplayName} • {input.Intent.Name}";
        StatusText.Text = "Start Assetto Corsa and enter a driving session, then connect.";
        _timer.Tick += Timer_Tick;
        Closed += (_, _) => { _timer.Stop(); _reader.Dispose(); };
    }

    private TelemetrySession NewSession() => new()
    {
        CarName = _input.Car.DisplayName,
        CarFolder = _input.Car.SourceFolderName,
        DriftPack = _input.DriftPack.Name,
        Wheelbase = _input.Hardware.Model,
        SteeringWheel = _input.Wheel.Model,
        DriftTarget = _input.Intent.Name,
        RequestedSampleRateHz = 50
    };

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_reader.TryConnect())
        {
            RecordButton.IsEnabled = true;
            StatusText.Text = "Connected to Assetto Corsa shared memory. Live telemetry is active.";
            if (!_clock.IsRunning) _clock.Start();
            _timer.Start();
        }
        else
            MessageBox.Show("Assetto Corsa shared memory is not available yet. Start AC and enter an on-track session, then try again.", "Telemetry", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (!_reader.IsConnected && !_reader.TryConnect())
        {
            MessageBox.Show("Connect to Assetto Corsa first.", "Telemetry");
            return;
        }

        _session = NewSession();
        _session.StartedUtc = DateTime.UtcNow;
        _analysis = null;
        _lastPacketId = -1;
        _reader.ResetDerivativeState();
        _clock.Restart();
        _recording = true;
        RecordButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        SaveButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        AnalysisText.Text = "Recording...";
        AssessmentText.Text = "";
        SuggestionText.Text = "";
        StatusText.Text = "Recording at a requested 50 Hz. Keep Atomic Drift Tuner open while you drive.";
        _timer.Start();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _recording = false;
        _session.EndedUtc = DateTime.UtcNow;
        StopButton.IsEnabled = false;
        RecordButton.IsEnabled = true;
        _analysis = _analyzer.Analyze(_session);
        RenderAnalysis(_analysis);
        SaveButton.IsEnabled = _session.Samples.Count > 0;
        ApplyButton.IsEnabled = !_analysis.CalibrationSuggestion.IsNeutral && _analysis.DriftTimeSeconds >= 2;
        StatusText.Text = $"Analysis complete: {_session.Samples.Count} unique physics frames recorded.";
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var x = _reader.Read(_clock.Elapsed.TotalSeconds);
            LiveText.Text =
                $"Speed               {x.SpeedKmh,7:0.0} km/h\n" +
                $"Body slip angle     {x.SlipAngleDeg,7:0.0}°\n" +
                $"Steering angle      {x.SteeringAngleDeg,7:0.0}\n" +
                $"Steering rate       {x.SteeringRateDegPerSec,7:0} /s\n" +
                $"Yaw rate            {x.YawRateDegPerSec,7:0.0}°/s\n" +
                $"Throttle            {x.Throttle * 100,7:0}%\n" +
                $"Rear wheel slip     {x.RearWheelSlipAvg,7:0.00}\n" +
                $"Final FFB           {x.FinalFfb,7:0.000}";

            if (_recording && x.PacketId != _lastPacketId)
            {
                _session.Samples.Add(x);
                _lastPacketId = x.PacketId;
                RecordingText.Text =
                    $"Elapsed             {_clock.Elapsed.TotalSeconds,7:0.0} s\n" +
                    $"Samples             {_session.Samples.Count,7}\n" +
                    $"Current packet      {x.PacketId,7}\n" +
                    $"Current drift       {(x.SpeedKmh >= 20 && Math.Abs(x.SlipAngleDeg) >= 10 ? "YES" : "no")}";
            }
        }
        catch (Exception ex)
        {
            _timer.Stop();
            _recording = false;
            RecordButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            StatusText.Text = "Telemetry disconnected: " + ex.Message;
            _reader.Dispose();
        }
    }

    private void RenderAnalysis(TelemetryAnalysis a)
    {
        AnalysisText.Text =
            $"Duration             {a.DurationSeconds,7:0.0} s\n" +
            $"Samples              {a.SampleCount,7}\n" +
            $"Effective rate       {a.EffectiveSampleRateHz,7:0.0} Hz\n" +
            $"Drift time           {a.DriftTimeSeconds,7:0.0} s ({a.DriftTimePct:0}%)\n" +
            $"Drift entries        {a.DriftEntries,7}\n" +
            $"Average angle        {a.AverageDriftAngleDeg,7:0.0}°\n" +
            $"Peak angle           {a.PeakDriftAngleDeg,7:0.0}°\n" +
            $"Avg steer rate       {a.AverageSteeringRateDegPerSec,7:0} /s\n" +
            $"Peak steer rate      {a.PeakSteeringRateDegPerSec,7:0} /s\n" +
            $"Avg yaw rate         {a.AverageYawRateDegPerSec,7:0.0}°/s\n" +
            $"Avg drift speed      {a.AverageSpeedWhileDriftingKmh,7:0.0} km/h\n" +
            $"Avg |FFB| drift      {a.AverageFfbAbsWhileDrifting,7:0.000}\n" +
            $"FFB clipping         {a.FfbClippingPctWhileDrifting,7:0.0}%\n" +
            $"Transitions          {a.TransitionCount,7}\n" +
            $"Avg transition       {a.AverageTransitionSeconds,7:0.00} s\n" +
            $"Oscillation events   {a.OscillationEvents,7}\n" +
            $"Extreme-angle events {a.SpinEvents,7}";

        AssessmentText.Text = a.Assessment + (a.Findings.Count > 0 ? "\n\n• " + string.Join("\n• ", a.Findings) : "");
        var q = a.CalibrationSuggestion;
        SuggestionText.Text = q.IsNeutral ? "No automatic correction proposed." :
            $"Wheel Speed          {Signed(q.WheelSpeedDelta)}\n" +
            $"Wheel Damper         {Signed(q.DampingDelta)}\n" +
            $"Wheel Friction       {Signed(q.FrictionDelta)}\n" +
            $"High-Speed Damping   {Signed(q.SpeedDampingDelta)}\n" +
            $"Base Torque          {Signed(q.TorqueLimitDelta)}\n" +
            $"AC Gain              {Signed(q.AcGainDelta)}\n" +
            $"Interpolation        {Signed(q.InterpolationDelta)}" +
            (q.Reasons.Count > 0 ? "\n\n" + string.Join("\n", q.Reasons.Select(r => "• " + r)) : "");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis is null) return;
        var paths = _sessionStore.Save(_session, _analysis);
        StatusText.Text = $"Saved session JSON and CSV to: {Path.GetDirectoryName(paths.JsonPath)}";
    }

    private void ApplySuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (_analysis is null || _analysis.CalibrationSuggestion.IsNeutral) return;
        var key = _calibrationEngine.BuildKey(_input);
        var existing = _calibrationStore.Get(key);
        var next = _calibrationEngine.ApplyTelemetrySuggestion(_input, existing, _analysis.CalibrationSuggestion);
        _calibrationStore.Upsert(next);
        CalibrationChanged = true;
        ApplyButton.IsEnabled = false;
        StatusText.Text = "Telemetry correction saved to this setup's Atomic calibration. Close this window to regenerate the main tune.";
    }

    private static string Signed(int x) => x >= 0 ? $"+{x}" : x.ToString();
}
