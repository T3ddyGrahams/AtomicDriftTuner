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
    public string Schema { get; set; } = "atomic-drift-tuner/telemetry-v0.5.0";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime EndedUtc { get; set; }
    public string CarName { get; set; } = "";
    public string? CarFolder { get; set; }
    public string DriftPack { get; set; } = "";
    public string Wheelbase { get; set; } = "";
    public string SteeringWheel { get; set; } = "";
    public string DriftTarget { get; set; } = "";
    public int RequestedSampleRateHz { get; set; } = 50;
    public List<TelemetrySample> Samples { get; set; } = [];
}

public sealed class TelemetryAnalysis
{
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
    public string Assessment { get; set; } = "";
    public List<string> Findings { get; set; } = [];
    public TelemetryCalibrationSuggestion CalibrationSuggestion { get; set; } = new();
}

public sealed class TelemetryCalibrationSuggestion
{
    public int TorqueLimitDelta { get; set; }
    public int WheelSpeedDelta { get; set; }
    public int DampingDelta { get; set; }
    public int FrictionDelta { get; set; }
    public int SpeedDampingDelta { get; set; }
    public int InterpolationDelta { get; set; }
    public int AcGainDelta { get; set; }
    public List<string> Reasons { get; set; } = [];
    public bool IsNeutral => TorqueLimitDelta == 0 && WheelSpeedDelta == 0 && DampingDelta == 0 &&
                             FrictionDelta == 0 && SpeedDampingDelta == 0 && InterpolationDelta == 0 && AcGainDelta == 0;
}
