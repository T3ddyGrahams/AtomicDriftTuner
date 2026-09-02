using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed class AzomLiveController
{
    // One explicit Apply/Revert batch at a time across the Atomic process.
    // This prevents a second window from starting another sequence while the
    // first batch is still writing and verifying readback.
    private static readonly SemaphoreSlim LiveBatchGate = new(1, 1);

    private readonly AzomBridgeClient _bridge;
    private readonly SimHubActionInvoker? _cliFallback;
    private readonly int _actionDelayMs;
    private readonly AzomRevertStore _revertStore = new();

    public AzomLiveController(
        AzomBridgeClient bridge,
        int actionDelayMs = 70,
        SimHubActionInvoker? cliFallback = null)
    {
        _bridge = bridge;
        _actionDelayMs = Math.Clamp(actionDelayMs, 20, 500);
        _cliFallback = cliFallback;
    }

    public Task<AzomLiveSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
        _bridge.ReadSnapshotAsync(cancellationToken: cancellationToken);

    public List<AzomApplyPlanItem> BuildPlan(AzomSettings target, AzomLiveSnapshot current, bool includePreferences)
    {
        var rows = new List<AzomApplyPlanItem>();

        if (!string.Equals(current.PropertyNamespace, "AZOM", StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(new AzomApplyPlanItem
            {
                Group = "Compatibility",
                DisplayName = "Live write support",
                PropertyName = "AZOM",
                Kind = AzomApplyItemKind.Unsupported,
                CanApply = false,
                CurrentDisplay = string.IsNullOrWhiteSpace(current.PropertyNamespace) ? "No namespace" : current.PropertyNamespace + ".*",
                TargetDisplay = "AZOM.*",
                Note = "Live writes are disabled for legacy property namespaces. Update AZOM before using Apply/Revert."
            });
            return rows;
        }

        AddNumeric(rows, "Core", "Game FFB Strength", "AZOM.FfbStrength", current.FfbStrength, target.Core.GameFfbStrengthPct, "AZOM.FfbStrength", 5, 10, "%");
        AddNumeric(rows, "Core", "Base Torque Output", "AZOM.Torque", current.Torque, target.Core.BaseTorqueOutputPct, "AZOM.Torque", 5, 10, "%");
        AddNumeric(rows, "Core", "Wheel Rotation Angle", "AZOM.Rotation", current.Rotation, target.Core.WheelRotationAngleDeg, "AZOM.Rotation", 90, 180, "°");
        AddNumeric(rows, "Core", "Maximum Wheel Speed", "AZOM.WheelSpeedLimit", current.WheelSpeedLimit, target.Core.MaximumWheelSpeedPct, "AZOM.WheelSpeedLimit", 5, 10, "%");
        AddNumeric(rows, "Core", "Interpolation", "AZOM.Interpolation", current.Interpolation, target.Core.Interpolation, "AZOM.Interpolation", 1, 2, "");

        AddNumeric(rows, "Wheelbase", "Wheel Damper", "AZOM.Damper", current.Damper, target.WheelbaseEffects.WheelDamperPct, "AZOM.Damper", 5, 10, "%");
        AddNumeric(rows, "Wheelbase", "Wheel Friction", "AZOM.Friction", current.Friction, target.WheelbaseEffects.WheelFrictionPct, "AZOM.Friction", 5, 10, "%");
        AddNumeric(rows, "Wheelbase", "Natural Inertia", "AZOM.Inertia", current.Inertia, target.WheelbaseEffects.NaturalInertia, "AZOM.Inertia", 10, 50, "");
        AddNumeric(rows, "Wheelbase", "Wheel Spring", "AZOM.Spring", current.Spring, target.WheelbaseEffects.WheelSpringPct, "AZOM.Spring", 5, 10, "%");

        AddNumeric(rows, "Game Effects", "Game Damper", "AZOM.GameDamper", current.GameDamper, target.GameEffects.GameDamperPct, "AZOM.GameDamper", 5, 10, "%");
        AddNumeric(rows, "Game Effects", "Game Friction", "AZOM.GameFriction", current.GameFriction, target.GameEffects.GameFrictionPct, "AZOM.GameFriction", 5, 10, "%");
        AddNumeric(rows, "Game Effects", "Game Inertia", "AZOM.GameInertia", current.GameInertia, target.GameEffects.GameInertiaPct, "AZOM.GameInertia", 5, 10, "%");
        AddNumeric(rows, "Game Effects", "Game Spring", "AZOM.GameSpring", current.GameSpring, target.GameEffects.GameSpringPct, "AZOM.GameSpring", 5, 10, "%");

        AddNumeric(rows, "Protection", "Steering Wheel Inertia", "AZOM.NaturalInertia", current.NaturalInertia, target.Protection.SteeringWheelInertia, "AZOM.NaturalInertia", 50, 200, "");
        AddNumeric(rows, "Soft Limit", "Soft Limit Stiffness", "AZOM.SoftLimitStiffness", current.SoftLimitStiffness, target.SoftLimit.Stiffness, "AZOM.SoftLimitStiffness", 1, 2, "");
        AddNumeric(rows, "High Speed Damping", "Damping Level", "AZOM.SpeedDamping", current.SpeedDamping, target.HighSpeedDamping.DampingLevelPct, "AZOM.SpeedDamping", 5, 10, "%");
        AddNumeric(rows, "High Speed Damping", "Trigger Speed", "AZOM.SpeedDampingPoint", current.SpeedDampingPoint, target.HighSpeedDamping.TriggerSpeedKph, "AZOM.SpeedDampingPoint", 10, 50, " kph");

        // AZOM RoadSensitivity changes the canned EQ curve, so apply it before custom EQ bands.
        AddNumeric(rows, "FFB Equalizer", "Sensitivity", "AZOM.RoadSensitivity", current.RoadSensitivity, target.FfbEqualizer.Sensitivity, "AZOM.RoadSensitivity", 1, 2, "");

        if (current.HasLegacySixBandEqualizer)
        {
            AddNumeric(rows, "FFB Equalizer", "10 Hz", "AZOM.Equalizer1", current.Equalizer1, target.FfbEqualizer.Hz10, "AZOM.Equalizer1", 5, 25, "%");
            AddNumeric(rows, "FFB Equalizer", "15 Hz", "AZOM.Equalizer2", current.Equalizer2, target.FfbEqualizer.Hz15, "AZOM.Equalizer2", 5, 25, "%");
            AddNumeric(rows, "FFB Equalizer", "25 Hz", "AZOM.Equalizer3", current.Equalizer3, target.FfbEqualizer.Hz25, "AZOM.Equalizer3", 5, 25, "%");
            AddNumeric(rows, "FFB Equalizer", "40 Hz", "AZOM.Equalizer4", current.Equalizer4, target.FfbEqualizer.Hz40, "AZOM.Equalizer4", 5, 25, "%");
            AddNumeric(rows, "FFB Equalizer", "60 Hz", "AZOM.Equalizer5", current.Equalizer5, target.FfbEqualizer.Hz60, "AZOM.Equalizer5", 5, 25, "%");
            AddNumeric(rows, "FFB Equalizer", "100 Hz", "AZOM.Equalizer6", current.Equalizer6, target.FfbEqualizer.Hz100, "AZOM.Equalizer6", 5, 25, "%");
        }
        else
        {
            rows.Add(new AzomApplyPlanItem { Group="FFB Equalizer", DisplayName="Custom six-band EQ", PropertyName="AZOM.Equalizer1..10", Kind=AzomApplyItemKind.Unsupported, CanApply=false, CurrentDisplay="10-band firmware", TargetDisplay="Atomic 6-band curve", Note="Automatic EQ writes are skipped on 10-band firmware until Atomic has a frequency-safe 10-band target model." });
        }

        AddNumeric(rows, "FFB Curve", "Curve X1", "AZOM.FfbCurveX1", current.FfbCurveX1, 20, "AZOM.FfbCurveX1", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve X2", "AZOM.FfbCurveX2", current.FfbCurveX2, 40, "AZOM.FfbCurveX2", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve X3", "AZOM.FfbCurveX3", current.FfbCurveX3, 60, "AZOM.FfbCurveX3", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve X4", "AZOM.FfbCurveX4", current.FfbCurveX4, 80, "AZOM.FfbCurveX4", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve Y1", "AZOM.FfbCurveY1", current.FfbCurveY1, target.FfbOutputCurve.Node20, "AZOM.FfbCurveY1", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve Y2", "AZOM.FfbCurveY2", current.FfbCurveY2, target.FfbOutputCurve.Node40, "AZOM.FfbCurveY2", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve Y3", "AZOM.FfbCurveY3", current.FfbCurveY3, target.FfbOutputCurve.Node60, "AZOM.FfbCurveY3", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve Y4", "AZOM.FfbCurveY4", current.FfbCurveY4, target.FfbOutputCurve.Node80, "AZOM.FfbCurveY4", 5, 10, "");
        AddNumeric(rows, "FFB Curve", "Curve Y5", "AZOM.FfbCurveY5", current.FfbCurveY5, target.FfbOutputCurve.Node100, "AZOM.FfbCurveY5", 5, 10, "");

        if (includePreferences)
        {
            AddNumeric(rows, "Preferences", "Gearshift Vibration", "AZOM.GearshiftVibration", current.GearshiftVibration, target.GearshiftVibration.ShiftIntensity, "AZOM.GearshiftVibration", 1, 2, "");
            AddToggle(rows, "Preferences", "Hands-Off Protection", "AZOM.Protection", current.Protection, target.Protection.HandsOffProtection, target.Protection.HandsOffProtection ? "AZOM.ProtectionOn" : "AZOM.ProtectionOff");
            AddToggle(rows, "Preferences", "Retain Game FFB", "AZOM.SoftLimitRetain", current.SoftLimitRetain, target.SoftLimit.RetainGameFfb, target.SoftLimit.RetainGameFfb ? "AZOM.SoftLimitRetainOn" : "AZOM.SoftLimitRetainOff");
            AddToggle(rows, "Preferences", "FFB Reversal", "AZOM.FfbReverse", current.FfbReverse, target.Miscellaneous.ForceFeedbackReversal, target.Miscellaneous.ForceFeedbackReversal ? "AZOM.FfbReverseOn" : "AZOM.FfbReverseOff");
            AddToggle(rows, "Preferences", "Base Status LED", "AZOM.BaseStatusLed", current.BaseStatusLed, target.Miscellaneous.BaseStatusLed, target.Miscellaneous.BaseStatusLed ? "AZOM.BaseStatusLedOn" : "AZOM.BaseStatusLedOff");
            AddToggle(rows, "Preferences", "Bluetooth", "AZOM.Bluetooth", current.Bluetooth, target.Miscellaneous.Bluetooth, target.Miscellaneous.Bluetooth ? "AZOM.BluetoothOn" : "AZOM.BluetoothOff");
            var currentStandby = current.WorkMode.HasValue ? current.WorkMode.Value == 1 : (bool?)null;
            AddToggle(rows, "Preferences", "Standby Mode", "AZOM.WorkMode", currentStandby, target.Miscellaneous.StandbyMode, target.Miscellaneous.StandbyMode ? "AZOM.WorkModeOff" : "AZOM.WorkModeOn");
            rows.Add(new AzomApplyPlanItem { Group="Preferences", DisplayName="Vibrate on Neutral", Kind=AzomApplyItemKind.Unsupported, CanApply=false, CurrentDisplay="not exposed", TargetDisplay=target.GearshiftVibration.VibrateOnNeutral ? "ON" : "OFF", Note="Current AZOM public SimHub property/action list does not expose this host-side option." });
            rows.Add(new AzomApplyPlanItem { Group="Preferences", DisplayName="Shift Debounce", Kind=AzomApplyItemKind.Unsupported, CanApply=false, CurrentDisplay="not exposed", TargetDisplay=$"{target.GearshiftVibration.ShiftDebounceMs} ms", Note="Current AZOM public SimHub property/action list does not expose this host-side option." });
            rows.Add(new AzomApplyPlanItem { Group="Preferences", DisplayName="Standby After", Kind=AzomApplyItemKind.Unsupported, CanApply=false, CurrentDisplay="not exposed", TargetDisplay=target.Miscellaneous.StandbyAfter, Note="No public AZOM property/action is currently documented for the standby timer dropdown." });
        }

        return rows;
    }

    public List<AzomApplyPlanItem> BuildRevertPlan(AzomLiveSnapshot desired, AzomLiveSnapshot current, IReadOnlyCollection<string> changedProperties)
    {
        var rows = new List<AzomApplyPlanItem>();
        var wanted = new HashSet<string>(changedProperties, StringComparer.OrdinalIgnoreCase);
        void N(string display, string prop, int? c, int? t, string action, int fine, int coarse, string suffix="") { if (wanted.Contains(prop)) AddNumeric(rows, "Revert", display, prop, c, t, action, fine, coarse, suffix); }
        void B(string display, string prop, bool? c, bool? t, string on, string off) { if (wanted.Contains(prop) && t.HasValue) AddToggle(rows, "Revert", display, prop, c, t, t.Value ? on : off); }

        N("Game FFB Strength","AZOM.FfbStrength",current.FfbStrength,desired.FfbStrength,"AZOM.FfbStrength",5,10,"%");
        N("Base Torque Output","AZOM.Torque",current.Torque,desired.Torque,"AZOM.Torque",5,10,"%");
        N("Wheel Rotation Angle","AZOM.Rotation",current.Rotation,desired.Rotation,"AZOM.Rotation",90,180,"°");
        N("Maximum Wheel Speed","AZOM.WheelSpeedLimit",current.WheelSpeedLimit,desired.WheelSpeedLimit,"AZOM.WheelSpeedLimit",5,10,"%");
        N("Interpolation","AZOM.Interpolation",current.Interpolation,desired.Interpolation,"AZOM.Interpolation",1,2);
        N("Gearshift Vibration","AZOM.GearshiftVibration",current.GearshiftVibration,desired.GearshiftVibration,"AZOM.GearshiftVibration",1,2);
        N("Wheel Damper","AZOM.Damper",current.Damper,desired.Damper,"AZOM.Damper",5,10,"%");
        N("Wheel Friction","AZOM.Friction",current.Friction,desired.Friction,"AZOM.Friction",5,10,"%");
        N("Natural Inertia","AZOM.Inertia",current.Inertia,desired.Inertia,"AZOM.Inertia",10,50);
        N("Wheel Spring","AZOM.Spring",current.Spring,desired.Spring,"AZOM.Spring",5,10,"%");
        N("Game Damper","AZOM.GameDamper",current.GameDamper,desired.GameDamper,"AZOM.GameDamper",5,10,"%");
        N("Game Friction","AZOM.GameFriction",current.GameFriction,desired.GameFriction,"AZOM.GameFriction",5,10,"%");
        N("Game Inertia","AZOM.GameInertia",current.GameInertia,desired.GameInertia,"AZOM.GameInertia",5,10,"%");
        N("Game Spring","AZOM.GameSpring",current.GameSpring,desired.GameSpring,"AZOM.GameSpring",5,10,"%");
        N("Steering Wheel Inertia","AZOM.NaturalInertia",current.NaturalInertia,desired.NaturalInertia,"AZOM.NaturalInertia",50,200);
        N("Soft Limit Stiffness","AZOM.SoftLimitStiffness",current.SoftLimitStiffness,desired.SoftLimitStiffness,"AZOM.SoftLimitStiffness",1,2);
        N("High Speed Damping","AZOM.SpeedDamping",current.SpeedDamping,desired.SpeedDamping,"AZOM.SpeedDamping",5,10,"%");
        N("High Speed Trigger","AZOM.SpeedDampingPoint",current.SpeedDampingPoint,desired.SpeedDampingPoint,"AZOM.SpeedDampingPoint",10,50," kph");
        N("Sensitivity","AZOM.RoadSensitivity",current.RoadSensitivity,desired.RoadSensitivity,"AZOM.RoadSensitivity",1,2);
        for (var i = 1; i <= 10; i++)
        {
            var c = GetEq(current, i); var t = GetEq(desired, i);
            N($"Equalizer {i}",$"AZOM.Equalizer{i}",c,t,$"AZOM.Equalizer{i}",5,25,"%");
        }
        N("Curve X1","AZOM.FfbCurveX1",current.FfbCurveX1,desired.FfbCurveX1,"AZOM.FfbCurveX1",5,10);
        N("Curve X2","AZOM.FfbCurveX2",current.FfbCurveX2,desired.FfbCurveX2,"AZOM.FfbCurveX2",5,10);
        N("Curve X3","AZOM.FfbCurveX3",current.FfbCurveX3,desired.FfbCurveX3,"AZOM.FfbCurveX3",5,10);
        N("Curve X4","AZOM.FfbCurveX4",current.FfbCurveX4,desired.FfbCurveX4,"AZOM.FfbCurveX4",5,10);
        N("Curve Y1","AZOM.FfbCurveY1",current.FfbCurveY1,desired.FfbCurveY1,"AZOM.FfbCurveY1",5,10);
        N("Curve Y2","AZOM.FfbCurveY2",current.FfbCurveY2,desired.FfbCurveY2,"AZOM.FfbCurveY2",5,10);
        N("Curve Y3","AZOM.FfbCurveY3",current.FfbCurveY3,desired.FfbCurveY3,"AZOM.FfbCurveY3",5,10);
        N("Curve Y4","AZOM.FfbCurveY4",current.FfbCurveY4,desired.FfbCurveY4,"AZOM.FfbCurveY4",5,10);
        N("Curve Y5","AZOM.FfbCurveY5",current.FfbCurveY5,desired.FfbCurveY5,"AZOM.FfbCurveY5",5,10);
        B("Hands-Off Protection","AZOM.Protection",current.Protection,desired.Protection,"AZOM.ProtectionOn","AZOM.ProtectionOff");
        B("Retain Game FFB","AZOM.SoftLimitRetain",current.SoftLimitRetain,desired.SoftLimitRetain,"AZOM.SoftLimitRetainOn","AZOM.SoftLimitRetainOff");
        B("FFB Reversal","AZOM.FfbReverse",current.FfbReverse,desired.FfbReverse,"AZOM.FfbReverseOn","AZOM.FfbReverseOff");
        B("Base Status LED","AZOM.BaseStatusLed",current.BaseStatusLed,desired.BaseStatusLed,"AZOM.BaseStatusLedOn","AZOM.BaseStatusLedOff");
        B("Bluetooth","AZOM.Bluetooth",current.Bluetooth,desired.Bluetooth,"AZOM.BluetoothOn","AZOM.BluetoothOff");
        if (wanted.Contains("AZOM.WorkMode") && desired.WorkMode.HasValue)
        {
            var c = current.WorkMode.HasValue ? current.WorkMode.Value == 1 : (bool?)null;
            var t = desired.WorkMode.Value == 1;
            AddToggle(rows,"Revert","Standby Mode","AZOM.WorkMode",c,t,t ? "AZOM.WorkModeOff" : "AZOM.WorkModeOn");
        }
        return rows;
    }

    public async Task<AzomApplyResult> ApplyAsync(
        List<AzomApplyPlanItem> plan,
        AzomLiveSnapshot before,
        CancellationToken cancellationToken = default)
    {
        await LiveBatchGate.WaitAsync(cancellationToken);

        try
        {
                    var changed = plan
                        .Where(x => x.CanApply && x.IsDifferent && x.IsSelectedForApply)
                        .ToList();

                    if (changed.Count == 0)
                        return new AzomApplyResult
                        {
                            After = before,
                            SettingsChanged = 0,
                            VerifiedSettingsChanged = 0
                        };

                    _revertStore.Save(before, changed.Select(x => x.PropertyName));

                    var result = new AzomApplyResult { SettingsChanged = changed.Count };
                    var live = before;

                    foreach (var item in changed)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var beforeDisplay = DisplayCurrent(item, live);

                        if (IsItemAtTarget(item, live))
                        {
                            result.VerifiedSettingsChanged++;
                            result.Audit.Add(new AzomApplyAuditItem
                            {
                                Group = item.Group,
                                Setting = item.DisplayName,
                                Before = beforeDisplay,
                                Target = item.TargetDisplay,
                                After = beforeDisplay,
                                Verified = true,
                                Transport = "Already matched"
                            });
                            continue;
                        }

                        var outcome = await ApplyItemVerifiedAsync(item, live, result, cancellationToken);
                        live = outcome.Snapshot;

                        result.Audit.Add(new AzomApplyAuditItem
                        {
                            Group = item.Group,
                            Setting = item.DisplayName,
                            Before = beforeDisplay,
                            Target = item.TargetDisplay,
                            After = DisplayCurrent(item, live),
                            Verified = outcome.Verified,
                            Transport = outcome.Transport,
                            Note = outcome.Note
                        });

                        if (!outcome.Verified)
                        {
                            result.Warnings.Add(
                                $"{item.DisplayName} did not reach the requested value. " +
                                "Atomic stopped the batch before touching any later selected settings.");
                            break;
                        }

                        result.VerifiedSettingsChanged++;
                    }

                    result.After = live;
                    return result;
    
        }
        finally
        {
            LiveBatchGate.Release();
        }
    }

    private sealed class ApplyOutcome
    {
        public bool Verified { get; init; }
        public AzomLiveSnapshot Snapshot { get; init; } = new();
        public string Transport { get; init; } = "";
        public string Note { get; init; } = "";
    }

    private async Task<ApplyOutcome> ApplyItemVerifiedAsync(
        AzomApplyPlanItem item,
        AzomLiveSnapshot before,
        AzomApplyResult result,
        CancellationToken cancellationToken)
    {
        var live = before;

        // v0.6 primary transport: exact AZOM commit. This uses AZOM's own
        // BaseSettingCatalog / SimHubRegistrar commit path and supports targets
        // that public 5/10-step actions cannot represent exactly.
        try
        {
            var method = await _bridge.SetSettingDirectAsync(
                item.PropertyName,
                item.Kind == AzomApplyItemKind.Numeric ? item.TargetInt : null,
                item.Kind == AzomApplyItemKind.Toggle ? item.TargetBool : null,
                cancellationToken: cancellationToken);

            result.DirectFallbackSettingsTriggered++;
            live = await ReadFreshAsync(cancellationToken);

            if (IsItemAtTarget(item, live))
            {
                return new ApplyOutcome
                {
                    Verified = true,
                    Snapshot = live,
                    Transport = "Exact AZOM commit",
                    Note = method ?? "AZOM internal commit path"
                };
            }

            result.Warnings.Add(
                $"{item.DisplayName}: exact AZOM commit returned, but readback is " +
                $"{DisplayCurrent(item, live)} instead of {item.TargetDisplay}.");
        }
        catch (Exception ex)
        {
            result.Warnings.Add(
                $"{item.DisplayName}: exact AZOM commit unavailable: {ex.Message}");
        }

        // Fallback 1: public in-process SimHub action(s).
        var bridgeActions = BuildActionsFromSnapshot(item, live);
        foreach (var action in bridgeActions)
        {
            await _bridge.TriggerActionAsync(action, cancellationToken: cancellationToken);
            result.ActionsTriggered++;
            result.BridgeActionsTriggered++;
            await Task.Delay(_actionDelayMs, cancellationToken);
        }

        if (bridgeActions.Count > 0)
        {
            live = await ReadFreshAsync(cancellationToken);
            if (IsItemAtTarget(item, live))
            {
                return new ApplyOutcome
                {
                    Verified = true,
                    Snapshot = live,
                    Transport = "SimHub action fallback",
                    Note = $"{bridgeActions.Count} registered AZOM action(s)"
                };
            }
        }

        // Fallback 2: documented SimHub CLI. Its exit code is diagnostic only;
        // live AZOM readback decides success.
        if (_cliFallback != null)
        {
            var cliActions = BuildActionsFromSnapshot(item, live);
            foreach (var action in cliActions)
            {
                var exitCode = await _cliFallback.TriggerAsync(action, cancellationToken);
                result.ActionsTriggered++;
                result.CliFallbackActionsTriggered++;
                if (exitCode.HasValue && exitCode.Value != 0)
                    result.Warnings.Add($"{action}: SimHub helper exit code {exitCode.Value}; verifying readback.");
            }

            if (cliActions.Count > 0)
            {
                live = await ReadFreshAsync(cancellationToken);
                if (IsItemAtTarget(item, live))
                {
                    return new ApplyOutcome
                    {
                        Verified = true,
                        Snapshot = live,
                        Transport = "SimHub CLI fallback",
                        Note = $"{cliActions.Count} action(s), readback verified"
                    };
                }
            }
        }

        return new ApplyOutcome
        {
            Verified = false,
            Snapshot = live,
            Transport = "Failed verification",
            Note = $"Actual {DisplayCurrent(item, live)}"
        };
    }

    private async Task<AzomLiveSnapshot> ReadFreshAsync(
        CancellationToken cancellationToken)
    {
        // Bridge cache refreshes at about 5 Hz. Wait across a refresh boundary.
        await Task.Delay(350, cancellationToken);
        return await ReadAsync(cancellationToken);
    }

    private static List<string> BuildActionsFromSnapshot(
        AzomApplyPlanItem item,
        AzomLiveSnapshot snapshot)
    {
        if (item.Kind == AzomApplyItemKind.Toggle)
        {
            return IsItemAtTarget(item, snapshot) ||
                   string.IsNullOrWhiteSpace(item.ToggleAction)
                ? new List<string>()
                : new List<string> { item.ToggleAction! };
        }

        var current = GetNumeric(snapshot, item.PropertyName);

        if (!current.HasValue ||
            !item.TargetInt.HasValue ||
            string.IsNullOrWhiteSpace(item.ActionBase))
            return new List<string>();

        return BuildStepSequence(
            current.Value,
            item.TargetInt.Value,
            item.FineStep,
            item.CoarseStep,
            item.ActionBase);
    }

    private static bool IsItemAtTarget(
        AzomApplyPlanItem item,
        AzomLiveSnapshot snapshot)
    {
        if (item.Kind == AzomApplyItemKind.Toggle)
        {
            var current = GetToggle(snapshot, item.PropertyName);
            return current.HasValue &&
                   item.TargetBool.HasValue &&
                   current.Value == item.TargetBool.Value;
        }

        var value = GetNumeric(snapshot, item.PropertyName);

        return value.HasValue &&
               item.TargetInt.HasValue &&
               value.Value == item.TargetInt.Value;
    }

    private static string DisplayCurrent(
        AzomApplyPlanItem item,
        AzomLiveSnapshot snapshot)
    {
        if (item.Kind == AzomApplyItemKind.Toggle)
        {
            var b = GetToggle(snapshot, item.PropertyName);
            return b.HasValue ? (b.Value ? "ON" : "OFF") : "N/A";
        }

        var n = GetNumeric(snapshot, item.PropertyName);
        return n.HasValue ? n.Value.ToString() : "N/A";
    }

    private static int? GetNumeric(
        AzomLiveSnapshot s,
        string propertyName) =>
        propertyName switch
        {
            "AZOM.FfbStrength" => s.FfbStrength,
            "AZOM.Torque" => s.Torque,
            "AZOM.Rotation" => s.Rotation,
            "AZOM.WheelSpeedLimit" => s.WheelSpeedLimit,
            "AZOM.Interpolation" => s.Interpolation,
            "AZOM.GearshiftVibration" => s.GearshiftVibration,
            "AZOM.Damper" => s.Damper,
            "AZOM.Friction" => s.Friction,
            "AZOM.Inertia" => s.Inertia,
            "AZOM.Spring" => s.Spring,
            "AZOM.GameDamper" => s.GameDamper,
            "AZOM.GameFriction" => s.GameFriction,
            "AZOM.GameInertia" => s.GameInertia,
            "AZOM.GameSpring" => s.GameSpring,
            "AZOM.NaturalInertia" => s.NaturalInertia,
            "AZOM.SoftLimitStiffness" => s.SoftLimitStiffness,
            "AZOM.SpeedDamping" => s.SpeedDamping,
            "AZOM.SpeedDampingPoint" => s.SpeedDampingPoint,
            "AZOM.RoadSensitivity" => s.RoadSensitivity,
            "AZOM.Equalizer1" => s.Equalizer1,
            "AZOM.Equalizer2" => s.Equalizer2,
            "AZOM.Equalizer3" => s.Equalizer3,
            "AZOM.Equalizer4" => s.Equalizer4,
            "AZOM.Equalizer5" => s.Equalizer5,
            "AZOM.Equalizer6" => s.Equalizer6,
            "AZOM.Equalizer7" => s.Equalizer7,
            "AZOM.Equalizer8" => s.Equalizer8,
            "AZOM.Equalizer9" => s.Equalizer9,
            "AZOM.Equalizer10" => s.Equalizer10,
            "AZOM.FfbCurveX1" => s.FfbCurveX1,
            "AZOM.FfbCurveX2" => s.FfbCurveX2,
            "AZOM.FfbCurveX3" => s.FfbCurveX3,
            "AZOM.FfbCurveX4" => s.FfbCurveX4,
            "AZOM.FfbCurveY1" => s.FfbCurveY1,
            "AZOM.FfbCurveY2" => s.FfbCurveY2,
            "AZOM.FfbCurveY3" => s.FfbCurveY3,
            "AZOM.FfbCurveY4" => s.FfbCurveY4,
            "AZOM.FfbCurveY5" => s.FfbCurveY5,
            "AZOM.WorkMode" => s.WorkMode,
            _ => null
        };

    private static bool? GetToggle(
        AzomLiveSnapshot s,
        string propertyName) =>
        propertyName switch
        {
            "AZOM.Protection" => s.Protection,
            "AZOM.SoftLimitRetain" => s.SoftLimitRetain,
            "AZOM.FfbReverse" => s.FfbReverse,
            "AZOM.BaseStatusLed" => s.BaseStatusLed,
            "AZOM.Bluetooth" => s.Bluetooth,
            "AZOM.WorkMode" => s.WorkMode.HasValue
                ? s.WorkMode.Value == 1
                : (bool?)null,
            _ => null
        };

    public AzomRevertRecord? LoadRevertRecord() => _revertStore.Load();

    private static void AddNumeric(List<AzomApplyPlanItem> rows, string group, string display, string prop, int? current, int? target, string actionBase, int fine, int coarse, string suffix)
    {
        var row = new AzomApplyPlanItem
        {
            Group = group,
            DisplayName = display,
            PropertyName = prop,
            Kind = AzomApplyItemKind.Numeric,
            CurrentInt = current,
            TargetInt = target,
            CurrentDisplay = current.HasValue && current.Value >= 0 ? current.Value + suffix : "N/A",
            TargetDisplay = target.HasValue ? target.Value + suffix : "N/A",
            ActionBase = actionBase,
            FineStep = fine,
            CoarseStep = coarse,
            CanApply = current.HasValue && current.Value >= 0 && target.HasValue
        };

        if (row.CanApply && row.IsDifferent)
        {
            row.EstimatedActions = BuildStepSequence(current!.Value, target!.Value, fine, coarse, actionBase).Count;
            row.IsSelectedForApply = true;
            if (fine > 0 && Math.Abs(target.Value - current.Value) % fine != 0)
                row.Note = "Exact AZOM commit required for this target; public action steps cannot land on it exactly.";
        }

        rows.Add(row);
    }

    private static void AddToggle(List<AzomApplyPlanItem> rows, string group, string display, string prop, bool? current, bool? target, string targetAction)
    {
        rows.Add(new AzomApplyPlanItem
        {
            Group=group, DisplayName=display, PropertyName=prop, Kind=AzomApplyItemKind.Toggle,
            CurrentBool=current, TargetBool=target, CurrentDisplay=current.HasValue ? (current.Value ? "ON" : "OFF") : "N/A",
            TargetDisplay=target.HasValue ? (target.Value ? "ON" : "OFF") : "N/A", ToggleAction=targetAction,
            CanApply=current.HasValue && target.HasValue,
            EstimatedActions=current.HasValue && target.HasValue && current.Value != target.Value ? 1 : 0,
            IsSelectedForApply=current.HasValue && target.HasValue && current.Value != target.Value
        });
    }

    private static List<string> BuildStepSequence(int current, int target, int fine, int coarse, string actionBase)
    {
        var delta = target - current;
        if (delta == 0 || fine <= 0) return [];
        var directionUp = delta > 0;
        var amount = Math.Abs(delta);
        var bestError = int.MaxValue;
        var bestActions = int.MaxValue;
        var bestCoarse = 0;
        var bestFine = 0;
        var maxCoarse = coarse > 0 ? amount / coarse + 2 : 0;
        for (var c = 0; c <= maxCoarse; c++)
        {
            var remaining = Math.Abs(amount - c * coarse);
            var f = (int)Math.Round(remaining / (double)fine);
            for (var ff = Math.Max(0, f - 1); ff <= f + 1; ff++)
            {
                var reached = c * coarse + ff * fine;
                var error = Math.Abs(amount - reached);
                var actions = c + ff;
                if (error < bestError || (error == bestError && actions < bestActions))
                {
                    bestError = error; bestActions = actions; bestCoarse = c; bestFine = ff;
                }
            }
        }

        var list = new List<string>(bestActions);
        var coarseSuffix = directionUp ? "UpCoarse" : "DownCoarse";
        var fineSuffix = directionUp ? "Up" : "Down";
        for (var i = 0; i < bestCoarse; i++) list.Add(actionBase + coarseSuffix);
        for (var i = 0; i < bestFine; i++) list.Add(actionBase + fineSuffix);
        return list;
    }

    private static int? GetEq(AzomLiveSnapshot s, int i) => i switch
    {
        1=>s.Equalizer1,2=>s.Equalizer2,3=>s.Equalizer3,4=>s.Equalizer4,5=>s.Equalizer5,
        6=>s.Equalizer6,7=>s.Equalizer7,8=>s.Equalizer8,9=>s.Equalizer9,10=>s.Equalizer10,_=>null
    };
}
