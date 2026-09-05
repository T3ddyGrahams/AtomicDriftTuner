using AtomicDriftTuner.Data;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TuningEngine
{
    private const int MinimumAcGain =
        35;

    private const int MaximumAcGain =
        90;

    public TuneResult Generate(
        TuneInput input,
        CalibrationProfile? calibration = null,
        AzomUserPreferences? preferences = null)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        Validate(
            input);

        preferences ??=
            new AzomUserPreferences();

        var hardware =
            input.Hardware;

        var wheel =
            input.Wheel;

        var pack =
            input.DriftPack;

        var car =
            input.Car;

        var intent =
            input.Intent;

        // Normalize all driver-intent inputs before they participate in the
        // tuning formulas. Corrupt or manually edited profile data must not
        // be able to push intermediate calculations outside their intended
        // 0..1 domain.
        var selfSteerIntent =
            Clamp01(
                intent.SelfSteer);

        var detailIntent =
            Clamp01(
                intent.Detail);

        var weightIntent =
            Clamp01(
                intent.Weight);

        var stabilityIntent =
            Clamp01(
                intent.Stability);

        var grip =
            car.Grip switch
            {
                GripLevel.Low =>
                    -0.12,

                GripLevel.High =>
                    0.16,

                _ =>
                    0.0
            };

        grip =
            Math.Clamp(
                grip +
                FiniteOrZero(
                    pack.GripBias),
                -0.20,
                0.30);

        var torqueClass =
            Clamp01(
                (
                    hardware.PeakTorqueNm -
                    3.0
                ) /
                22.0);

        var rimDiameter =
            Clamp01(
                (
                    wheel.DiameterMm -
                    270.0
                ) /
                130.0);

        var rimInertia =
            Clamp01(
                (
                    wheel.InertiaFactor -
                    0.70
                ) /
                0.80);

        var caster =
            Clamp01(
                (
                    car.CasterDeg -
                    5.0
                ) /
                6.0);

        var tire =
            Clamp01(
                (
                    car.FrontTireWidthMm -
                    205.0
                ) /
                100.0);

        var mass =
            Clamp01(
                (
                    car.MassKg -
                    950.0
                ) /
                700.0);

        var effectiveSelfSteer =
            Clamp01(
                selfSteerIntent +
                FiniteOrZero(
                    pack.SelfSteerBias));

        var effectiveDetail =
            Clamp01(
                detailIntent +
                FiniteOrZero(
                    pack.DetailBias));

        var dampingBias =
            FiniteOrZero(
                pack.DampingBias);

        var targetNm =
            4.1 +
            effectiveDetail * 1.6 +
            weightIntent * 1.1 +
            Math.Max(
                0,
                grip) * 1.6 +
            tire * 0.45 +
            mass * 0.25 +
            rimDiameter * 0.35;

        targetNm =
            Math.Clamp(
                targetNm,
                3.5,
                8.8);

        // AZOM's observed Base Torque Output range is 50..100 rather than
        // 0..100. The catalog remains the authority for the actual supported
        // range.
        var baseTorque =
            RoundClamp(
                targetNm /
                hardware.PeakTorqueNm *
                100.0,
                AzomSettingCatalog.BaseTorqueOutput.Min,
                AzomSettingCatalog.BaseTorqueOutput.Max);

        var gameFfb =
            RoundClamp(
                93 +
                effectiveDetail * 7,
                AzomSettingCatalog.GameFfbStrength.Min,
                AzomSettingCatalog.GameFfbStrength.Max);

        var wheelSpeed =
            RoundClamp(
                92 +
                effectiveSelfSteer * 88 -
                rimDiameter * 24 -
                rimInertia * 18 +
                caster * 7,
                AzomSettingCatalog.MaximumWheelSpeed.Min,
                AzomSettingCatalog.MaximumWheelSpeed.Max);

        var wheelDamper =
            RoundClamp(
                5 +
                stabilityIntent * 18 +
                rimInertia * 8 +
                rimDiameter * 5 +
                caster * 3 +
                grip * 11 +
                dampingBias * 30 -
                effectiveSelfSteer * 11,
                AzomSettingCatalog.WheelDamper.Min,
                AzomSettingCatalog.WheelDamper.Max);

        var wheelFriction =
            RoundClamp(
                2 +
                stabilityIntent * 8 +
                rimInertia * 4 +
                weightIntent * 3 -
                effectiveSelfSteer * 5,
                AzomSettingCatalog.WheelFriction.Min,
                AzomSettingCatalog.WheelFriction.Max);

        var highSpeedDamping =
            RoundClamp(
                3 +
                stabilityIntent * 13 +
                torqueClass * 5 +
                dampingBias * 15 -
                effectiveSelfSteer * 7,
                AzomSettingCatalog.HighSpeedDampingLevel.Min,
                AzomSettingCatalog.HighSpeedDampingLevel.Max);

        var highSpeedTrigger =
            RoundClamp(
                90 +
                stabilityIntent * 55 +
                Math.Max(
                    0,
                    grip) * 45 +
                torqueClass * 20,
                AzomSettingCatalog.HighSpeedTriggerSpeed.Min,
                AzomSettingCatalog.HighSpeedTriggerSpeed.Max);

        var interpolation =
            RoundClamp(
                4 -
                effectiveDetail * 3 +
                torqueClass * 0.5,
                AzomSettingCatalog.Interpolation.Min,
                AzomSettingCatalog.Interpolation.Max);

        var maximumSupportedRotation =
            ResolveMaximumRotation(
                hardware.MaxRotationDeg);

        var rotation =
            RoundClamp(
                car.SteeringLockPerSideDeg *
                15.0,
                AzomSettingCatalog.WheelRotationAngle.Min,
                maximumSupportedRotation);

        var naturalInertia =
            RoundClamp(
                100 +
                rimInertia * 150 +
                rimDiameter * 45 +
                weightIntent * 20,
                AzomSettingCatalog.NaturalInertia.Min,
                AzomSettingCatalog.NaturalInertia.Max);

        var steeringWheelInertia =
            RoundClamp(
                350 +
                rimInertia * 1250 +
                rimDiameter * 650 +
                weightIntent * 250,
                AzomSettingCatalog.SteeringWheelInertia.Min,
                AzomSettingCatalog.SteeringWheelInertia.Max);

        var softLimitStiffness =
            RoundClamp(
                3.5 +
                torqueClass * 2.0 +
                stabilityIntent * 1.5,
                AzomSettingCatalog.SoftLimitStiffness.Min,
                AzomSettingCatalog.SoftLimitStiffness.Max);

        var eq10 =
            RoundClamp(
                100 +
                weightIntent * 14 +
                stabilityIntent * 4,
                AzomSettingCatalog.EqualizerBand.Min,
                AzomSettingCatalog.EqualizerBand.Max);

        var eq15 =
            RoundClamp(
                100 +
                weightIntent * 18 +
                effectiveDetail * 3,
                AzomSettingCatalog.EqualizerBand.Min,
                AzomSettingCatalog.EqualizerBand.Max);

        var eq25 =
            RoundClamp(
                100 +
                effectiveDetail * 18,
                AzomSettingCatalog.EqualizerBand.Min,
                AzomSettingCatalog.EqualizerBand.Max);

        var eq40 =
            RoundClamp(
                100 +
                effectiveDetail * 12,
                AzomSettingCatalog.EqualizerBand.Min,
                AzomSettingCatalog.EqualizerBand.Max);

        var eq60 =
            RoundClamp(
                96 +
                effectiveDetail * 8,
                AzomSettingCatalog.EqualizerBand.Min,
                AzomSettingCatalog.EqualizerBand.Max);

        var eq100 =
            RoundClamp(
                78 +
                effectiveDetail * 18,
                AzomSettingCatalog.EqualizerBand.Min,
                AzomSettingCatalog.EqualizerBand.Max);

        var eqSensitivity =
            RoundClamp(
                3 +
                effectiveDetail * 5,
                AzomSettingCatalog.EqualizerSensitivity.Min,
                AzomSettingCatalog.EqualizerSensitivity.Max);

        var acGain =
            RoundClamp(
                68 -
                torqueClass * 11 +
                effectiveDetail * 4 +
                Math.Max(
                    0,
                    grip) * 6 -
                Math.Max(
                    0,
                    targetNm - 6.5) * 1.2,
                MinimumAcGain,
                MaximumAcGain);

        var calibrationState =
            ResolveCalibration(
                input,
                calibration);

        if (calibrationState.Profile is not null)
        {
            var applied =
                calibrationState.Profile;

            baseTorque =
                AzomSettingCatalog.BaseTorqueOutput.Clamp(
                    AddSafely(
                        baseTorque,
                        applied.TorqueLimitDelta));

            wheelSpeed =
                AzomSettingCatalog.MaximumWheelSpeed.Clamp(
                    AddSafely(
                        wheelSpeed,
                        applied.WheelSpeedDelta));

            wheelDamper =
                AzomSettingCatalog.WheelDamper.Clamp(
                    AddSafely(
                        wheelDamper,
                        applied.DampingDelta));

            wheelFriction =
                AzomSettingCatalog.WheelFriction.Clamp(
                    AddSafely(
                        wheelFriction,
                        applied.FrictionDelta));

            highSpeedDamping =
                AzomSettingCatalog.HighSpeedDampingLevel.Clamp(
                    AddSafely(
                        highSpeedDamping,
                        applied.SpeedDampingDelta));

            interpolation =
                AzomSettingCatalog.Interpolation.Clamp(
                    AddSafely(
                        interpolation,
                        applied.InterpolationDelta));

            acGain =
                Math.Clamp(
                    AddSafely(
                        acGain,
                        applied.AcGainDelta),
                    MinimumAcGain,
                    MaximumAcGain);
        }

        var azom =
            new AzomSettings
            {
                Core =
                    new AzomCoreSettings
                    {
                        WheelRotationAngleDeg =
                            rotation,

                        GameFfbStrengthPct =
                            gameFfb,

                        BaseTorqueOutputPct =
                            baseTorque,

                        MaximumWheelSpeedPct =
                            wheelSpeed,

                        Interpolation =
                            interpolation
                    },

                GearshiftVibration =
                    new AzomGearshiftVibrationSettings
                    {
                        ShiftIntensity =
                            Math.Clamp(
                                preferences.ShiftIntensity,
                                0,
                                5),

                        VibrateOnNeutral =
                            preferences.VibrateOnNeutral,

                        ShiftDebounceMs =
                            Math.Clamp(
                                preferences.ShiftDebounceMs,
                                0,
                                1000)
                    },

                WheelbaseEffects =
                    new AzomWheelbaseEffectsSettings
                    {
                        WheelDamperPct =
                            wheelDamper,

                        WheelFrictionPct =
                            wheelFriction,

                        NaturalInertia =
                            naturalInertia,

                        WheelSpringPct =
                            0
                    },

                GameEffects =
                    new AzomGameEffectsSettings
                    {
                        GameDamperPct =
                            0,

                        GameFrictionPct =
                            0,

                        GameInertiaPct =
                            0,

                        GameSpringPct =
                            0
                    },

                Protection =
                    new AzomProtectionSettings
                    {
                        HandsOffProtection =
                            preferences.HandsOffProtection,

                        SteeringWheelInertia =
                            steeringWheelInertia
                    },

                SoftLimit =
                    new AzomSoftLimitSettings
                    {
                        Stiffness =
                            softLimitStiffness,

                        RetainGameFfb =
                            preferences.RetainGameFfb
                    },

                FfbEqualizer =
                    new AzomFfbEqualizerSettings
                    {
                        Hz10 =
                            eq10,

                        Hz15 =
                            eq15,

                        Hz25 =
                            eq25,

                        Hz40 =
                            eq40,

                        Hz60 =
                            eq60,

                        Hz100 =
                            eq100,

                        Sensitivity =
                            eqSensitivity
                    },

                // Linear remains the drift default because it preserves a
                // predictable input/output relationship.
                FfbOutputCurve =
                    new AzomFfbOutputCurveSettings
                    {
                        Preset =
                            AzomCurvePreset.Linear,

                        Node20 =
                            20,

                        Node40 =
                            40,

                        Node60 =
                            60,

                        Node80 =
                            80,

                        Node100 =
                            100
                    },

                HighSpeedDamping =
                    new AzomHighSpeedDampingSettings
                    {
                        DampingLevelPct =
                            highSpeedDamping,

                        TriggerSpeedKph =
                            highSpeedTrigger
                    },

                Miscellaneous =
                    new AzomMiscellaneousSettings
                    {
                        ForceFeedbackReversal =
                            preferences.ForceFeedbackReversal,

                        StandbyMode =
                            preferences.StandbyMode,

                        StandbyAfter =
                            string.IsNullOrWhiteSpace(
                                preferences.StandbyAfter)
                                ? "Disabled"
                                : preferences.StandbyAfter.Trim(),

                        BaseStatusLed =
                            preferences.BaseStatusLed,

                        Bluetooth =
                            preferences.Bluetooth
                    }
            };

        var estimatedPeakWheelTorqueNm =
            hardware.PeakTorqueNm *
            (
                baseTorque /
                100.0
            ) *
            (
                gameFfb /
                100.0
            ) *
            (
                acGain /
                100.0
            );

        var result =
            new TuneResult
            {
                Azom =
                    azom,

                Ac =
                    new AssettoCorsaSettings
                    {
                        GainPct =
                            acGain,

                        FilterPct =
                            0,

                        MinimumForcePct =
                            0,

                        KerbPct =
                            0,

                        RoadPct =
                            0,

                        SlipPct =
                            0,

                        AbsPct =
                            0
                    },

                EstimatedPeakWheelTorqueNm =
                    estimatedPeakWheelTorqueNm,

                SelfSteerScore =
                    RoundClamp(
                        effectiveSelfSteer * 100 -
                        wheelDamper * 0.35 +
                        wheelSpeed * 0.08,
                        0,
                        100),

                StabilityScore =
                    RoundClamp(
                        stabilityIntent * 100 +
                        wheelDamper * 0.35 +
                        highSpeedDamping * 0.25,
                        0,
                        100),

                DetailScore =
                    RoundClamp(
                        effectiveDetail * 100 -
                        interpolation * 4,
                        0,
                        100),

                CalibrationSummary =
                    calibrationState.Profile is null
                        ? calibrationState.Reason
                        : $"Applied {calibrationState.Profile.Samples} calibration sample(s)"
            };

        result.Notes.Add(
            $"Wheel profile: {wheel.Model}, {wheel.DiameterMm:0} mm, inertia factor {wheel.InertiaFactor:0.00}.");

        result.Notes.Add(
            $"Drift pack baseline: {pack.Name} ({pack.Category}). Pack biases are ADT tuner defaults, not exact pack physics data.");

        result.Notes.Add(
            $"Car-data confidence score: {car.Confidence.Score}/100.");

        result.Notes.Add(
            $"Estimated combined peak after AZOM Base Torque + Game FFB + AC Gain: {result.EstimatedPeakWheelTorqueNm:0.0} Nm.");

        result.Notes.Add(
            "The combined peak torque value is an estimate for comparison and safety context; it is not a guarantee of exact physical wheel torque.");

        result.Notes.Add(
            "AZOM Game Effects are kept at 0 in the starting tune so game-supplied spring, friction, and inertia effects do not mask steering forces.");

        result.Notes.Add(
            "FFB Output Curve defaults to Linear. Equalizer values are conservative drift-oriented starting points; telemetry calibration does not alter EQ bands yet.");

        result.Notes.Add(
            "Gearshift, protection, and miscellaneous toggles are preserved as user preferences rather than changed per car.");

        if (calibrationState.Profile is not null)
        {
            var applied =
                calibrationState.Profile;

            result.Notes.Add(
                $"Saved calibration applied: wheel speed {Signed(applied.WheelSpeedDelta)}, wheel damper {Signed(applied.DampingDelta)}, wheel friction {Signed(applied.FrictionDelta)}, base torque {Signed(applied.TorqueLimitDelta)}, AC gain {Signed(applied.AcGainDelta)}.");
        }
        else if (
            calibration is not null &&
            !string.IsNullOrWhiteSpace(
                calibrationState.Reason) &&
            calibrationState.Reason !=
            "No saved calibration")
        {
            result.Notes.Add(
                calibrationState.Reason);
        }

        if (wheel.DiameterMm >= 350)
        {
            result.Notes.Add(
                "Large wheel detected: maximum wheel-speed target reduced and inertia compensation increased.");
        }

        if (wheel.InertiaFactor >= 1.20)
        {
            result.Notes.Add(
                "High wheel inertia detected: added wheelbase-side control and inertia compensation.");
        }

        if (hardware.PeakTorqueNm <= 5.5)
        {
            result.Notes.Add(
                "Lower-torque base: AZOM Base Torque Output cannot go below 50%; check AC FFB clipping during sustained load.");
        }

        return result;
    }

    private static CalibrationResolution ResolveCalibration(
        TuneInput input,
        CalibrationProfile? calibration)
    {
        if (calibration is null)
        {
            return new CalibrationResolution(
                null,
                "No saved calibration");
        }

        if (calibration.Samples <= 0)
        {
            return new CalibrationResolution(
                null,
                "Calibration profile contains no completed samples and was not applied.");
        }

        string expectedKey;

        try
        {
            expectedKey =
                new CalibrationEngine()
                    .BuildKey(
                        input);
        }
        catch
        {
            // Input has already passed this engine's tuning validation.
            // If calibration identity cannot be established, do not risk
            // applying an unidentified calibration.
            return new CalibrationResolution(
                null,
                "Saved calibration identity could not be verified and was not applied.");
        }

        if (
            string.IsNullOrWhiteSpace(
                calibration.Key))
        {
            return new CalibrationResolution(
                null,
                "Saved calibration has no identity key and was not applied.");
        }

        if (!string.Equals(
                calibration.Key.Trim(),
                expectedKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return new CalibrationResolution(
                null,
                "Saved calibration belongs to a different hardware, wheel, drift-pack, or car combination and was not applied.");
        }

        return new CalibrationResolution(
            calibration,
            string.Empty);
    }

    private static int ResolveMaximumRotation(
        int hardwareMaximumRotation)
    {
        if (hardwareMaximumRotation <= 0)
        {
            return
                AzomSettingCatalog.WheelRotationAngle.Max;
        }

        return Math.Clamp(
            hardwareMaximumRotation,
            AzomSettingCatalog.WheelRotationAngle.Min,
            AzomSettingCatalog.WheelRotationAngle.Max);
    }

    private static void Validate(
        TuneInput input)
    {
        if (input.Hardware is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate a tune because the wheelbase profile is missing.");
        }

        if (input.Wheel is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate a tune because the steering-wheel profile is missing.");
        }

        if (input.DriftPack is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate a tune because the drift-pack profile is missing.");
        }

        if (input.Car is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate a tune because the car profile is missing.");
        }

        if (input.Intent is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate a tune because driver intent is missing.");
        }

        RequireFinite(
            input.Hardware.PeakTorqueNm,
            nameof(input.Hardware.PeakTorqueNm));

        RequireFinite(
            input.Wheel.DiameterMm,
            nameof(input.Wheel.DiameterMm));

        RequireFinite(
            input.Wheel.InertiaFactor,
            nameof(input.Wheel.InertiaFactor));

        RequireFinite(
            input.Car.MassKg,
            nameof(input.Car.MassKg));

        RequireFinite(
            input.Car.SteeringLockPerSideDeg,
            nameof(input.Car.SteeringLockPerSideDeg));

        RequireFinite(
            input.Car.CasterDeg,
            nameof(input.Car.CasterDeg));

        RequireFinite(
            input.Car.FrontTireWidthMm,
            nameof(input.Car.FrontTireWidthMm));

        RequireFinite(
            input.Intent.SelfSteer,
            nameof(input.Intent.SelfSteer));

        RequireFinite(
            input.Intent.Detail,
            nameof(input.Intent.Detail));

        RequireFinite(
            input.Intent.Weight,
            nameof(input.Intent.Weight));

        RequireFinite(
            input.Intent.Stability,
            nameof(input.Intent.Stability));

        RequireFinite(
            input.DriftPack.GripBias,
            nameof(input.DriftPack.GripBias));

        RequireFinite(
            input.DriftPack.SelfSteerBias,
            nameof(input.DriftPack.SelfSteerBias));

        RequireFinite(
            input.DriftPack.DetailBias,
            nameof(input.DriftPack.DetailBias));

        RequireFinite(
            input.DriftPack.DampingBias,
            nameof(input.DriftPack.DampingBias));

        if (
            input.Hardware.PeakTorqueNm <= 0 ||
            input.Hardware.PeakTorqueNm > 40)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Hardware.PeakTorqueNm),
                "Peak torque must be greater than 0 and no more than 40 Nm.");
        }

        if (
            input.Wheel.DiameterMm < 200 ||
            input.Wheel.DiameterMm > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Wheel.DiameterMm),
                "Wheel diameter must be 200–500 mm.");
        }

        if (
            input.Wheel.InertiaFactor < 0.40 ||
            input.Wheel.InertiaFactor > 2.00)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Wheel.InertiaFactor),
                "Wheel inertia factor must be 0.40–2.00.");
        }

        if (
            input.Car.MassKg < 500 ||
            input.Car.MassKg > 3000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Car.MassKg),
                "Car mass must be 500–3000 kg.");
        }

        if (
            input.Car.SteeringLockPerSideDeg < 20 ||
            input.Car.SteeringLockPerSideDeg > 80)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.Car.SteeringLockPerSideDeg),
                "Steering lock must be 20–80° per side.");
        }
    }

    private static void RequireFinite(
        double value,
        string name)
    {
        if (!double.IsFinite(
                value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                "ADT tuning inputs must contain finite numeric values.");
        }
    }

    private static double Clamp01(
        double value)
    {
        return Math.Clamp(
            value,
            0,
            1);
    }

    private static double FiniteOrZero(
        double value)
    {
        return double.IsFinite(value)
            ? value
            : 0;
    }

    private static int RoundClamp(
        double value,
        int minimum,
        int maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "ADT received an invalid tuning-output range.");
        }

        if (!double.IsFinite(
                value))
        {
            throw new InvalidDataException(
                "ADT generated a non-finite tuning value.");
        }

        var bounded =
            Math.Clamp(
                value,
                minimum,
                maximum);

        return (int)Math.Round(
            bounded,
            MidpointRounding.AwayFromZero);
    }

    private static int AddSafely(
        int current,
        int delta)
    {
        var value =
            (long)current +
            delta;

        if (value > int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value < int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }

    private static string Signed(
        int value)
    {
        return value >= 0
            ? $"+{value}"
            : value.ToString();
    }

    private sealed record CalibrationResolution(
        CalibrationProfile? Profile,
        string Reason);
}
