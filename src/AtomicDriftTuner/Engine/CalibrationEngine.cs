using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class CalibrationEngine
{
    private const int MinimumTorqueLimitDelta =
        -20;

    private const int MaximumTorqueLimitDelta =
        20;

    private const int MinimumWheelSpeedDelta =
        -40;

    private const int MaximumWheelSpeedDelta =
        40;

    private const int MinimumDampingDelta =
        -15;

    private const int MaximumDampingDelta =
        20;

    private const int MinimumFrictionDelta =
        -10;

    private const int MaximumFrictionDelta =
        12;

    private const int MinimumSpeedDampingDelta =
        -10;

    private const int MaximumSpeedDampingDelta =
        20;

    private const int MinimumInterpolationDelta =
        -3;

    private const int MaximumInterpolationDelta =
        4;

    private const int MinimumAcGainDelta =
        -12;

    private const int MaximumAcGainDelta =
        12;

    public string BuildKey(
        TuneInput input)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        if (input.Hardware is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a calibration key because the wheelbase profile is missing.");
        }

        if (input.Wheel is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a calibration key because the steering-wheel profile is missing.");
        }

        if (input.DriftPack is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a calibration key because the drift-pack profile is missing.");
        }

        if (input.Car is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a calibration key because the car profile is missing.");
        }

        var hardwareId =
            NormalizeKeyPart(
                input.Hardware.Id,
                "wheelbase");

        var wheelId =
            NormalizeKeyPart(
                input.Wheel.Id,
                "steering wheel");

        var packId =
            NormalizeKeyPart(
                input.DriftPack.Id,
                "drift pack");

        var carId =
            NormalizeKeyPart(
                input.Car.Id,
                "car");

        // Keep the existing key format for compatibility with calibration
        // records created by earlier ADT beta releases.
        return
            $"{hardwareId}|{wheelId}|{packId}|{carId}"
                .ToLowerInvariant();
    }

    public CalibrationProfile ApplyFeedback(
        TuneInput input,
        CalibrationProfile? existing,
        CalibrationFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            feedback);

        var key =
            BuildKey(
                input);

        var next =
            CreateWorkingProfile(
                existing,
                key);

        // Self-steer:
        //
        // Positive feedback means self-steer is too fast.
        // Negative feedback means self-steer is too slow.
        //
        // Too slow  -> more wheel speed, less damping/friction.
        // Too fast  -> less wheel speed, more damping/friction.
        next.WheelSpeedDelta =
            AddScaledAndClamp(
                next.WheelSpeedDelta,
                feedback.SelfSteer,
                -6,
                MinimumWheelSpeedDelta,
                MaximumWheelSpeedDelta);

        next.DampingDelta =
            AddScaledAndClamp(
                next.DampingDelta,
                feedback.SelfSteer,
                2,
                MinimumDampingDelta,
                MaximumDampingDelta);

        next.FrictionDelta =
            AddScaledAndClamp(
                next.FrictionDelta,
                feedback.SelfSteer,
                1,
                MinimumFrictionDelta,
                MaximumFrictionDelta);

        // FFB strength:
        //
        // Too weak -> raise generated AZOM torque and AC gain.
        // Too strong -> reduce them.
        next.TorqueLimitDelta =
            AddScaledAndClamp(
                next.TorqueLimitDelta,
                feedback.FfbStrength,
                -3,
                MinimumTorqueLimitDelta,
                MaximumTorqueLimitDelta);

        next.AcGainDelta =
            AddScaledAndClamp(
                next.AcGainDelta,
                feedback.FfbStrength,
                -2,
                MinimumAcGainDelta,
                MaximumAcGainDelta);

        // Steering weight:
        //
        // Too heavy -> reduce damping/friction.
        // Too light -> add some control/weight.
        next.DampingDelta =
            AddScaledAndClamp(
                next.DampingDelta,
                feedback.SteeringWeight,
                -2,
                MinimumDampingDelta,
                MaximumDampingDelta);

        next.FrictionDelta =
            AddScaledAndClamp(
                next.FrictionDelta,
                feedback.SteeringWeight,
                -2,
                MinimumFrictionDelta,
                MaximumFrictionDelta);

        // Noise/detail:
        //
        // Too noisy -> add interpolation.
        // Too smooth -> reduce interpolation.
        next.InterpolationDelta =
            AddScaledAndClamp(
                next.InterpolationDelta,
                feedback.DetailNoise,
                1,
                MinimumInterpolationDelta,
                MaximumInterpolationDelta);

        // Oscillation is intentionally one-directional.
        //
        // A report of oscillation may add control, but a negative value must
        // never be interpreted as permission to remove stability controls.
        var oscillation =
            Math.Max(
                0,
                feedback.Oscillation);

        next.DampingDelta =
            AddScaledAndClamp(
                next.DampingDelta,
                oscillation,
                2,
                MinimumDampingDelta,
                MaximumDampingDelta);

        next.SpeedDampingDelta =
            AddScaledAndClamp(
                next.SpeedDampingDelta,
                oscillation,
                2,
                MinimumSpeedDampingDelta,
                MaximumSpeedDampingDelta);

        next.WheelSpeedDelta =
            AddScaledAndClamp(
                next.WheelSpeedDelta,
                oscillation,
                -2,
                MinimumWheelSpeedDelta,
                MaximumWheelSpeedDelta);

        FinalizeProfile(
            next,
            key);

        return next;
    }

    public CalibrationProfile ApplyTelemetrySuggestion(
        TuneInput input,
        CalibrationProfile? existing,
        TelemetryCalibrationSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            suggestion);

        var key =
            BuildKey(
                input);

        var next =
            CreateWorkingProfile(
                existing,
                key);

        next.TorqueLimitDelta =
            AddAndClamp(
                next.TorqueLimitDelta,
                suggestion.TorqueLimitDelta,
                MinimumTorqueLimitDelta,
                MaximumTorqueLimitDelta);

        next.WheelSpeedDelta =
            AddAndClamp(
                next.WheelSpeedDelta,
                suggestion.WheelSpeedDelta,
                MinimumWheelSpeedDelta,
                MaximumWheelSpeedDelta);

        next.DampingDelta =
            AddAndClamp(
                next.DampingDelta,
                suggestion.DampingDelta,
                MinimumDampingDelta,
                MaximumDampingDelta);

        next.FrictionDelta =
            AddAndClamp(
                next.FrictionDelta,
                suggestion.FrictionDelta,
                MinimumFrictionDelta,
                MaximumFrictionDelta);

        next.SpeedDampingDelta =
            AddAndClamp(
                next.SpeedDampingDelta,
                suggestion.SpeedDampingDelta,
                MinimumSpeedDampingDelta,
                MaximumSpeedDampingDelta);

        next.InterpolationDelta =
            AddAndClamp(
                next.InterpolationDelta,
                suggestion.InterpolationDelta,
                MinimumInterpolationDelta,
                MaximumInterpolationDelta);

        next.AcGainDelta =
            AddAndClamp(
                next.AcGainDelta,
                suggestion.AcGainDelta,
                MinimumAcGainDelta,
                MaximumAcGainDelta);

        FinalizeProfile(
            next,
            key);

        return next;
    }

    private static CalibrationProfile CreateWorkingProfile(
        CalibrationProfile? existing,
        string expectedKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedKey);

        CalibrationProfile profile;

        if (
            existing is not null &&
            string.Equals(
                existing.Key?.Trim(),
                expectedKey,
                StringComparison.OrdinalIgnoreCase))
        {
            // Only carry learned calibration forward when the persisted
            // identity exactly matches the current wheelbase/wheel/pack/car.
            profile =
                Clone(
                    existing);
        }
        else
        {
            // A missing or mismatched key means the supplied calibration
            // belongs to an unknown or different identity. Start clean rather
            // than transferring learned deltas between cars or hardware.
            profile =
                new CalibrationProfile();
        }

        profile.Key =
            expectedKey;

        ClampProfile(
            profile);

        if (profile.Samples < 0)
        {
            profile.Samples =
                0;
        }

        return profile;
    }

    private static void FinalizeProfile(
        CalibrationProfile profile,
        string key)
    {
        profile.Key =
            key;

        ClampProfile(
            profile);

        profile.Samples =
            profile.Samples == int.MaxValue
                ? int.MaxValue
                : profile.Samples + 1;

        profile.UpdatedUtc =
            DateTime.UtcNow;
    }

    private static void ClampProfile(
        CalibrationProfile profile)
    {
        profile.TorqueLimitDelta =
            Math.Clamp(
                profile.TorqueLimitDelta,
                MinimumTorqueLimitDelta,
                MaximumTorqueLimitDelta);

        profile.WheelSpeedDelta =
            Math.Clamp(
                profile.WheelSpeedDelta,
                MinimumWheelSpeedDelta,
                MaximumWheelSpeedDelta);

        profile.DampingDelta =
            Math.Clamp(
                profile.DampingDelta,
                MinimumDampingDelta,
                MaximumDampingDelta);

        profile.FrictionDelta =
            Math.Clamp(
                profile.FrictionDelta,
                MinimumFrictionDelta,
                MaximumFrictionDelta);

        profile.SpeedDampingDelta =
            Math.Clamp(
                profile.SpeedDampingDelta,
                MinimumSpeedDampingDelta,
                MaximumSpeedDampingDelta);

        profile.InterpolationDelta =
            Math.Clamp(
                profile.InterpolationDelta,
                MinimumInterpolationDelta,
                MaximumInterpolationDelta);

        profile.AcGainDelta =
            Math.Clamp(
                profile.AcGainDelta,
                MinimumAcGainDelta,
                MaximumAcGainDelta);
    }

    private static int AddAndClamp(
        int current,
        int delta,
        int minimum,
        int maximum)
    {
        var result =
            (long)current +
            delta;

        return ClampLongToIntRange(
            result,
            minimum,
            maximum);
    }

    private static int AddScaledAndClamp(
        int current,
        int feedback,
        int multiplier,
        int minimum,
        int maximum)
    {
        var result =
            (long)current +
            (long)feedback *
            multiplier;

        return ClampLongToIntRange(
            result,
            minimum,
            maximum);
    }

    private static int ClampLongToIntRange(
        long value,
        int minimum,
        int maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        if (value > maximum)
        {
            return maximum;
        }

        return (int)value;
    }

    private static string NormalizeKeyPart(
        string? value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidDataException(
                $"ADT cannot build a calibration key because the {description} has no ID.");
        }

        var normalized =
            value.Trim();

        if (
            normalized.Contains('|') ||
            normalized.Contains('\r') ||
            normalized.Contains('\n') ||
            normalized.Contains('\0'))
        {
            throw new InvalidDataException(
                $"ADT cannot build a calibration key because the {description} ID contains invalid characters.");
        }

        return normalized;
    }

    private static CalibrationProfile Clone(
        CalibrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(
            profile);

        return new CalibrationProfile
        {
            Key =
                profile.Key,

            Samples =
                profile.Samples,

            UpdatedUtc =
                profile.UpdatedUtc,

            TorqueLimitDelta =
                profile.TorqueLimitDelta,

            WheelSpeedDelta =
                profile.WheelSpeedDelta,

            DampingDelta =
                profile.DampingDelta,

            FrictionDelta =
                profile.FrictionDelta,

            SpeedDampingDelta =
                profile.SpeedDampingDelta,

            InterpolationDelta =
                profile.InterpolationDelta,

            AcGainDelta =
                profile.AcGainDelta
        };
    }
}
