using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AzomLiveController
{
    private const int DefaultActionDelayMs = 70;
    private const int MinimumActionDelayMs = 20;
    private const int MaximumActionDelayMs = 500;

    private const int ReadbackDelayMs = 350;

    // One live AZOM write operation at a time across the ADT process.
    //
    // Explicit Apply/Revert batches and debounced interactive writes share
    // this gate so they cannot issue overlapping wheelbase operations.
    private static readonly SemaphoreSlim LiveWriteGate =
        new(1, 1);

    // Incremented whenever an explicit Apply/Revert batch acquires the write
    // gate. Interactive requests capture this generation when queued so an
    // older pending slider/edit request cannot run after a newer explicit
    // Apply/Revert operation and overwrite its verified result.
    private static long _explicitBatchGeneration;

    private readonly AzomBridgeClient _bridge;
    private readonly SimHubActionInvoker? _cliFallback;
    private readonly int _actionDelayMs;
    private readonly AzomRevertStore _revertStore = new();

    public AzomLiveController(
        AzomBridgeClient bridge,
        int actionDelayMs = DefaultActionDelayMs,
        SimHubActionInvoker? cliFallback = null)
    {
        _bridge =
            bridge ??
            throw new ArgumentNullException(
                nameof(bridge));

        _actionDelayMs =
            Math.Clamp(
                actionDelayMs,
                MinimumActionDelayMs,
                MaximumActionDelayMs);

        _cliFallback =
            cliFallback;
    }

    internal static long CaptureExplicitBatchGeneration()
    {
        return Volatile.Read(
            ref _explicitBatchGeneration);
    }

    internal static async Task<IDisposable> AcquireLiveWriteGateAsync(
        CancellationToken cancellationToken)
    {
        await LiveWriteGate.WaitAsync(
            cancellationToken);

        return new LiveWriteGateLease();
    }

    public Task<AzomLiveSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        return _bridge.ReadSnapshotAsync(
            cancellationToken:
                cancellationToken);
    }

    public List<AzomApplyPlanItem> BuildPlan(
        AzomSettings target,
        AzomLiveSnapshot current,
        bool includePreferences)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(current);

        var rows =
            new List<AzomApplyPlanItem>();

        if (!IsSupportedAzomNamespace(current))
        {
            rows.Add(
                new AzomApplyPlanItem
                {
                    Group =
                        "Compatibility",

                    DisplayName =
                        "Live write support",

                    PropertyName =
                        "AZOM",

                    Kind =
                        AzomApplyItemKind.Unsupported,

                    CanApply =
                        false,

                    CurrentDisplay =
                        string.IsNullOrWhiteSpace(
                            current.PropertyNamespace)
                            ? "No namespace"
                            : current.PropertyNamespace + ".*",

                    TargetDisplay =
                        "AZOM.*",

                    Note =
                        "Live writes are disabled for legacy property namespaces. Update AZOM before using Apply/Revert."
                });

            return rows;
        }

        AddNumeric(
            rows,
            "Core",
            "Game FFB Strength",
            "AZOM.FfbStrength",
            current.FfbStrength,
            target.Core.GameFfbStrengthPct,
            "AZOM.FfbStrength",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Core",
            "Base Torque Output",
            "AZOM.Torque",
            current.Torque,
            target.Core.BaseTorqueOutputPct,
            "AZOM.Torque",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Core",
            "Wheel Rotation Angle",
            "AZOM.Rotation",
            current.Rotation,
            target.Core.WheelRotationAngleDeg,
            "AZOM.Rotation",
            90,
            180,
            "°");

        AddNumeric(
            rows,
            "Core",
            "Maximum Wheel Speed",
            "AZOM.WheelSpeedLimit",
            current.WheelSpeedLimit,
            target.Core.MaximumWheelSpeedPct,
            "AZOM.WheelSpeedLimit",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Core",
            "Interpolation",
            "AZOM.Interpolation",
            current.Interpolation,
            target.Core.Interpolation,
            "AZOM.Interpolation",
            1,
            2,
            "");

        AddNumeric(
            rows,
            "Wheelbase",
            "Wheel Damper",
            "AZOM.Damper",
            current.Damper,
            target.WheelbaseEffects.WheelDamperPct,
            "AZOM.Damper",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Wheelbase",
            "Wheel Friction",
            "AZOM.Friction",
            current.Friction,
            target.WheelbaseEffects.WheelFrictionPct,
            "AZOM.Friction",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Wheelbase",
            "Natural Inertia",
            "AZOM.Inertia",
            current.Inertia,
            target.WheelbaseEffects.NaturalInertia,
            "AZOM.Inertia",
            10,
            50,
            "");

        AddNumeric(
            rows,
            "Wheelbase",
            "Wheel Spring",
            "AZOM.Spring",
            current.Spring,
            target.WheelbaseEffects.WheelSpringPct,
            "AZOM.Spring",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Game Effects",
            "Game Damper",
            "AZOM.GameDamper",
            current.GameDamper,
            target.GameEffects.GameDamperPct,
            "AZOM.GameDamper",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Game Effects",
            "Game Friction",
            "AZOM.GameFriction",
            current.GameFriction,
            target.GameEffects.GameFrictionPct,
            "AZOM.GameFriction",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Game Effects",
            "Game Inertia",
            "AZOM.GameInertia",
            current.GameInertia,
            target.GameEffects.GameInertiaPct,
            "AZOM.GameInertia",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Game Effects",
            "Game Spring",
            "AZOM.GameSpring",
            current.GameSpring,
            target.GameEffects.GameSpringPct,
            "AZOM.GameSpring",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "Protection",
            "Steering Wheel Inertia",
            "AZOM.NaturalInertia",
            current.NaturalInertia,
            target.Protection.SteeringWheelInertia,
            "AZOM.NaturalInertia",
            50,
            200,
            "");

        AddNumeric(
            rows,
            "Soft Limit",
            "Soft Limit Stiffness",
            "AZOM.SoftLimitStiffness",
            current.SoftLimitStiffness,
            target.SoftLimit.Stiffness,
            "AZOM.SoftLimitStiffness",
            1,
            2,
            "");

        AddNumeric(
            rows,
            "High Speed Damping",
            "Damping Level",
            "AZOM.SpeedDamping",
            current.SpeedDamping,
            target.HighSpeedDamping.DampingLevelPct,
            "AZOM.SpeedDamping",
            5,
            10,
            "%");

        AddNumeric(
            rows,
            "High Speed Damping",
            "Trigger Speed",
            "AZOM.SpeedDampingPoint",
            current.SpeedDampingPoint,
            target.HighSpeedDamping.TriggerSpeedKph,
            "AZOM.SpeedDampingPoint",
            10,
            50,
            " kph");

        // RoadSensitivity changes AZOM's canned EQ curve.
        // Apply it before custom EQ bands so ADT's final EQ target wins.
        AddNumeric(
            rows,
            "FFB Equalizer",
            "Sensitivity",
            "AZOM.RoadSensitivity",
            current.RoadSensitivity,
            target.FfbEqualizer.Sensitivity,
            "AZOM.RoadSensitivity",
            1,
            2,
            "");

        if (current.HasLegacySixBandEqualizer)
        {
            AddNumeric(
                rows,
                "FFB Equalizer",
                "10 Hz",
                "AZOM.Equalizer1",
                current.Equalizer1,
                target.FfbEqualizer.Hz10,
                "AZOM.Equalizer1",
                5,
                25,
                "%");

            AddNumeric(
                rows,
                "FFB Equalizer",
                "15 Hz",
                "AZOM.Equalizer2",
                current.Equalizer2,
                target.FfbEqualizer.Hz15,
                "AZOM.Equalizer2",
                5,
                25,
                "%");

            AddNumeric(
                rows,
                "FFB Equalizer",
                "25 Hz",
                "AZOM.Equalizer3",
                current.Equalizer3,
                target.FfbEqualizer.Hz25,
                "AZOM.Equalizer3",
                5,
                25,
                "%");

            AddNumeric(
                rows,
                "FFB Equalizer",
                "40 Hz",
                "AZOM.Equalizer4",
                current.Equalizer4,
                target.FfbEqualizer.Hz40,
                "AZOM.Equalizer4",
                5,
                25,
                "%");

            AddNumeric(
                rows,
                "FFB Equalizer",
                "60 Hz",
                "AZOM.Equalizer5",
                current.Equalizer5,
                target.FfbEqualizer.Hz60,
                "AZOM.Equalizer5",
                5,
                25,
                "%");

            AddNumeric(
                rows,
                "FFB Equalizer",
                "100 Hz",
                "AZOM.Equalizer6",
                current.Equalizer6,
                target.FfbEqualizer.Hz100,
                "AZOM.Equalizer6",
                5,
                25,
                "%");
        }
        else
        {
            rows.Add(
                new AzomApplyPlanItem
                {
                    Group =
                        "FFB Equalizer",

                    DisplayName =
                        "Custom six-band EQ",

                    PropertyName =
                        "AZOM.Equalizer1..10",

                    Kind =
                        AzomApplyItemKind.Unsupported,

                    CanApply =
                        false,

                    CurrentDisplay =
                        "10-band firmware",

                    TargetDisplay =
                        "ADT 6-band curve",

                    Note =
                        "Automatic EQ writes are skipped on 10-band firmware until ADT has a frequency-safe 10-band target model."
                });
        }

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve X1",
            "AZOM.FfbCurveX1",
            current.FfbCurveX1,
            20,
            "AZOM.FfbCurveX1",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve X2",
            "AZOM.FfbCurveX2",
            current.FfbCurveX2,
            40,
            "AZOM.FfbCurveX2",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve X3",
            "AZOM.FfbCurveX3",
            current.FfbCurveX3,
            60,
            "AZOM.FfbCurveX3",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve X4",
            "AZOM.FfbCurveX4",
            current.FfbCurveX4,
            80,
            "AZOM.FfbCurveX4",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve Y1",
            "AZOM.FfbCurveY1",
            current.FfbCurveY1,
            target.FfbOutputCurve.Node20,
            "AZOM.FfbCurveY1",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve Y2",
            "AZOM.FfbCurveY2",
            current.FfbCurveY2,
            target.FfbOutputCurve.Node40,
            "AZOM.FfbCurveY2",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve Y3",
            "AZOM.FfbCurveY3",
            current.FfbCurveY3,
            target.FfbOutputCurve.Node60,
            "AZOM.FfbCurveY3",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve Y4",
            "AZOM.FfbCurveY4",
            current.FfbCurveY4,
            target.FfbOutputCurve.Node80,
            "AZOM.FfbCurveY4",
            5,
            10,
            "");

        AddNumeric(
            rows,
            "FFB Curve",
            "Curve Y5",
            "AZOM.FfbCurveY5",
            current.FfbCurveY5,
            target.FfbOutputCurve.Node100,
            "AZOM.FfbCurveY5",
            5,
            10,
            "");

        if (includePreferences)
        {
            AddNumeric(
                rows,
                "Preferences",
                "Gearshift Vibration",
                "AZOM.GearshiftVibration",
                current.GearshiftVibration,
                target.GearshiftVibration.ShiftIntensity,
                "AZOM.GearshiftVibration",
                1,
                2,
                "");

            AddToggle(
                rows,
                "Preferences",
                "Hands-Off Protection",
                "AZOM.Protection",
                current.Protection,
                target.Protection.HandsOffProtection,
                target.Protection.HandsOffProtection
                    ? "AZOM.ProtectionOn"
                    : "AZOM.ProtectionOff");

            AddToggle(
                rows,
                "Preferences",
                "Retain Game FFB",
                "AZOM.SoftLimitRetain",
                current.SoftLimitRetain,
                target.SoftLimit.RetainGameFfb,
                target.SoftLimit.RetainGameFfb
                    ? "AZOM.SoftLimitRetainOn"
                    : "AZOM.SoftLimitRetainOff");

            AddToggle(
                rows,
                "Preferences",
                "FFB Reversal",
                "AZOM.FfbReverse",
                current.FfbReverse,
                target.Miscellaneous.ForceFeedbackReversal,
                target.Miscellaneous.ForceFeedbackReversal
                    ? "AZOM.FfbReverseOn"
                    : "AZOM.FfbReverseOff");

            AddToggle(
                rows,
                "Preferences",
                "Base Status LED",
                "AZOM.BaseStatusLed",
                current.BaseStatusLed,
                target.Miscellaneous.BaseStatusLed,
                target.Miscellaneous.BaseStatusLed
                    ? "AZOM.BaseStatusLedOn"
                    : "AZOM.BaseStatusLedOff");

            AddToggle(
                rows,
                "Preferences",
                "Bluetooth",
                "AZOM.Bluetooth",
                current.Bluetooth,
                target.Miscellaneous.Bluetooth,
                target.Miscellaneous.Bluetooth
                    ? "AZOM.BluetoothOn"
                    : "AZOM.BluetoothOff");

            var currentStandby =
                current.WorkMode.HasValue
                    ? current.WorkMode.Value == 1
                    : (bool?)null;

            AddToggle(
                rows,
                "Preferences",
                "Standby Mode",
                "AZOM.WorkMode",
                currentStandby,
                target.Miscellaneous.StandbyMode,
                target.Miscellaneous.StandbyMode
                    ? "AZOM.WorkModeOff"
                    : "AZOM.WorkModeOn");

            rows.Add(
                new AzomApplyPlanItem
                {
                    Group =
                        "Preferences",

                    DisplayName =
                        "Vibrate on Neutral",

                    Kind =
                        AzomApplyItemKind.Unsupported,

                    CanApply =
                        false,

                    CurrentDisplay =
                        "not exposed",

                    TargetDisplay =
                        target.GearshiftVibration.VibrateOnNeutral
                            ? "ON"
                            : "OFF",

                    Note =
                        "Current AZOM public SimHub property/action list does not expose this host-side option."
                });

            rows.Add(
                new AzomApplyPlanItem
                {
                    Group =
                        "Preferences",

                    DisplayName =
                        "Shift Debounce",

                    Kind =
                        AzomApplyItemKind.Unsupported,

                    CanApply =
                        false,

                    CurrentDisplay =
                        "not exposed",

                    TargetDisplay =
                        $"{target.GearshiftVibration.ShiftDebounceMs} ms",

                    Note =
                        "Current AZOM public SimHub property/action list does not expose this host-side option."
                });

            rows.Add(
                new AzomApplyPlanItem
                {
                    Group =
                        "Preferences",

                    DisplayName =
                        "Standby After",

                    Kind =
                        AzomApplyItemKind.Unsupported,

                    CanApply =
                        false,

                    CurrentDisplay =
                        "not exposed",

                    TargetDisplay =
                        target.Miscellaneous.StandbyAfter,

                    Note =
                        "No public AZOM property/action is currently documented for the standby timer dropdown."
                });
        }

        return rows;
    }

    public List<AzomApplyPlanItem> BuildRevertPlan(
        AzomLiveSnapshot desired,
        AzomLiveSnapshot current,
        IReadOnlyCollection<string> changedProperties)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changedProperties);

        var rows =
            new List<AzomApplyPlanItem>();

        var wanted =
            new HashSet<string>(
                changedProperties.Where(
                    x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

        void Numeric(
            string display,
            string property,
            int? currentValue,
            int? targetValue,
            string action,
            int fine,
            int coarse,
            string suffix = "")
        {
            if (!wanted.Contains(property))
            {
                return;
            }

            AddNumeric(
                rows,
                "Revert",
                display,
                property,
                currentValue,
                targetValue,
                action,
                fine,
                coarse,
                suffix);
        }

        void Toggle(
            string display,
            string property,
            bool? currentValue,
            bool? targetValue,
            string onAction,
            string offAction)
        {
            if (
                !wanted.Contains(property) ||
                !targetValue.HasValue)
            {
                return;
            }

            AddToggle(
                rows,
                "Revert",
                display,
                property,
                currentValue,
                targetValue,
                targetValue.Value
                    ? onAction
                    : offAction);
        }

        Numeric(
            "Game FFB Strength",
            "AZOM.FfbStrength",
            current.FfbStrength,
            desired.FfbStrength,
            "AZOM.FfbStrength",
            5,
            10,
            "%");

        Numeric(
            "Base Torque Output",
            "AZOM.Torque",
            current.Torque,
            desired.Torque,
            "AZOM.Torque",
            5,
            10,
            "%");

        Numeric(
            "Wheel Rotation Angle",
            "AZOM.Rotation",
            current.Rotation,
            desired.Rotation,
            "AZOM.Rotation",
            90,
            180,
            "°");

        Numeric(
            "Maximum Wheel Speed",
            "AZOM.WheelSpeedLimit",
            current.WheelSpeedLimit,
            desired.WheelSpeedLimit,
            "AZOM.WheelSpeedLimit",
            5,
            10,
            "%");

        Numeric(
            "Interpolation",
            "AZOM.Interpolation",
            current.Interpolation,
            desired.Interpolation,
            "AZOM.Interpolation",
            1,
            2);

        Numeric(
            "Gearshift Vibration",
            "AZOM.GearshiftVibration",
            current.GearshiftVibration,
            desired.GearshiftVibration,
            "AZOM.GearshiftVibration",
            1,
            2);

        Numeric(
            "Wheel Damper",
            "AZOM.Damper",
            current.Damper,
            desired.Damper,
            "AZOM.Damper",
            5,
            10,
            "%");

        Numeric(
            "Wheel Friction",
            "AZOM.Friction",
            current.Friction,
            desired.Friction,
            "AZOM.Friction",
            5,
            10,
            "%");

        Numeric(
            "Natural Inertia",
            "AZOM.Inertia",
            current.Inertia,
            desired.Inertia,
            "AZOM.Inertia",
            10,
            50);

        Numeric(
            "Wheel Spring",
            "AZOM.Spring",
            current.Spring,
            desired.Spring,
            "AZOM.Spring",
            5,
            10,
            "%");

        Numeric(
            "Game Damper",
            "AZOM.GameDamper",
            current.GameDamper,
            desired.GameDamper,
            "AZOM.GameDamper",
            5,
            10,
            "%");

        Numeric(
            "Game Friction",
            "AZOM.GameFriction",
            current.GameFriction,
            desired.GameFriction,
            "AZOM.GameFriction",
            5,
            10,
            "%");

        Numeric(
            "Game Inertia",
            "AZOM.GameInertia",
            current.GameInertia,
            desired.GameInertia,
            "AZOM.GameInertia",
            5,
            10,
            "%");

        Numeric(
            "Game Spring",
            "AZOM.GameSpring",
            current.GameSpring,
            desired.GameSpring,
            "AZOM.GameSpring",
            5,
            10,
            "%");

        Numeric(
            "Steering Wheel Inertia",
            "AZOM.NaturalInertia",
            current.NaturalInertia,
            desired.NaturalInertia,
            "AZOM.NaturalInertia",
            50,
            200);

        Numeric(
            "Soft Limit Stiffness",
            "AZOM.SoftLimitStiffness",
            current.SoftLimitStiffness,
            desired.SoftLimitStiffness,
            "AZOM.SoftLimitStiffness",
            1,
            2);

        Numeric(
            "High Speed Damping",
            "AZOM.SpeedDamping",
            current.SpeedDamping,
            desired.SpeedDamping,
            "AZOM.SpeedDamping",
            5,
            10,
            "%");

        Numeric(
            "High Speed Trigger",
            "AZOM.SpeedDampingPoint",
            current.SpeedDampingPoint,
            desired.SpeedDampingPoint,
            "AZOM.SpeedDampingPoint",
            10,
            50,
            " kph");

        Numeric(
            "Sensitivity",
            "AZOM.RoadSensitivity",
            current.RoadSensitivity,
            desired.RoadSensitivity,
            "AZOM.RoadSensitivity",
            1,
            2);

        for (var i = 1; i <= 10; i++)
        {
            Numeric(
                $"Equalizer {i}",
                $"AZOM.Equalizer{i}",
                GetEq(current, i),
                GetEq(desired, i),
                $"AZOM.Equalizer{i}",
                5,
                25,
                "%");
        }

        Numeric(
            "Curve X1",
            "AZOM.FfbCurveX1",
            current.FfbCurveX1,
            desired.FfbCurveX1,
            "AZOM.FfbCurveX1",
            5,
            10);

        Numeric(
            "Curve X2",
            "AZOM.FfbCurveX2",
            current.FfbCurveX2,
            desired.FfbCurveX2,
            "AZOM.FfbCurveX2",
            5,
            10);

        Numeric(
            "Curve X3",
            "AZOM.FfbCurveX3",
            current.FfbCurveX3,
            desired.FfbCurveX3,
            "AZOM.FfbCurveX3",
            5,
            10);

        Numeric(
            "Curve X4",
            "AZOM.FfbCurveX4",
            current.FfbCurveX4,
            desired.FfbCurveX4,
            "AZOM.FfbCurveX4",
            5,
            10);

        Numeric(
            "Curve Y1",
            "AZOM.FfbCurveY1",
            current.FfbCurveY1,
            desired.FfbCurveY1,
            "AZOM.FfbCurveY1",
            5,
            10);

        Numeric(
            "Curve Y2",
            "AZOM.FfbCurveY2",
            current.FfbCurveY2,
            desired.FfbCurveY2,
            "AZOM.FfbCurveY2",
            5,
            10);

        Numeric(
            "Curve Y3",
            "AZOM.FfbCurveY3",
            current.FfbCurveY3,
            desired.FfbCurveY3,
            "AZOM.FfbCurveY3",
            5,
            10);

        Numeric(
            "Curve Y4",
            "AZOM.FfbCurveY4",
            current.FfbCurveY4,
            desired.FfbCurveY4,
            "AZOM.FfbCurveY4",
            5,
            10);

        Numeric(
            "Curve Y5",
            "AZOM.FfbCurveY5",
            current.FfbCurveY5,
            desired.FfbCurveY5,
            "AZOM.FfbCurveY5",
            5,
            10);

        Toggle(
            "Hands-Off Protection",
            "AZOM.Protection",
            current.Protection,
            desired.Protection,
            "AZOM.ProtectionOn",
            "AZOM.ProtectionOff");

        Toggle(
            "Retain Game FFB",
            "AZOM.SoftLimitRetain",
            current.SoftLimitRetain,
            desired.SoftLimitRetain,
            "AZOM.SoftLimitRetainOn",
            "AZOM.SoftLimitRetainOff");

        Toggle(
            "FFB Reversal",
            "AZOM.FfbReverse",
            current.FfbReverse,
            desired.FfbReverse,
            "AZOM.FfbReverseOn",
            "AZOM.FfbReverseOff");

        Toggle(
            "Base Status LED",
            "AZOM.BaseStatusLed",
            current.BaseStatusLed,
            desired.BaseStatusLed,
            "AZOM.BaseStatusLedOn",
            "AZOM.BaseStatusLedOff");

        Toggle(
            "Bluetooth",
            "AZOM.Bluetooth",
            current.Bluetooth,
            desired.Bluetooth,
            "AZOM.BluetoothOn",
            "AZOM.BluetoothOff");

        if (
            wanted.Contains("AZOM.WorkMode") &&
            desired.WorkMode.HasValue)
        {
            var currentStandby =
                current.WorkMode.HasValue
                    ? current.WorkMode.Value == 1
                    : (bool?)null;

            var targetStandby =
                desired.WorkMode.Value == 1;

            AddToggle(
                rows,
                "Revert",
                "Standby Mode",
                "AZOM.WorkMode",
                currentStandby,
                targetStandby,
                targetStandby
                    ? "AZOM.WorkModeOff"
                    : "AZOM.WorkModeOn");
        }

        return rows;
    }

    public async Task<AzomApplyResult> ApplyAsync(
        List<AzomApplyPlanItem> plan,
        AzomLiveSnapshot before,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(before);

        var writeGate =
            await AcquireLiveWriteGateAsync(
                cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An explicit Apply/Revert batch wins over any interactive request
            // that was queued before this batch acquired the shared write gate.
            Interlocked.Increment(
                ref _explicitBatchGeneration);

            // The snapshot supplied by the caller may have been captured before
            // this batch waited behind another Apply/Revert operation.
            //
            // Read again only after acquiring the batch gate so rollback always
            // represents the actual live state immediately before this batch.
            var authoritativeBefore =
                await ReadAsync(
                    cancellationToken);

            if (!IsSupportedAzomNamespace(authoritativeBefore))
            {
                throw new InvalidOperationException(
                    "ADT cannot apply AZOM settings because the current bridge snapshot is not using the supported AZOM.* property namespace.");
            }

            if (!authoritativeBefore.SettingsReadable)
            {
                throw new InvalidOperationException(
                    "ADT cannot apply AZOM settings because current live base settings are not safely readable.");
            }

            var selected =
                plan
                    .Where(
                        x =>
                            x is not null &&
                            x.CanApply &&
                            x.IsDifferent &&
                            x.IsSelectedForApply)
                    .ToList();

            if (selected.Count == 0)
            {
                return new AzomApplyResult
                {
                    After =
                        authoritativeBefore,

                    SettingsChanged =
                        0,

                    VerifiedSettingsChanged =
                        0
                };
            }

            // Save the actual pre-batch state before the first write attempt.
            _revertStore.Save(
                authoritativeBefore,
                selected.Select(
                    x => x.PropertyName));

            var result =
                new AzomApplyResult
                {
                    SettingsChanged =
                        selected.Count
                };

            var live =
                authoritativeBefore;

            foreach (var item in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var beforeDisplay =
                    DisplayCurrent(
                        item,
                        live);

                if (IsItemAtTarget(
                        item,
                        live))
                {
                    result.VerifiedSettingsChanged++;

                    result.Audit.Add(
                        new AzomApplyAuditItem
                        {
                            Group =
                                item.Group,

                            Setting =
                                item.DisplayName,

                            Before =
                                beforeDisplay,

                            Target =
                                item.TargetDisplay,

                            After =
                                beforeDisplay,

                            Verified =
                                true,

                            Transport =
                                "Already matched"
                        });

                    continue;
                }

                var outcome =
                    await ApplyItemVerifiedAsync(
                        item,
                        live,
                        result,
                        cancellationToken);

                live =
                    outcome.Snapshot;

                result.Audit.Add(
                    new AzomApplyAuditItem
                    {
                        Group =
                            item.Group,

                        Setting =
                            item.DisplayName,

                        Before =
                            beforeDisplay,

                        Target =
                            item.TargetDisplay,

                        After =
                            DisplayCurrent(
                                item,
                                live),

                        Verified =
                            outcome.Verified,

                        Transport =
                            outcome.Transport,

                        Note =
                            outcome.Note
                    });

                if (!outcome.Verified)
                {
                    result.Warnings.Add(
                        $"{item.DisplayName} did not reach the requested value. " +
                        "ADT stopped the batch before touching any later selected settings.");

                    break;
                }

                result.VerifiedSettingsChanged++;
            }

            result.After =
                live;

            return result;
        }
        finally
        {
            writeGate.Dispose();
        }
    }

    private sealed class LiveWriteGateLease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (
                Interlocked.Exchange(
                    ref _released,
                    1) == 0)
            {
                LiveWriteGate.Release();
            }
        }
    }

    private sealed class ApplyOutcome
    {
        public bool Verified { get; init; }

        public AzomLiveSnapshot Snapshot { get; init; } =
            new();

        public string Transport { get; init; } =
            "";

        public string Note { get; init; } =
            "";
    }

    private async Task<ApplyOutcome> ApplyItemVerifiedAsync(
        AzomApplyPlanItem item,
        AzomLiveSnapshot before,
        AzomApplyResult result,
        CancellationToken cancellationToken)
    {
        var live =
            before;

        // Primary transport:
        // exact AZOM internal commit through the bridge.
        //
        // This can represent values that the public step actions cannot.
        try
        {
            var method =
                await _bridge.SetSettingDirectAsync(
                    item.PropertyName,
                    item.Kind == AzomApplyItemKind.Numeric
                        ? item.TargetInt
                        : null,
                    item.Kind == AzomApplyItemKind.Toggle
                        ? item.TargetBool
                        : null,
                    cancellationToken:
                        cancellationToken);

            // Keep this existing result counter name for model/UI
            // compatibility even though exact commit is now primary.
            result.DirectFallbackSettingsTriggered++;

            live =
                await ReadFreshAsync(
                    cancellationToken);

            if (IsItemAtTarget(
                    item,
                    live))
            {
                return new ApplyOutcome
                {
                    Verified =
                        true,

                    Snapshot =
                        live,

                    Transport =
                        "Exact AZOM commit",

                    Note =
                        method ??
                        "AZOM internal commit path"
                };
            }

            result.Warnings.Add(
                $"{item.DisplayName}: exact AZOM commit returned, but live readback is " +
                $"{DisplayCurrent(item, live)} instead of {item.TargetDisplay}.");
        }
        catch (OperationCanceledException)
        {
            // Cancellation is never interpreted as transport failure.
            // Do not continue into another write mechanism.
            throw;
        }
        catch (Exception ex)
        {
            result.Warnings.Add(
                $"{item.DisplayName}: exact AZOM commit reported an error: {ex.Message}");

            // A timeout/error does not prove the bridge failed before changing
            // the setting. Re-read before deciding whether another transport
            // may safely be attempted.
            try
            {
                live =
                    await ReadFreshAsync(
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception readEx)
            {
                result.Warnings.Add(
                    $"{item.DisplayName}: ADT could not establish the live value after the uncertain exact-write result: {readEx.Message}");

                return FailedUnknownState(
                    item,
                    before,
                    "Exact write failed and live state could not be verified");
            }

            if (IsItemAtTarget(
                    item,
                    live))
            {
                return new ApplyOutcome
                {
                    Verified =
                        true,

                    Snapshot =
                        live,

                    Transport =
                        "Exact AZOM commit",

                    Note =
                        "Bridge reported an error, but live readback verified the requested value."
                };
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Fallback 1:
        // public in-process SimHub actions.
        //
        // Public step actions are used ONLY when they can reach the requested
        // value exactly. ADT will never deliberately send an approximate
        // fallback sequence.
        var bridgeActions =
            BuildExactActionsFromSnapshot(
                item,
                live);

        if (bridgeActions.Count > 0)
        {
            var bridgeActionFailure =
                false;

            foreach (var action in bridgeActions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await _bridge.TriggerActionAsync(
                        action,
                        cancellationToken:
                            cancellationToken);

                    result.ActionsTriggered++;
                    result.BridgeActionsTriggered++;

                    await Task.Delay(
                        _actionDelayMs,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    bridgeActionFailure =
                        true;

                    result.Warnings.Add(
                        $"{item.DisplayName}: SimHub action {action} reported an error: {ex.Message}");

                    // Do not continue sending the remaining stale sequence.
                    // The failed action may actually have executed.
                    break;
                }
            }

            try
            {
                live =
                    await ReadFreshAsync(
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Warnings.Add(
                    $"{item.DisplayName}: ADT could not verify live state after SimHub action fallback: {ex.Message}");

                return FailedUnknownState(
                    item,
                    before,
                    "SimHub action fallback ended with unknown live state");
            }

            if (IsItemAtTarget(
                    item,
                    live))
            {
                return new ApplyOutcome
                {
                    Verified =
                        true,

                    Snapshot =
                        live,

                    Transport =
                        "SimHub action fallback",

                    Note =
                        bridgeActionFailure
                            ? "An action reported an error, but final live readback verified the requested value."
                            : $"{bridgeActions.Count} exact registered AZOM action(s)"
                };
            }

            if (bridgeActionFailure)
            {
                result.Warnings.Add(
                    $"{item.DisplayName}: action fallback stopped after an uncertain action result; ADT re-read the current value before considering another transport.");
            }
        }
        else if (
            item.Kind == AzomApplyItemKind.Numeric &&
            !IsItemAtTarget(item, live))
        {
            result.Warnings.Add(
                $"{item.DisplayName}: public AZOM step actions cannot reach {item.TargetDisplay} exactly from the current live value, so ADT skipped the action fallback.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Fallback 2:
        // documented SimHub CLI action invocation.
        //
        // As above, only an exact action sequence is allowed.
        // Process exit codes are diagnostic; live readback decides success.
        if (_cliFallback is not null)
        {
            var cliActions =
                BuildExactActionsFromSnapshot(
                    item,
                    live);

            if (cliActions.Count > 0)
            {
                foreach (var action in cliActions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var exitCode =
                            await _cliFallback.TriggerAsync(
                                action,
                                cancellationToken);

                        result.ActionsTriggered++;
                        result.CliFallbackActionsTriggered++;

                        if (
                            exitCode.HasValue &&
                            exitCode.Value != 0)
                        {
                            result.Warnings.Add(
                                $"{action}: SimHub helper exit code {exitCode.Value}; ADT will use live readback to determine the actual result.");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add(
                            $"{item.DisplayName}: SimHub CLI action {action} reported an error: {ex.Message}");

                        // As with the in-process action path, the action could
                        // have executed before the transport error occurred.
                        break;
                    }
                }

                try
                {
                    live =
                        await ReadFreshAsync(
                            cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add(
                        $"{item.DisplayName}: ADT could not verify live state after SimHub CLI fallback: {ex.Message}");

                    return FailedUnknownState(
                        item,
                        before,
                        "SimHub CLI fallback ended with unknown live state");
                }

                if (IsItemAtTarget(
                        item,
                        live))
                {
                    return new ApplyOutcome
                    {
                        Verified =
                            true,

                        Snapshot =
                            live,

                        Transport =
                            "SimHub CLI fallback",

                        Note =
                            $"{cliActions.Count} exact action(s), live readback verified"
                    };
                }
            }
        }

        return new ApplyOutcome
        {
            Verified =
                false,

            Snapshot =
                live,

            Transport =
                "Failed verification",

            Note =
                $"Actual {DisplayCurrent(item, live)}"
        };
    }

    private static ApplyOutcome FailedUnknownState(
        AzomApplyPlanItem item,
        AzomLiveSnapshot lastKnown,
        string reason)
    {
        return new ApplyOutcome
        {
            Verified =
                false,

            Snapshot =
                lastKnown,

            Transport =
                "Verification unavailable",

            Note =
                $"{reason}. Last known value: {DisplayCurrent(item, lastKnown)}"
        };
    }

    private async Task<AzomLiveSnapshot> ReadFreshAsync(
        CancellationToken cancellationToken)
    {
        // The bridge snapshot cache refreshes at roughly 5 Hz.
        // Waiting 350 ms deliberately crosses a refresh boundary.
        await Task.Delay(
            ReadbackDelayMs,
            cancellationToken);

        return await ReadAsync(
            cancellationToken);
    }

    private static List<string> BuildExactActionsFromSnapshot(
        AzomApplyPlanItem item,
        AzomLiveSnapshot snapshot)
    {
        if (item.Kind == AzomApplyItemKind.Toggle)
        {
            if (
                IsItemAtTarget(item, snapshot) ||
                string.IsNullOrWhiteSpace(item.ToggleAction))
            {
                return [];
            }

            return
            [
                item.ToggleAction!
            ];
        }

        if (item.Kind != AzomApplyItemKind.Numeric)
        {
            return [];
        }

        var current =
            GetNumeric(
                snapshot,
                item.PropertyName);

        if (
            !current.HasValue ||
            !item.TargetInt.HasValue ||
            string.IsNullOrWhiteSpace(item.ActionBase))
        {
            return [];
        }

        return TryBuildExactStepSequence(
            current.Value,
            item.TargetInt.Value,
            item.FineStep,
            item.CoarseStep,
            item.ActionBase,
            out var actions)
            ? actions
            : [];
    }

    private static bool IsItemAtTarget(
        AzomApplyPlanItem item,
        AzomLiveSnapshot snapshot)
    {
        if (item.Kind == AzomApplyItemKind.Toggle)
        {
            var current =
                GetToggle(
                    snapshot,
                    item.PropertyName);

            return
                current.HasValue &&
                item.TargetBool.HasValue &&
                current.Value ==
                item.TargetBool.Value;
        }

        if (item.Kind != AzomApplyItemKind.Numeric)
        {
            return false;
        }

        var value =
            GetNumeric(
                snapshot,
                item.PropertyName);

        return
            value.HasValue &&
            item.TargetInt.HasValue &&
            value.Value ==
            item.TargetInt.Value;
    }

    private static string DisplayCurrent(
        AzomApplyPlanItem item,
        AzomLiveSnapshot snapshot)
    {
        if (item.Kind == AzomApplyItemKind.Toggle)
        {
            var current =
                GetToggle(
                    snapshot,
                    item.PropertyName);

            return current.HasValue
                ? current.Value
                    ? "ON"
                    : "OFF"
                : "N/A";
        }

        if (item.Kind != AzomApplyItemKind.Numeric)
        {
            return "N/A";
        }

        var value =
            GetNumeric(
                snapshot,
                item.PropertyName);

        return value.HasValue
            ? value.Value.ToString()
            : "N/A";
    }

    private static int? GetNumeric(
        AzomLiveSnapshot snapshot,
        string propertyName)
    {
        return propertyName switch
        {
            "AZOM.FfbStrength" =>
                snapshot.FfbStrength,

            "AZOM.Torque" =>
                snapshot.Torque,

            "AZOM.Rotation" =>
                snapshot.Rotation,

            "AZOM.WheelSpeedLimit" =>
                snapshot.WheelSpeedLimit,

            "AZOM.Interpolation" =>
                snapshot.Interpolation,

            "AZOM.GearshiftVibration" =>
                snapshot.GearshiftVibration,

            "AZOM.Damper" =>
                snapshot.Damper,

            "AZOM.Friction" =>
                snapshot.Friction,

            "AZOM.Inertia" =>
                snapshot.Inertia,

            "AZOM.Spring" =>
                snapshot.Spring,

            "AZOM.GameDamper" =>
                snapshot.GameDamper,

            "AZOM.GameFriction" =>
                snapshot.GameFriction,

            "AZOM.GameInertia" =>
                snapshot.GameInertia,

            "AZOM.GameSpring" =>
                snapshot.GameSpring,

            "AZOM.NaturalInertia" =>
                snapshot.NaturalInertia,

            "AZOM.SoftLimitStiffness" =>
                snapshot.SoftLimitStiffness,

            "AZOM.SpeedDamping" =>
                snapshot.SpeedDamping,

            "AZOM.SpeedDampingPoint" =>
                snapshot.SpeedDampingPoint,

            "AZOM.RoadSensitivity" =>
                snapshot.RoadSensitivity,

            "AZOM.Equalizer1" =>
                snapshot.Equalizer1,

            "AZOM.Equalizer2" =>
                snapshot.Equalizer2,

            "AZOM.Equalizer3" =>
                snapshot.Equalizer3,

            "AZOM.Equalizer4" =>
                snapshot.Equalizer4,

            "AZOM.Equalizer5" =>
                snapshot.Equalizer5,

            "AZOM.Equalizer6" =>
                snapshot.Equalizer6,

            "AZOM.Equalizer7" =>
                snapshot.Equalizer7,

            "AZOM.Equalizer8" =>
                snapshot.Equalizer8,

            "AZOM.Equalizer9" =>
                snapshot.Equalizer9,

            "AZOM.Equalizer10" =>
                snapshot.Equalizer10,

            "AZOM.FfbCurveX1" =>
                snapshot.FfbCurveX1,

            "AZOM.FfbCurveX2" =>
                snapshot.FfbCurveX2,

            "AZOM.FfbCurveX3" =>
                snapshot.FfbCurveX3,

            "AZOM.FfbCurveX4" =>
                snapshot.FfbCurveX4,

            "AZOM.FfbCurveY1" =>
                snapshot.FfbCurveY1,

            "AZOM.FfbCurveY2" =>
                snapshot.FfbCurveY2,

            "AZOM.FfbCurveY3" =>
                snapshot.FfbCurveY3,

            "AZOM.FfbCurveY4" =>
                snapshot.FfbCurveY4,

            "AZOM.FfbCurveY5" =>
                snapshot.FfbCurveY5,

            "AZOM.WorkMode" =>
                snapshot.WorkMode,

            _ =>
                null
        };
    }

    private static bool? GetToggle(
        AzomLiveSnapshot snapshot,
        string propertyName)
    {
        return propertyName switch
        {
            "AZOM.Protection" =>
                snapshot.Protection,

            "AZOM.SoftLimitRetain" =>
                snapshot.SoftLimitRetain,

            "AZOM.FfbReverse" =>
                snapshot.FfbReverse,

            "AZOM.BaseStatusLed" =>
                snapshot.BaseStatusLed,

            "AZOM.Bluetooth" =>
                snapshot.Bluetooth,

            "AZOM.WorkMode" =>
                snapshot.WorkMode.HasValue
                    ? snapshot.WorkMode.Value == 1
                    : (bool?)null,

            _ =>
                null
        };
    }

    public AzomRevertRecord? LoadRevertRecord()
    {
        return _revertStore.Load();
    }

    private static void AddNumeric(
        List<AzomApplyPlanItem> rows,
        string group,
        string display,
        string property,
        int? current,
        int? target,
        string actionBase,
        int fine,
        int coarse,
        string suffix)
    {
        var row =
            new AzomApplyPlanItem
            {
                Group =
                    group,

                DisplayName =
                    display,

                PropertyName =
                    property,

                Kind =
                    AzomApplyItemKind.Numeric,

                CurrentInt =
                    current,

                TargetInt =
                    target,

                CurrentDisplay =
                    current.HasValue &&
                    current.Value >= 0
                        ? current.Value + suffix
                        : "N/A",

                TargetDisplay =
                    target.HasValue
                        ? target.Value + suffix
                        : "N/A",

                ActionBase =
                    actionBase,

                FineStep =
                    fine,

                CoarseStep =
                    coarse,

                CanApply =
                    current.HasValue &&
                    current.Value >= 0 &&
                    target.HasValue
            };

        if (
            row.CanApply &&
            row.IsDifferent)
        {
            row.IsSelectedForApply =
                true;

            if (TryBuildExactStepSequence(
                    current!.Value,
                    target!.Value,
                    fine,
                    coarse,
                    actionBase,
                    out var actions))
            {
                row.EstimatedActions =
                    actions.Count;
            }
            else
            {
                row.EstimatedActions =
                    0;

                row.Note =
                    "Exact AZOM commit required for this target; public action steps cannot reach it exactly.";
            }
        }

        rows.Add(
            row);
    }

    private static void AddToggle(
        List<AzomApplyPlanItem> rows,
        string group,
        string display,
        string property,
        bool? current,
        bool? target,
        string targetAction)
    {
        var different =
            current.HasValue &&
            target.HasValue &&
            current.Value != target.Value;

        rows.Add(
            new AzomApplyPlanItem
            {
                Group =
                    group,

                DisplayName =
                    display,

                PropertyName =
                    property,

                Kind =
                    AzomApplyItemKind.Toggle,

                CurrentBool =
                    current,

                TargetBool =
                    target,

                CurrentDisplay =
                    current.HasValue
                        ? current.Value
                            ? "ON"
                            : "OFF"
                        : "N/A",

                TargetDisplay =
                    target.HasValue
                        ? target.Value
                            ? "ON"
                            : "OFF"
                        : "N/A",

                ToggleAction =
                    targetAction,

                CanApply =
                    current.HasValue &&
                    target.HasValue,

                EstimatedActions =
                    different
                        ? 1
                        : 0,

                IsSelectedForApply =
                    different
            });
    }

    private static bool TryBuildExactStepSequence(
        int current,
        int target,
        int fine,
        int coarse,
        string actionBase,
        out List<string> actions)
    {
        actions =
            [];

        var delta =
            target - current;

        if (delta == 0)
        {
            return true;
        }

        if (
            fine <= 0 ||
            string.IsNullOrWhiteSpace(actionBase))
        {
            return false;
        }

        var directionUp =
            delta > 0;

        var amount =
            Math.Abs(delta);

        var bestActionCount =
            int.MaxValue;

        var bestCoarse =
            0;

        var bestFine =
            0;

        var maxCoarse =
            coarse > 0
                ? amount / coarse
                : 0;

        for (
            var coarseCount = 0;
            coarseCount <= maxCoarse;
            coarseCount++)
        {
            var coarseAmount =
                coarseCount * coarse;

            var remaining =
                amount -
                coarseAmount;

            if (remaining < 0)
            {
                continue;
            }

            if (remaining % fine != 0)
            {
                continue;
            }

            var fineCount =
                remaining / fine;

            var totalActions =
                coarseCount +
                fineCount;

            if (totalActions < bestActionCount)
            {
                bestActionCount =
                    totalActions;

                bestCoarse =
                    coarseCount;

                bestFine =
                    fineCount;
            }
        }

        if (bestActionCount == int.MaxValue)
        {
            return false;
        }

        actions =
            new List<string>(
                bestActionCount);

        var coarseSuffix =
            directionUp
                ? "UpCoarse"
                : "DownCoarse";

        var fineSuffix =
            directionUp
                ? "Up"
                : "Down";

        for (
            var i = 0;
            i < bestCoarse;
            i++)
        {
            actions.Add(
                actionBase +
                coarseSuffix);
        }

        for (
            var i = 0;
            i < bestFine;
            i++)
        {
            actions.Add(
                actionBase +
                fineSuffix);
        }

        return true;
    }

    private static int? GetEq(
        AzomLiveSnapshot snapshot,
        int index)
    {
        return index switch
        {
            1 => snapshot.Equalizer1,
            2 => snapshot.Equalizer2,
            3 => snapshot.Equalizer3,
            4 => snapshot.Equalizer4,
            5 => snapshot.Equalizer5,
            6 => snapshot.Equalizer6,
            7 => snapshot.Equalizer7,
            8 => snapshot.Equalizer8,
            9 => snapshot.Equalizer9,
            10 => snapshot.Equalizer10,
            _ => null
        };
    }

    private static bool IsSupportedAzomNamespace(
        AzomLiveSnapshot snapshot)
    {
        return string.Equals(
            snapshot.PropertyNamespace,
            "AZOM",
            StringComparison.OrdinalIgnoreCase);
    }
}
