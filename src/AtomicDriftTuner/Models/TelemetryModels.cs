using System.Text.Json.Serialization;

namespace AtomicDriftTuner.Models;

public sealed class TelemetrySample
{
    public double TimeSeconds { get; set; }

    public int PacketId { get; set; }

    public double SpeedKmh { get; set; }

    public double Throttle { get; set; }

    public double Brake { get; set; }

    public double Clutch { get; set; }

    public int Gear { get; set; }

    public int Rpm { get; set; }

    public double SteeringAngleDeg { get; set; }

    public double SteeringRateDegPerSec { get; set; }

    public double SlipAngleDeg { get; set; }

    public double YawRateDegPerSec { get; set; }

    public double LateralG { get; set; }

    public double LongitudinalG { get; set; }

    public double FrontWheelSlipAvg { get; set; }

    public double RearWheelSlipAvg { get; set; }

    public double FinalFfb { get; set; }

    public double FrontTyrePressureAvg { get; set; }

    public double RearTyrePressureAvg { get; set; }
}

public sealed class TelemetrySession
{
    public const string CurrentSchema =
        "atomic-drift-tuner/telemetry-v0.5.0";

    private string _schema =
        CurrentSchema;

    private string _id =
        Guid.NewGuid().ToString("N");

    private string _carName =
        string.Empty;

    private string _driftPack =
        string.Empty;

    private string _wheelbase =
        string.Empty;

    private string _steeringWheel =
        string.Empty;

    private string _driftTarget =
        string.Empty;

    private List<TelemetrySample> _samples =
        [];

    public string Schema
    {
        get => _schema;

        set =>
            _schema =
                string.IsNullOrWhiteSpace(value)
                    ? CurrentSchema
                    : value.Trim();
    }

    public string Id
    {
        get => _id;

        set =>
            _id =
                string.IsNullOrWhiteSpace(value)
                    ? Guid.NewGuid().ToString("N")
                    : value.Trim();
    }

    public DateTime StartedUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime EndedUtc { get; set; }

    public string CarName
    {
        get => _carName;

        set =>
            _carName =
                value?.Trim() ??
                string.Empty;
    }

    public string? CarFolder { get; set; }

    public string DriftPack
    {
        get => _driftPack;

        set =>
            _driftPack =
                value?.Trim() ??
                string.Empty;
    }

    public string Wheelbase
    {
        get => _wheelbase;

        set =>
            _wheelbase =
                value?.Trim() ??
                string.Empty;
    }

    public string SteeringWheel
    {
        get => _steeringWheel;

        set =>
            _steeringWheel =
                value?.Trim() ??
                string.Empty;
    }

    public string DriftTarget
    {
        get => _driftTarget;

        set =>
            _driftTarget =
                value?.Trim() ??
                string.Empty;
    }

    public int RequestedSampleRateHz { get; set; } =
        50;

    public List<TelemetrySample> Samples
    {
        get => _samples;

        set =>
            _samples =
                value ??
                [];
    }
}

public sealed class TelemetryAnalysis
{
    private string _assessment =
        string.Empty;

    private List<string> _findings =
        [];

    private TelemetryCalibrationSuggestion _calibrationSuggestion =
        new();

    public double DurationSeconds { get; set; }

    public int SampleCount { get; set; }

    public double EffectiveSampleRateHz { get; set; }

    public double DriftTimeSeconds { get; set; }

    public double DriftTimePct { get; set; }

    public double AverageDriftAngleDeg { get; set; }

    public double PeakDriftAngleDeg { get; set; }

    public double AverageSteeringRateDegPerSec { get; set; }

    public double PeakSteeringRateDegPerSec { get; set; }

    public double AverageYawRateDegPerSec { get; set; }

    public double PeakYawRateDegPerSec { get; set; }

    public double AverageSpeedWhileDriftingKmh { get; set; }

    public double AverageFrontWheelSlipWhileDrifting { get; set; }

    public double AverageRearWheelSlipWhileDrifting { get; set; }

    public double AverageFfbAbsWhileDrifting { get; set; }

    public double FfbClippingPctWhileDrifting { get; set; }

    public int TransitionCount { get; set; }

    public double AverageTransitionSeconds { get; set; }

    public int OscillationEvents { get; set; }

    public int SpinEvents { get; set; }

    public int DriftEntries { get; set; }

    public string Assessment
    {
        get => _assessment;

        set =>
            _assessment =
                value ??
                string.Empty;
    }

    public List<string> Findings
    {
        get => _findings;

        set =>
            _findings =
                value ??
                [];
    }

    public TelemetryCalibrationSuggestion CalibrationSuggestion
    {
        get => _calibrationSuggestion;

        set =>
            _calibrationSuggestion =
                value ??
                new TelemetryCalibrationSuggestion();
    }
}

public sealed class TelemetryCalibrationSuggestion
{
    private List<string> _reasons =
        [];

    public int TorqueLimitDelta { get; set; }

    public int WheelSpeedDelta { get; set; }

    public int DampingDelta { get; set; }

    public int FrictionDelta { get; set; }

    public int SpeedDampingDelta { get; set; }

    public int InterpolationDelta { get; set; }

    public int AcGainDelta { get; set; }

    public List<string> Reasons
    {
        get => _reasons;

        set =>
            _reasons =
                value ??
                [];
    }

    [JsonIgnore]
    public bool IsNeutral =>
        TorqueLimitDelta == 0 &&
        WheelSpeedDelta == 0 &&
        DampingDelta == 0 &&
        FrictionDelta == 0 &&
        SpeedDampingDelta == 0 &&
        InterpolationDelta == 0 &&
        AcGainDelta == 0;
}