using AtomicDriftTuner.Models;
using AtomicDriftTuner.Data;

namespace AtomicDriftTuner.Engine;

public sealed class TuningEngine
{
    public TuneResult Generate(TuneInput input, CalibrationProfile? calibration = null, AzomUserPreferences? preferences = null)
    {
        Validate(input);
        preferences ??= new AzomUserPreferences();

        var h = input.Hardware;
        var w = input.Wheel;
        var p = input.DriftPack;
        var c = input.Car;
        var d = input.Intent;

        double grip = c.Grip switch
        {
            GripLevel.Low => -0.12,
            GripLevel.High => 0.16,
            _ => 0.0
        };
        grip = Math.Clamp(grip + p.GripBias, -0.20, 0.30);

        double torqueClass = Clamp01((h.PeakTorqueNm - 3.0) / 22.0);
        double rimDiameter = Clamp01((w.DiameterMm - 270.0) / 130.0);
        double rimInertia = Clamp01((w.InertiaFactor - 0.70) / 0.80);
        double caster = Clamp01((c.CasterDeg - 5.0) / 6.0);
        double tire = Clamp01((c.FrontTireWidthMm - 205.0) / 100.0);
        double mass = Clamp01((c.MassKg - 950.0) / 700.0);

        double effectiveSelfSteer = Math.Clamp(d.SelfSteer + p.SelfSteerBias, 0, 1);
        double effectiveDetail = Math.Clamp(d.Detail + p.DetailBias, 0, 1);

        double targetNm = 4.1
                          + effectiveDetail * 1.6
                          + d.Weight * 1.1
                          + Math.Max(0, grip) * 1.6
                          + tire * 0.45
                          + mass * 0.25
                          + rimDiameter * 0.35;
        targetNm = Math.Clamp(targetNm, 3.5, 8.8);

        // AZOM's observed Base Torque Output range is 50..100, not 0..100.
        int baseTorque = RoundClamp(targetNm / h.PeakTorqueNm * 100.0, AzomSettingCatalog.BaseTorqueOutput.Min, AzomSettingCatalog.BaseTorqueOutput.Max);
        int gameFfb = RoundClamp(93 + effectiveDetail * 7, AzomSettingCatalog.GameFfbStrength.Min, AzomSettingCatalog.GameFfbStrength.Max);

        int wheelSpeed = RoundClamp(
            92 + effectiveSelfSteer * 88
            - rimDiameter * 24
            - rimInertia * 18
            + caster * 7,
            AzomSettingCatalog.MaximumWheelSpeed.Min, AzomSettingCatalog.MaximumWheelSpeed.Max);

        int wheelDamper = RoundClamp(
            5 + d.Stability * 18
            + rimInertia * 8
            + rimDiameter * 5
            + caster * 3
            + grip * 11
            + p.DampingBias * 30
            - effectiveSelfSteer * 11,
            AzomSettingCatalog.WheelDamper.Min, AzomSettingCatalog.WheelDamper.Max);

        int wheelFriction = RoundClamp(
            2 + d.Stability * 8
            + rimInertia * 4
            + d.Weight * 3
            - effectiveSelfSteer * 5,
            AzomSettingCatalog.WheelFriction.Min, AzomSettingCatalog.WheelFriction.Max);

        int highSpeedDamping = RoundClamp(
            3 + d.Stability * 13
            + torqueClass * 5
            + p.DampingBias * 15
            - effectiveSelfSteer * 7,
            AzomSettingCatalog.HighSpeedDampingLevel.Min, AzomSettingCatalog.HighSpeedDampingLevel.Max);

        int highSpeedTrigger = RoundClamp(
            90 + d.Stability * 55 + Math.Max(0, grip) * 45 + torqueClass * 20,
            AzomSettingCatalog.HighSpeedTriggerSpeed.Min, AzomSettingCatalog.HighSpeedTriggerSpeed.Max);

        int interpolation = RoundClamp(4 - effectiveDetail * 3 + torqueClass * 0.5, AzomSettingCatalog.Interpolation.Min, AzomSettingCatalog.Interpolation.Max);

        int rotation = RoundClamp(
            c.SteeringLockPerSideDeg * 15.0,
            AzomSettingCatalog.WheelRotationAngle.Min,
            Math.Min(AzomSettingCatalog.WheelRotationAngle.Max, h.MaxRotationDeg));

        int naturalInertia = RoundClamp(
            100 + rimInertia * 150 + rimDiameter * 45 + d.Weight * 20,
            AzomSettingCatalog.NaturalInertia.Min, AzomSettingCatalog.NaturalInertia.Max);

        int steeringWheelInertia = RoundClamp(
            350 + rimInertia * 1250 + rimDiameter * 650 + d.Weight * 250,
            AzomSettingCatalog.SteeringWheelInertia.Min, AzomSettingCatalog.SteeringWheelInertia.Max);

        int softLimitStiffness = RoundClamp(3.5 + torqueClass * 2.0 + d.Stability * 1.5, AzomSettingCatalog.SoftLimitStiffness.Min, AzomSettingCatalog.SoftLimitStiffness.Max);

        int eq10 = RoundClamp(100 + d.Weight * 14 + d.Stability * 4, AzomSettingCatalog.EqualizerBand.Min, AzomSettingCatalog.EqualizerBand.Max);
        int eq15 = RoundClamp(100 + d.Weight * 18 + effectiveDetail * 3, AzomSettingCatalog.EqualizerBand.Min, AzomSettingCatalog.EqualizerBand.Max);
        int eq25 = RoundClamp(100 + effectiveDetail * 18, AzomSettingCatalog.EqualizerBand.Min, AzomSettingCatalog.EqualizerBand.Max);
        int eq40 = RoundClamp(100 + effectiveDetail * 12, AzomSettingCatalog.EqualizerBand.Min, AzomSettingCatalog.EqualizerBand.Max);
        int eq60 = RoundClamp(96 + effectiveDetail * 8, AzomSettingCatalog.EqualizerBand.Min, AzomSettingCatalog.EqualizerBand.Max);
        int eq100 = RoundClamp(78 + effectiveDetail * 18, AzomSettingCatalog.EqualizerBand.Min, AzomSettingCatalog.EqualizerBand.Max);
        int eqSensitivity = RoundClamp(3 + effectiveDetail * 5, AzomSettingCatalog.EqualizerSensitivity.Min, AzomSettingCatalog.EqualizerSensitivity.Max);

        int acGain = RoundClamp(
            68 - torqueClass * 11
            + effectiveDetail * 4
            + Math.Max(0, grip) * 6
            - Math.Max(0, targetNm - 6.5) * 1.2,
            35, 90);

        if (calibration is not null)
        {
            baseTorque = AzomSettingCatalog.BaseTorqueOutput.Clamp(baseTorque + calibration.TorqueLimitDelta);
            wheelSpeed = AzomSettingCatalog.MaximumWheelSpeed.Clamp(wheelSpeed + calibration.WheelSpeedDelta);
            wheelDamper = AzomSettingCatalog.WheelDamper.Clamp(wheelDamper + calibration.DampingDelta);
            wheelFriction = AzomSettingCatalog.WheelFriction.Clamp(wheelFriction + calibration.FrictionDelta);
            highSpeedDamping = AzomSettingCatalog.HighSpeedDampingLevel.Clamp(highSpeedDamping + calibration.SpeedDampingDelta);
            interpolation = AzomSettingCatalog.Interpolation.Clamp(interpolation + calibration.InterpolationDelta);
            acGain = Math.Clamp(acGain + calibration.AcGainDelta, 35, 90);
        }

        var azom = new AzomSettings
        {
            Core = new AzomCoreSettings
            {
                WheelRotationAngleDeg = rotation,
                GameFfbStrengthPct = gameFfb,
                BaseTorqueOutputPct = baseTorque,
                MaximumWheelSpeedPct = wheelSpeed,
                Interpolation = interpolation
            },
            GearshiftVibration = new AzomGearshiftVibrationSettings
            {
                ShiftIntensity = Math.Clamp(preferences.ShiftIntensity, 0, 5),
                VibrateOnNeutral = preferences.VibrateOnNeutral,
                ShiftDebounceMs = Math.Clamp(preferences.ShiftDebounceMs, 0, 1000)
            },
            WheelbaseEffects = new AzomWheelbaseEffectsSettings
            {
                WheelDamperPct = wheelDamper,
                WheelFrictionPct = wheelFriction,
                NaturalInertia = naturalInertia,
                WheelSpringPct = 0
            },
            GameEffects = new AzomGameEffectsSettings
            {
                GameDamperPct = 0,
                GameFrictionPct = 0,
                GameInertiaPct = 0,
                GameSpringPct = 0
            },
            Protection = new AzomProtectionSettings
            {
                HandsOffProtection = preferences.HandsOffProtection,
                SteeringWheelInertia = steeringWheelInertia
            },
            SoftLimit = new AzomSoftLimitSettings
            {
                Stiffness = softLimitStiffness,
                RetainGameFfb = preferences.RetainGameFfb
            },
            FfbEqualizer = new AzomFfbEqualizerSettings
            {
                Hz10 = eq10,
                Hz15 = eq15,
                Hz25 = eq25,
                Hz40 = eq40,
                Hz60 = eq60,
                Hz100 = eq100,
                Sensitivity = eqSensitivity
            },
            // Linear is intentionally the drift default: predictable input/output relationship.
            FfbOutputCurve = new AzomFfbOutputCurveSettings
            {
                Preset = AzomCurvePreset.Linear,
                Node20 = 20,
                Node40 = 40,
                Node60 = 60,
                Node80 = 80,
                Node100 = 100
            },
            HighSpeedDamping = new AzomHighSpeedDampingSettings
            {
                DampingLevelPct = highSpeedDamping,
                TriggerSpeedKph = highSpeedTrigger
            },
            Miscellaneous = new AzomMiscellaneousSettings
            {
                ForceFeedbackReversal = preferences.ForceFeedbackReversal,
                StandbyMode = preferences.StandbyMode,
                StandbyAfter = string.IsNullOrWhiteSpace(preferences.StandbyAfter) ? "Disabled" : preferences.StandbyAfter,
                BaseStatusLed = preferences.BaseStatusLed,
                Bluetooth = preferences.Bluetooth
            }
        };

        var result = new TuneResult
        {
            Azom = azom,
            Ac = new AssettoCorsaSettings
            {
                GainPct = acGain,
                FilterPct = 0,
                MinimumForcePct = 0,
                KerbPct = 0,
                RoadPct = 0,
                SlipPct = 0,
                AbsPct = 0
            },
            EstimatedPeakWheelTorqueNm = h.PeakTorqueNm * (baseTorque / 100.0) * (gameFfb / 100.0) * (acGain / 100.0),
            SelfSteerScore = RoundClamp(effectiveSelfSteer * 100 - wheelDamper * 0.35 + wheelSpeed * 0.08, 0, 100),
            StabilityScore = RoundClamp(d.Stability * 100 + wheelDamper * 0.35 + highSpeedDamping * 0.25, 0, 100),
            DetailScore = RoundClamp(effectiveDetail * 100 - interpolation * 4, 0, 100),
            CalibrationSummary = calibration is null || calibration.Samples == 0
                ? "No saved calibration"
                : $"Applied {calibration.Samples} calibration sample(s)"
        };

        result.Notes.Add($"Wheel profile: {w.Model}, {w.DiameterMm:0} mm, inertia factor {w.InertiaFactor:0.00}.");
        result.Notes.Add($"Drift pack baseline: {p.Name} ({p.Category}). Pack biases are tuner defaults, not exact pack physics data.");
        result.Notes.Add($"Car-data confidence score: {c.Confidence.Score}/100.");
        result.Notes.Add($"Estimated combined peak after AZOM Base Torque + Game FFB + AC Gain: {result.EstimatedPeakWheelTorqueNm:0.0} Nm.");
        result.Notes.Add("AZOM Game Effects are kept at 0 in the starting tune so game-supplied spring/friction/inertia effects do not mask steering forces.");
        result.Notes.Add("FFB Output Curve defaults to Linear. Equalizer values are conservative drift-oriented starting points; telemetry calibration does not alter EQ bands yet.");
        result.Notes.Add("Gearshift, protection and miscellaneous toggles are preserved as user preferences rather than changed per car.");

        if (calibration is not null && calibration.Samples > 0)
            result.Notes.Add($"Saved calibration applied: wheel speed {Signed(calibration.WheelSpeedDelta)}, wheel damper {Signed(calibration.DampingDelta)}, wheel friction {Signed(calibration.FrictionDelta)}, base torque {Signed(calibration.TorqueLimitDelta)}, AC gain {Signed(calibration.AcGainDelta)}.");
        if (w.DiameterMm >= 350)
            result.Notes.Add("Large wheel detected: maximum wheel-speed target reduced and inertia compensation increased.");
        if (w.InertiaFactor >= 1.20)
            result.Notes.Add("High wheel inertia detected: added wheelbase-side control and inertia compensation.");
        if (h.PeakTorqueNm <= 5.5)
            result.Notes.Add("Lower-torque base: AZOM Base Torque Output cannot go below 50%; check AC FFB clipping during sustained load.");

        return result;
    }

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private static void Validate(TuneInput x)
    {
        if (x.Hardware.PeakTorqueNm <= 0 || x.Hardware.PeakTorqueNm > 40)
            throw new ArgumentOutOfRangeException(nameof(x.Hardware.PeakTorqueNm), "Peak torque must be 0–40 Nm.");
        if (x.Wheel.DiameterMm < 200 || x.Wheel.DiameterMm > 500)
            throw new ArgumentOutOfRangeException(nameof(x.Wheel.DiameterMm), "Wheel diameter must be 200–500 mm.");
        if (x.Wheel.InertiaFactor < 0.40 || x.Wheel.InertiaFactor > 2.00)
            throw new ArgumentOutOfRangeException(nameof(x.Wheel.InertiaFactor), "Wheel inertia factor must be 0.40–2.00.");
        if (x.Car.MassKg < 500 || x.Car.MassKg > 3000)
            throw new ArgumentOutOfRangeException(nameof(x.Car.MassKg), "Car mass must be 500–3000 kg.");
        if (x.Car.SteeringLockPerSideDeg < 20 || x.Car.SteeringLockPerSideDeg > 80)
            throw new ArgumentOutOfRangeException(nameof(x.Car.SteeringLockPerSideDeg), "Steering lock must be 20–80° per side.");
    }

    private static double Clamp01(double x) => Math.Clamp(x, 0, 1);
    private static int RoundClamp(double x, int min, int max) => (int)Math.Round(Math.Clamp(x, min, max));
}
