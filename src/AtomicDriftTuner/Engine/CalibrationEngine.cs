using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class CalibrationEngine
{
    public string BuildKey(TuneInput input) =>
        $"{input.Hardware.Id}|{input.Wheel.Id}|{input.DriftPack.Id}|{input.Car.Id}".ToLowerInvariant();

    public CalibrationProfile ApplyFeedback(TuneInput input, CalibrationProfile? existing, CalibrationFeedback feedback)
    {
        var next = existing is null
            ? new CalibrationProfile { Key = BuildKey(input) }
            : Clone(existing);

        // Self-steer: too slow => wheel speed up, damping/friction down.
        next.WheelSpeedDelta += -feedback.SelfSteer * 6;
        next.DampingDelta += feedback.SelfSteer * 2;
        next.FrictionDelta += feedback.SelfSteer;

        // Strength: too weak => raise AZOM Base Torque Output and AC gain.
        next.TorqueLimitDelta += -feedback.FfbStrength * 3;
        next.AcGainDelta += -feedback.FfbStrength * 2;

        // Weight: too heavy => reduce damping/friction; too light => add some.
        next.DampingDelta += -feedback.SteeringWeight * 2;
        next.FrictionDelta += -feedback.SteeringWeight * 2;

        // Noise/detail: too noisy => add interpolation; too smooth => reduce it.
        next.InterpolationDelta += feedback.DetailNoise;

        // Oscillation only ever adds control. Severe oscillation is intentionally noticeable.
        next.DampingDelta += feedback.Oscillation * 2;
        next.SpeedDampingDelta += feedback.Oscillation * 2;
        next.WheelSpeedDelta -= feedback.Oscillation * 2;

        next.TorqueLimitDelta = Math.Clamp(next.TorqueLimitDelta, -20, 20);
        next.WheelSpeedDelta = Math.Clamp(next.WheelSpeedDelta, -40, 40);
        next.DampingDelta = Math.Clamp(next.DampingDelta, -15, 20);
        next.FrictionDelta = Math.Clamp(next.FrictionDelta, -10, 12);
        next.SpeedDampingDelta = Math.Clamp(next.SpeedDampingDelta, -10, 20);
        next.InterpolationDelta = Math.Clamp(next.InterpolationDelta, -3, 4);
        next.AcGainDelta = Math.Clamp(next.AcGainDelta, -12, 12);
        next.Samples++;
        next.UpdatedUtc = DateTime.UtcNow;
        return next;
    }

    public CalibrationProfile ApplyTelemetrySuggestion(TuneInput input, CalibrationProfile? existing, TelemetryCalibrationSuggestion suggestion)
    {
        var next = existing is null
            ? new CalibrationProfile { Key = BuildKey(input) }
            : Clone(existing);

        next.TorqueLimitDelta = Math.Clamp(next.TorqueLimitDelta + suggestion.TorqueLimitDelta, -20, 20);
        next.WheelSpeedDelta = Math.Clamp(next.WheelSpeedDelta + suggestion.WheelSpeedDelta, -40, 40);
        next.DampingDelta = Math.Clamp(next.DampingDelta + suggestion.DampingDelta, -15, 20);
        next.FrictionDelta = Math.Clamp(next.FrictionDelta + suggestion.FrictionDelta, -10, 12);
        next.SpeedDampingDelta = Math.Clamp(next.SpeedDampingDelta + suggestion.SpeedDampingDelta, -10, 20);
        next.InterpolationDelta = Math.Clamp(next.InterpolationDelta + suggestion.InterpolationDelta, -3, 4);
        next.AcGainDelta = Math.Clamp(next.AcGainDelta + suggestion.AcGainDelta, -12, 12);
        next.Samples++;
        next.UpdatedUtc = DateTime.UtcNow;
        return next;
    }

    private static CalibrationProfile Clone(CalibrationProfile p) => new()
    {
        Key = p.Key,
        Samples = p.Samples,
        UpdatedUtc = p.UpdatedUtc,
        TorqueLimitDelta = p.TorqueLimitDelta,
        WheelSpeedDelta = p.WheelSpeedDelta,
        DampingDelta = p.DampingDelta,
        FrictionDelta = p.FrictionDelta,
        SpeedDampingDelta = p.SpeedDampingDelta,
        InterpolationDelta = p.InterpolationDelta,
        AcGainDelta = p.AcGainDelta
    };
}
