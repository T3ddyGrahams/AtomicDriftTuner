using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TelemetryTuningAssistantEngine
{
    private const double MinimumGeneralDriftSeconds =
        5.0;

    private const double MinimumAngleStabilityDriftSeconds =
        8.0;

    private const double StrongSelfSteerEvidenceSeconds =
        20.0;

    private const double StrongFfbEvidenceSeconds =
        15.0;

    private const double ExtremeSteeringRateDegPerSec =
        1000.0;

    private const double HighSteeringRateDegPerSec =
        900.0;

    public TuningAssistantReport Build(
        TuneInput input,
        CarBehaviorTarget behavior,
        SavedTelemetrySession selected,
        SavedTelemetrySession? previous = null)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            behavior);

        ArgumentNullException.ThrowIfNull(
            selected);

        if (selected.Analysis is null)
        {
            throw new InvalidDataException(
                "ADT cannot build a tuning-assistant report because the selected telemetry session has no analysis.");
        }

        // Never normalize or otherwise mutate the caller-owned Desired
        // Behavior profile merely because a report was requested.
        var desiredBehavior =
            CloneBehavior(
                behavior);

        desiredBehavior.Normalize();

        var analysis =
            selected.Analysis;

        var report =
            new TuningAssistantReport
            {
                OverallConfidence =
                    SessionConfidence(
                        analysis),

                ConfidenceReason =
                    ConfidenceReason(
                        analysis),

                ProposedCalibration =
                    CloneSuggestion(
                        analysis.CalibrationSuggestion),

                SuggestedBehaviorTarget =
                    CloneBehavior(
                        desiredBehavior)
            };

        BuildTransitionAssessment(
            report,
            desiredBehavior,
            analysis);

        BuildSelfSteerAssessment(
            report,
            desiredBehavior,
            analysis);

        BuildAngleStabilityAssessment(
            report,
            desiredBehavior,
            analysis);

        BuildOscillationAssessment(
            report,
            analysis);

        BuildFfbAssessment(
            report,
            analysis);

        AddTargetOnlyAssessment(
            report,
            "Front-end bite",
            BehaviorLabel(
                desiredBehavior.FrontEndBite,
                "calmer",
                "neutral",
                "more aggressive"),
            "Current telemetry does not yet isolate front-tire response from driver steering input strongly enough for ADT to auto-correct this axis.");

        AddTargetOnlyAssessment(
            report,
            "Rear grip",
            BehaviorLabel(
                desiredBehavior.RearGrip,
                "looser",
                "neutral",
                "more planted"),
            "Rear wheel-slip data is recorded, but absolute slip varies substantially by car and tyre model. ADT does not yet treat it as a reliable standalone grip target.");

        AddTargetOnlyAssessment(
            report,
            "Throttle steering",
            BehaviorLabel(
                desiredBehavior.ThrottleSteering,
                "less rotation",
                "neutral",
                "more rotation"),
            "Throttle-to-yaw causality is not yet isolated strongly enough for ADT to make automatic setup corrections from this axis.");

        AddTargetOnlyAssessment(
            report,
            "Initiation",
            BehaviorLabel(
                desiredBehavior.InitiationSharpness,
                "progressive",
                "neutral",
                "sharper"),
            "Drift entries are detected, but entry rise-time and driver-technique effects are not yet separated strongly enough for automatic initiation corrections.");

        // Evidence indicating instability must always win over a target that
        // asks for more wheel response.
        ApplyCalibrationSafetyGuards(
            report.ProposedCalibration,
            analysis);

        ClampSuggestion(
            report.ProposedCalibration);

        DeduplicateReasons(
            report.ProposedCalibration);

        AddCalibrationRecommendations(
            report);

        AddCarSetupRecommendation(
            report,
            desiredBehavior);

        AddPreserveRecommendation(
            report);

        if (
            previous is not null &&
            previous.Analysis is not null)
        {
            BuildComparison(
                report,
                previous.Analysis,
                analysis);
        }

        var needsWork =
            report.Assessments.Count(
                assessment =>
                    assessment.Status ==
                    "NEEDS WORK");

        var onTarget =
            report.Assessments.Count(
                assessment =>
                    assessment.Status ==
                    "ON TARGET");

        if (
            analysis.DriftTimeSeconds <
            MinimumGeneralDriftSeconds)
        {
            report.OverallAssessment =
                "The session is too short for strong tuning conclusions. ADT will preserve the current tune and recommends recording a longer drift run before applying telemetry-based changes.";
        }
        else if (needsWork == 0)
        {
            report.OverallAssessment =
                $"The measured areas are controlled for the current target. {onTarget} telemetry check(s) are on target; preserve those settings and make only small changes to behavior axes that are not yet measured reliably.";
        }
        else
        {
            report.OverallAssessment =
                $"ADT found {needsWork} measured area(s) that do not closely match the desired behavior. Recommendations are deliberately small and preserve areas that already look controlled.";
        }

        report.HasSuggestedBehaviorChange =
            !SameBehavior(
                desiredBehavior,
                report.SuggestedBehaviorTarget);

        if (report.HasSuggestedBehaviorChange)
        {
            report.SuggestedBehaviorSummary =
                BuildBehaviorChangeSummary(
                    desiredBehavior,
                    report.SuggestedBehaviorTarget);
        }
        else
        {
            report.SuggestedBehaviorSummary =
                "No temporary AC Desired Behavior adjustment is proposed from this telemetry session.";
        }

        return report;
    }

    private static void BuildTransitionAssessment(
        TuningAssistantReport report,
        CarBehaviorTarget behavior,
        TelemetryAnalysis analysis)
    {
        var desired =
            BehaviorLabel(
                behavior.TransitionSpeed,
                "smooth / slower",
                "balanced",
                "quick");

        if (analysis.TransitionCount < 2)
        {
            AddAssessment(
                report,
                "Transition speed",
                desired,
                "Not enough complete transitions",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                $"Only {analysis.TransitionCount} complete transition(s) were detected. Two or more are required before ADT biases the car setup from transition telemetry.");

            return;
        }

        var observedLevel =
            TransitionLevel(
                analysis.AverageTransitionSeconds);

        var confidence =
            analysis.TransitionCount >= 6
                ? AssistantConfidence.High
                : AssistantConfidence.Medium;

        var difference =
            behavior.TransitionSpeed -
            observedLevel;

        var status =
            DifferenceStatus(
                difference);

        AddAssessment(
            report,
            "Transition speed",
            desired,
            $"{TransitionObservedLabel(observedLevel)} ({analysis.AverageTransitionSeconds:0.00}s crossover)",
            status,
            confidence,
            $"{analysis.TransitionCount} complete direction changes were detected. Lower low-angle crossover time is treated as quicker transition response.");

        if (difference >= 1)
        {
            report.SuggestedBehaviorTarget.TransitionSpeed =
                Math.Clamp(
                    report.SuggestedBehaviorTarget.TransitionSpeed + 1,
                    -2,
                    2);

            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain =
                        "AC CAR SETUP",

                    Priority =
                        difference >= 2
                            ? "HIGH"
                            : "MEDIUM",

                    Change =
                        "Bias the AC setup one step toward quicker transitions.",

                    Why =
                        $"Observed transition crossover ({analysis.AverageTransitionSeconds:0.00}s) is slower than the saved Desired Behavior target. ADT will hand this to the behavior-blending and range-safe setup tuner rather than writing raw setup values directly.",

                    Confidence =
                        confidence
                            .ToString()
                            .ToUpperInvariant()
                });
        }
        else if (difference <= -2)
        {
            report.SuggestedBehaviorTarget.TransitionSpeed =
                Math.Clamp(
                    report.SuggestedBehaviorTarget.TransitionSpeed - 1,
                    -2,
                    2);

            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain =
                        "AC CAR SETUP",

                    Priority =
                        "MEDIUM",

                    Change =
                        "Bias the AC setup one step toward smoother transitions.",

                    Why =
                        "The car is transitioning materially quicker than the saved Desired Behavior target.",

                    Confidence =
                        confidence
                            .ToString()
                            .ToUpperInvariant()
                });
        }
        else if (
            status ==
            AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "Transition response is on target; preserve transition-oriented rear-platform settings.");
        }
    }

    private static void BuildSelfSteerAssessment(
        TuningAssistantReport report,
        CarBehaviorTarget behavior,
        TelemetryAnalysis analysis)
    {
        var desired =
            BehaviorLabel(
                behavior.SelfSteerSpeed,
                "slower",
                "balanced",
                "faster");

        if (
            analysis.DriftTimeSeconds <
            MinimumGeneralDriftSeconds)
        {
            AddAssessment(
                report,
                "Self-steer speed",
                desired,
                "Not enough sustained drift",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                "At least several seconds of sustained drift are needed before the steering-return heuristic is useful.");

            return;
        }

        var observedLevel =
            SelfSteerLevel(
                analysis.AverageSteeringRateDegPerSec);

        var difference =
            behavior.SelfSteerSpeed -
            observedLevel;

        // Steering rate is influenced by the driver's hands and corrections,
        // so this metric deliberately remains capped at MEDIUM confidence.
        var confidence =
            analysis.DriftTimeSeconds >=
            StrongSelfSteerEvidenceSeconds
                ? AssistantConfidence.Medium
                : AssistantConfidence.Low;

        var status =
            DifferenceStatus(
                difference);

        AddAssessment(
            report,
            "Self-steer speed",
            desired,
            $"{SelfSteerObservedLabel(observedLevel)} ({analysis.AverageSteeringRateDegPerSec:0}°/s average)",
            status,
            confidence,
            $"Average and peak steering rate ({analysis.PeakSteeringRateDegPerSec:0}°/s) are used as a heuristic. Driver corrections can influence this metric, so confidence is intentionally capped.");

        var unsafeFastSteering =
            analysis.OscillationEvents >= 4 ||
            analysis.PeakSteeringRateDegPerSec >
            ExtremeSteeringRateDegPerSec;

        if (unsafeFastSteering)
        {
            // Safety evidence wins over a Desired Behavior target asking for
            // more self-steer speed.
            if (
                report.ProposedCalibration.WheelSpeedDelta >
                -3)
            {
                report.ProposedCalibration.WheelSpeedDelta =
                    -3;
            }

            if (
                report.ProposedCalibration.DampingDelta <
                1)
            {
                report.ProposedCalibration.DampingDelta =
                    1;
            }

            report.ProposedCalibration.Reasons.Add(
                "Peak steering speed or oscillation evidence is already high, so ADT will not recommend additional wheel speed even if the saved self-steer target asks for faster response.");
        }
        else if (difference >= 1)
        {
            if (
                report.ProposedCalibration.WheelSpeedDelta <=
                0)
            {
                report.ProposedCalibration.WheelSpeedDelta +=
                    difference >= 2
                        ? 3
                        : 2;
            }

            if (
                report.ProposedCalibration.DampingDelta >=
                0)
            {
                report.ProposedCalibration.DampingDelta -=
                    1;
            }

            report.ProposedCalibration.Reasons.Add(
                "Desired self-steer is faster than the measured steering-return heuristic and no strong instability evidence is present: allow a small wheel-speed increase with slightly less damping.");
        }
        else if (difference <= -1)
        {
            if (
                report.ProposedCalibration.WheelSpeedDelta >=
                0)
            {
                report.ProposedCalibration.WheelSpeedDelta -=
                    difference <= -2
                        ? 3
                        : 2;
            }

            if (
                report.ProposedCalibration.DampingDelta <=
                0)
            {
                report.ProposedCalibration.DampingDelta +=
                    1;
            }

            report.ProposedCalibration.Reasons.Add(
                "Measured steering return is faster than the saved self-steer target: add a small amount of control.");
        }
        else if (
            status ==
            AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "Self-steer rate is near the saved target; preserve wheel-speed and damping balance unless stronger stability evidence requires a change.");
        }
    }

    private static void BuildAngleStabilityAssessment(
        TuningAssistantReport report,
        CarBehaviorTarget behavior,
        TelemetryAnalysis analysis)
    {
        var desired =
            BehaviorLabel(
                behavior.AngleStability,
                "lively",
                "balanced",
                "stable");

        if (
            analysis.DriftTimeSeconds <
            MinimumAngleStabilityDriftSeconds)
        {
            AddAssessment(
                report,
                "Angle stability",
                desired,
                "Not enough sustained drift",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                "Extreme-angle event rate becomes too noisy in very short sessions.");

            return;
        }

        var extremePerMinute =
            EventRate(
                analysis.SpinEvents,
                analysis.DriftTimeSeconds);

        var observedLevel =
            extremePerMinute switch
            {
                <= 0.30 => 2,
                <= 0.80 => 1,
                <= 1.50 => 0,
                <= 2.50 => -1,
                _ => -2
            };

        var difference =
            behavior.AngleStability -
            observedLevel;

        var status =
            DifferenceStatus(
                difference);

        AddAssessment(
            report,
            "Angle stability",
            desired,
            $"{AngleObservedLabel(observedLevel)} ({extremePerMinute:0.0} sustained extreme-angle events/min)",
            status,
            AssistantConfidence.Medium,
            $"ADT saw {analysis.SpinEvents} sustained event(s) above 72° body slip during {analysis.DriftTimeSeconds:0}s of detected drift. These can include intentional extreme entries, so they are not treated as confirmed spins and confidence is capped at MEDIUM.");

        if (difference >= 1)
        {
            report.SuggestedBehaviorTarget.AngleStability =
                Math.Clamp(
                    report.SuggestedBehaviorTarget.AngleStability + 1,
                    -2,
                    2);

            if (
                report.SuggestedBehaviorTarget.RearGrip < 2 &&
                extremePerMinute >= 1.5)
            {
                report.SuggestedBehaviorTarget.RearGrip++;
            }

            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain =
                        "AC CAR SETUP",

                    Priority =
                        difference >= 2
                            ? "HIGH"
                            : "MEDIUM",

                    Change =
                        "Bias the setup one step toward more high-angle stability.",

                    Why =
                        "The sustained extreme-angle event rate is higher than the saved stability target. Temporary behavior guidance may also add one rear-grip step when the event rate is high.",

                    Confidence =
                        "MEDIUM"
                });

            if (
                analysis.OscillationEvents > 0 ||
                analysis.PeakSteeringRateDegPerSec >
                HighSteeringRateDegPerSec)
            {
                report.ProposedCalibration.SpeedDampingDelta +=
                    1;

                report.ProposedCalibration.Reasons.Add(
                    "Angle stability is below target and steering also has fast or oscillatory evidence: add a small high-speed damping increment.");
            }
        }
        else if (
            status ==
            AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "High-angle stability evidence is on target; preserve stability-oriented car-setup settings.");
        }
    }

    private static void BuildOscillationAssessment(
        TuningAssistantReport report,
        TelemetryAnalysis analysis)
    {
        if (
            analysis.DriftTimeSeconds <
            MinimumGeneralDriftSeconds)
        {
            AddAssessment(
                report,
                "Oscillation control",
                "Low / controlled",
                "Not enough sustained drift",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                "Oscillation clustering requires sustained drift steering activity.");

            return;
        }

        var perMinute =
            EventRate(
                analysis.OscillationEvents,
                analysis.DriftTimeSeconds);

        var status =
            perMinute <= 0.5
                ? AssistantFindingStatus.OnTarget
                : perMinute <= 1.5
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        // Fast steering reversals can still contain deliberate driver input,
        // so do not claim HIGH confidence until ADT can better separate
        // hands-off oscillation from manual correction.
        var confidence =
            analysis.DriftTimeSeconds >=
            StrongSelfSteerEvidenceSeconds
                ? AssistantConfidence.Medium
                : AssistantConfidence.Low;

        AddAssessment(
            report,
            "Oscillation control",
            "Low / controlled",
            $"{analysis.OscillationEvents} cluster(s), {perMinute:0.0}/min",
            status,
            confidence,
            "Fast steering-direction reversals inside short time windows are grouped as oscillation evidence. Driver corrections can also contribute to this metric.");

        if (
            status ==
            AssistantFindingStatus.NeedsWork)
        {
            if (
                report.ProposedCalibration.DampingDelta <
                2)
            {
                report.ProposedCalibration.DampingDelta =
                    2;
            }

            if (
                report.ProposedCalibration.SpeedDampingDelta <
                2)
            {
                report.ProposedCalibration.SpeedDampingDelta =
                    2;
            }

            if (
                report.ProposedCalibration.WheelSpeedDelta >
                -4)
            {
                report.ProposedCalibration.WheelSpeedDelta =
                    -4;
            }

            report.ProposedCalibration.Reasons.Add(
                "Oscillation rate is high: add wheelbase control and reduce maximum wheel speed slightly before chasing faster self-steer.");
        }
        else if (
            status ==
            AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "Oscillation evidence is controlled; do not add damping solely for stability if the car already meets the target.");
        }
    }

    private static void BuildFfbAssessment(
        TuningAssistantReport report,
        TelemetryAnalysis analysis)
    {
        if (
            analysis.DriftTimeSeconds <
            MinimumGeneralDriftSeconds)
        {
            AddAssessment(
                report,
                "FFB headroom",
                "Low sustained clipping",
                "Not enough sustained drift",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                "FFB saturation is only meaningful during a representative drift load.");

            return;
        }

        var status =
            analysis.FfbClippingPctWhileDrifting < 4
                ? AssistantFindingStatus.OnTarget
                : analysis.FfbClippingPctWhileDrifting < 8
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        var confidence =
            analysis.DriftTimeSeconds >=
            StrongFfbEvidenceSeconds
                ? AssistantConfidence.High
                : AssistantConfidence.Medium;

        AddAssessment(
            report,
            "FFB headroom",
            "Low sustained clipping",
            $"{analysis.FfbClippingPctWhileDrifting:0.0}% of drift samples ≥ 98% |FFB|",
            status,
            confidence,
            $"Average absolute FFB during detected drift was {analysis.AverageFfbAbsWhileDrifting:0.000}. This is a clipping/headroom heuristic, not a force-quality score.");

        if (
            status ==
            AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "FFB headroom looks healthy; preserve AC gain unless the driver reports that overall force is too weak or too strong.");
        }
    }

    private static void ApplyCalibrationSafetyGuards(
        TelemetryCalibrationSuggestion suggestion,
        TelemetryAnalysis analysis)
    {
        if (analysis.OscillationEvents >= 4)
        {
            suggestion.WheelSpeedDelta =
                Math.Min(
                    suggestion.WheelSpeedDelta,
                    -4);

            suggestion.DampingDelta =
                Math.Max(
                    suggestion.DampingDelta,
                    2);

            suggestion.SpeedDampingDelta =
                Math.Max(
                    suggestion.SpeedDampingDelta,
                    2);
        }

        if (
            analysis.PeakSteeringRateDegPerSec >
            ExtremeSteeringRateDegPerSec)
        {
            suggestion.WheelSpeedDelta =
                Math.Min(
                    suggestion.WheelSpeedDelta,
                    -3);

            suggestion.DampingDelta =
                Math.Max(
                    suggestion.DampingDelta,
                    1);
        }

        if (analysis.SpinEvents >= 3)
        {
            suggestion.SpeedDampingDelta =
                Math.Max(
                    suggestion.SpeedDampingDelta,
                    2);

            suggestion.FrictionDelta =
                Math.Max(
                    suggestion.FrictionDelta,
                    1);
        }
    }

    private static void AddCalibrationRecommendations(
        TuningAssistantReport report)
    {
        var suggestion =
            report.ProposedCalibration;

        if (
            suggestion.WheelSpeedDelta != 0 ||
            suggestion.DampingDelta != 0 ||
            suggestion.FrictionDelta != 0 ||
            suggestion.SpeedDampingDelta != 0 ||
            suggestion.TorqueLimitDelta != 0 ||
            suggestion.InterpolationDelta != 0)
        {
            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain =
                        "AZOM / CALIBRATION",

                    Priority =
                        Math.Abs(
                            suggestion.WheelSpeedDelta) >= 5 ||
                        Math.Abs(
                            suggestion.DampingDelta) >= 3
                            ? "HIGH"
                            : "MEDIUM",

                    Change =
                        $"Wheel speed {Signed(suggestion.WheelSpeedDelta)}, damper {Signed(suggestion.DampingDelta)}, friction {Signed(suggestion.FrictionDelta)}, high-speed damping {Signed(suggestion.SpeedDampingDelta)}, torque {Signed(suggestion.TorqueLimitDelta)}, interpolation {Signed(suggestion.InterpolationDelta)}.",

                    Why =
                        "Telemetry evidence is converted into the same bounded ADT calibration layer used by the main tuning engine. Applying this recommendation does not directly write the wheelbase.",

                    Confidence =
                        report.OverallConfidence
                            .ToString()
                            .ToUpperInvariant()
                });
        }

        if (suggestion.AcGainDelta != 0)
        {
            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain =
                        "AC FFB",

                    Priority =
                        Math.Abs(
                            suggestion.AcGainDelta) >= 3
                            ? "HIGH"
                            : "MEDIUM",

                    Change =
                        $"AC Gain calibration {Signed(suggestion.AcGainDelta)}.",

                    Why =
                        "The telemetry FFB-headroom heuristic found sustained output saturation. ADT adjusts its generated AC gain rather than hiding clipping with unrelated wheelbase settings.",

                    Confidence =
                        report.OverallConfidence
                            .ToString()
                            .ToUpperInvariant()
                });
        }
    }

    private static void AddCarSetupRecommendation(
        TuningAssistantReport report,
        CarBehaviorTarget original)
    {
        if (
            SameBehavior(
                original,
                report.SuggestedBehaviorTarget))
        {
            return;
        }

        report.Recommendations.Add(
            new AssistantRecommendation
            {
                Domain =
                    "AC CAR SETUP",

                Priority =
                    "REVIEW",

                Change =
                    "Open the AC Car Setup Tuner with temporary telemetry guidance.",

                Why =
                    "The assistant changes temporary Desired Behavior guidance only one step at a time, then lets ADT's behavior-blending and setup-range safeguards convert those goals into car-specific values. Nothing is saved automatically.",

                Confidence =
                    report.OverallConfidence
                        .ToString()
                        .ToUpperInvariant()
            });
    }

    private static void AddPreserveRecommendation(
        TuningAssistantReport report)
    {
        if (report.PreserveNotes.Count == 0)
        {
            return;
        }

        report.Recommendations.Add(
            new AssistantRecommendation
            {
                Domain =
                    "PRESERVE",

                Priority =
                    "KEEP",

                Change =
                    "Keep settings associated with areas that are already on target.",

                Why =
                    string.Join(
                        " ",
                        report.PreserveNotes
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase)),

                Confidence =
                    report.OverallConfidence
                        .ToString()
                        .ToUpperInvariant()
            });
    }

    private static void BuildComparison(
        TuningAssistantReport report,
        TelemetryAnalysis previous,
        TelemetryAnalysis current)
    {
        report.Comparison.Add(
            CompareContext(
                "Drift time",
                previous.DriftTimePct,
                current.DriftTimePct,
                "%",
                "Drift-time percentage is context, not a score. A higher value can simply reflect a different track section or driving objective."));

        if (
            previous.TransitionCount > 0 &&
            current.TransitionCount > 0)
        {
            report.Comparison.Add(
                CompareDirectional(
                    "Avg transition",
                    previous.AverageTransitionSeconds,
                    current.AverageTransitionSeconds,
                    "s",
                    higherIsBetter: false,
                    "Lower is a quicker low-angle crossover. Compare only similar driving and track sections."));
        }

        if (
            previous.DriftTimeSeconds > 0 &&
            current.DriftTimeSeconds > 0)
        {
            report.Comparison.Add(
                CompareDirectional(
                    "Oscillation rate",
                    EventRate(
                        previous.OscillationEvents,
                        previous.DriftTimeSeconds),
                    EventRate(
                        current.OscillationEvents,
                        current.DriftTimeSeconds),
                    "/min",
                    higherIsBetter: false,
                    "Lower fast steering-reversal clustering is generally preferable, but driver corrections can affect this metric."));

            report.Comparison.Add(
                CompareDirectional(
                    "Extreme-angle rate",
                    EventRate(
                        previous.SpinEvents,
                        previous.DriftTimeSeconds),
                    EventRate(
                        current.SpinEvents,
                        current.DriftTimeSeconds),
                    "/min",
                    higherIsBetter: false,
                    "Lower can indicate greater stability, but intentional extreme entries can affect this metric."));
        }

        report.Comparison.Add(
            CompareDirectional(
                "FFB clipping",
                previous.FfbClippingPctWhileDrifting,
                current.FfbClippingPctWhileDrifting,
                "%",
                higherIsBetter: false,
                "Lower sustained saturation usually preserves more FFB headroom and detail."));
    }

    private static AssistantComparisonRow CompareDirectional(
        string metric,
        double previous,
        double current,
        string unit,
        bool higherIsBetter,
        string note)
    {
        previous =
            FiniteOrZero(
                previous);

        current =
            FiniteOrZero(
                current);

        var delta =
            current -
            previous;

        var unchanged =
            Math.Abs(delta) <
            0.01;

        var improved =
            !unchanged &&
            (
                higherIsBetter
                    ? delta > 0
                    : delta < 0
            );

        var interpretation =
            unchanged
                ? "Essentially unchanged. " + note
                : improved
                    ? "Moved in the generally favorable direction. " + note
                    : "Moved in the generally unfavorable direction. " + note;

        return new AssistantComparisonRow
        {
            Metric =
                metric,

            Previous =
                $"{previous:0.00}{unit}",

            Current =
                $"{current:0.00}{unit}",

            Change =
                SignedDouble(
                    delta,
                    unit),

            Interpretation =
                interpretation
        };
    }

    private static AssistantComparisonRow CompareContext(
        string metric,
        double previous,
        double current,
        string unit,
        string note)
    {
        previous =
            FiniteOrZero(
                previous);

        current =
            FiniteOrZero(
                current);

        var delta =
            current -
            previous;

        return new AssistantComparisonRow
        {
            Metric =
                metric,

            Previous =
                $"{previous:0.00}{unit}",

            Current =
                $"{current:0.00}{unit}",

            Change =
                SignedDouble(
                    delta,
                    unit),

            Interpretation =
                "Context only. " + note
        };
    }

    private static double EventRate(
        int events,
        double driftSeconds)
    {
        if (
            events <= 0 ||
            !double.IsFinite(driftSeconds) ||
            driftSeconds <= 0)
        {
            return 0;
        }

        return
            events *
            60.0 /
            driftSeconds;
    }

    private static AssistantConfidence SessionConfidence(
        TelemetryAnalysis analysis)
    {
        var score =
            0;

        if (analysis.DriftTimeSeconds >= 45)
        {
            score +=
                2;
        }
        else if (analysis.DriftTimeSeconds >= 15)
        {
            score +=
                1;
        }

        if (analysis.TransitionCount >= 6)
        {
            score +=
                2;
        }
        else if (analysis.TransitionCount >= 2)
        {
            score +=
                1;
        }

        if (
            analysis.EffectiveSampleRateHz >= 35 &&
            analysis.EffectiveSampleRateHz <= 200)
        {
            score +=
                1;
        }

        if (analysis.DriftEntries >= 3)
        {
            score +=
                1;
        }

        return score >= 5
            ? AssistantConfidence.High
            : score >= 3
                ? AssistantConfidence.Medium
                : AssistantConfidence.Low;
    }

    private static string ConfidenceReason(
        TelemetryAnalysis analysis)
    {
        return
            $"{analysis.DriftTimeSeconds:0}s detected drift • " +
            $"{analysis.TransitionCount} transition(s) • " +
            $"{analysis.DriftEntries} drift entr{(analysis.DriftEntries == 1 ? "y" : "ies")} • " +
            $"{analysis.EffectiveSampleRateHz:0.0} Hz effective sample rate.";
    }

    private static AssistantFindingStatus DifferenceStatus(
        int difference)
    {
        return Math.Abs(difference) switch
        {
            0 =>
                AssistantFindingStatus.OnTarget,

            1 =>
                AssistantFindingStatus.NearTarget,

            _ =>
                AssistantFindingStatus.NeedsWork
        };
    }

    private static int TransitionLevel(
        double seconds)
    {
        return seconds switch
        {
            <= 0.45 => 2,
            <= 0.65 => 1,
            <= 0.90 => 0,
            <= 1.20 => -1,
            _ => -2
        };
    }

    private static string TransitionObservedLabel(
        int level)
    {
        return level switch
        {
            2 => "very quick",
            1 => "quick",
            0 => "balanced",
            -1 => "smooth / slower",
            _ => "very slow"
        };
    }

    private static int SelfSteerLevel(
        double rate)
    {
        return rate switch
        {
            >= 350 => 2,
            >= 220 => 1,
            >= 130 => 0,
            >= 80 => -1,
            _ => -2
        };
    }

    private static string SelfSteerObservedLabel(
        int level)
    {
        return level switch
        {
            2 => "very fast",
            1 => "fast",
            0 => "balanced",
            -1 => "slow",
            _ => "very slow"
        };
    }

    private static string AngleObservedLabel(
        int level)
    {
        return level switch
        {
            2 => "very stable evidence",
            1 => "stable evidence",
            0 => "mixed / balanced evidence",
            -1 => "lively / loss-prone evidence",
            _ => "unstable evidence"
        };
    }

    private static string BehaviorLabel(
        int value,
        string negative,
        string neutral,
        string positive)
    {
        return value switch
        {
            <= -2 =>
                $"strongly {negative}",

            -1 =>
                negative,

            0 =>
                neutral,

            1 =>
                positive,

            _ =>
                $"strongly {positive}"
        };
    }

    private static void AddTargetOnlyAssessment(
        TuningAssistantReport report,
        string behavior,
        string desired,
        string evidence)
    {
        AddAssessment(
            report,
            behavior,
            desired,
            "Not isolated reliably from telemetry yet",
            AssistantFindingStatus.TargetOnly,
            AssistantConfidence.Low,
            evidence);
    }

    private static void AddAssessment(
        TuningAssistantReport report,
        string behavior,
        string desired,
        string observed,
        AssistantFindingStatus status,
        AssistantConfidence confidence,
        string evidence)
    {
        report.Assessments.Add(
            new AssistantBehaviorAssessment
            {
                Behavior =
                    behavior,

                Desired =
                    desired,

                Observed =
                    observed,

                Status =
                    StatusText(
                        status),

                Confidence =
                    confidence
                        .ToString()
                        .ToUpperInvariant(),

                Evidence =
                    evidence
            });
    }

    private static string StatusText(
        AssistantFindingStatus status)
    {
        return status switch
        {
            AssistantFindingStatus.OnTarget =>
                "ON TARGET",

            AssistantFindingStatus.NearTarget =>
                "NEAR TARGET",

            AssistantFindingStatus.NeedsWork =>
                "NEEDS WORK",

            AssistantFindingStatus.TargetOnly =>
                "TARGET SAVED",

            _ =>
                "INSUFFICIENT"
        };
    }

    private static TelemetryCalibrationSuggestion CloneSuggestion(
        TelemetryCalibrationSuggestion source)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        return new TelemetryCalibrationSuggestion
        {
            TorqueLimitDelta =
                source.TorqueLimitDelta,

            WheelSpeedDelta =
                source.WheelSpeedDelta,

            DampingDelta =
                source.DampingDelta,

            FrictionDelta =
                source.FrictionDelta,

            SpeedDampingDelta =
                source.SpeedDampingDelta,

            InterpolationDelta =
                source.InterpolationDelta,

            AcGainDelta =
                source.AcGainDelta,

            Reasons =
                source.Reasons is null
                    ? []
                    : new List<string>(
                        source.Reasons)
        };
    }

    private static CarBehaviorTarget CloneBehavior(
        CarBehaviorTarget source)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        return new CarBehaviorTarget
        {
            Key =
                source.Key,

            DisplayName =
                source.DisplayName,

            UpdatedUtc =
                source.UpdatedUtc,

            FrontEndBite =
                source.FrontEndBite,

            RearGrip =
                source.RearGrip,

            SelfSteerSpeed =
                source.SelfSteerSpeed,

            TransitionSpeed =
                source.TransitionSpeed,

            AngleStability =
                source.AngleStability,

            ThrottleSteering =
                source.ThrottleSteering,

            InitiationSharpness =
                source.InitiationSharpness
        };
    }

    private static bool SameBehavior(
        CarBehaviorTarget first,
        CarBehaviorTarget second)
    {
        return
            first.FrontEndBite ==
            second.FrontEndBite &&

            first.RearGrip ==
            second.RearGrip &&

            first.SelfSteerSpeed ==
            second.SelfSteerSpeed &&

            first.TransitionSpeed ==
            second.TransitionSpeed &&

            first.AngleStability ==
            second.AngleStability &&

            first.ThrottleSteering ==
            second.ThrottleSteering &&

            first.InitiationSharpness ==
            second.InitiationSharpness;
    }

    private static string BuildBehaviorChangeSummary(
        CarBehaviorTarget before,
        CarBehaviorTarget after)
    {
        var changes =
            new List<string>();

        AddBehaviorChange(
            changes,
            "Transition",
            before.TransitionSpeed,
            after.TransitionSpeed);

        AddBehaviorChange(
            changes,
            "Angle stability",
            before.AngleStability,
            after.AngleStability);

        AddBehaviorChange(
            changes,
            "Rear grip",
            before.RearGrip,
            after.RearGrip);

        AddBehaviorChange(
            changes,
            "Self-steer",
            before.SelfSteerSpeed,
            after.SelfSteerSpeed);

        return changes.Count == 0
            ? "No temporary AC Desired Behavior adjustment is proposed."
            : "Temporary AC behavior guidance: " +
              string.Join(
                  " • ",
                  changes) +
              ". These values are not saved unless you explicitly save them in the AC Car Setup Tuner.";
    }

    private static void AddBehaviorChange(
        List<string> changes,
        string name,
        int before,
        int after)
    {
        if (before == after)
        {
            return;
        }

        changes.Add(
            $"{name} {Signed(before)} → {Signed(after)}");
    }

    private static void ClampSuggestion(
        TelemetryCalibrationSuggestion suggestion)
    {
        suggestion.TorqueLimitDelta =
            Math.Clamp(
                suggestion.TorqueLimitDelta,
                -10,
                10);

        suggestion.WheelSpeedDelta =
            Math.Clamp(
                suggestion.WheelSpeedDelta,
                -10,
                10);

        suggestion.DampingDelta =
            Math.Clamp(
                suggestion.DampingDelta,
                -5,
                5);

        suggestion.FrictionDelta =
            Math.Clamp(
                suggestion.FrictionDelta,
                -3,
                3);

        suggestion.SpeedDampingDelta =
            Math.Clamp(
                suggestion.SpeedDampingDelta,
                -5,
                5);

        suggestion.InterpolationDelta =
            Math.Clamp(
                suggestion.InterpolationDelta,
                -2,
                2);

        suggestion.AcGainDelta =
            Math.Clamp(
                suggestion.AcGainDelta,
                -5,
                2);
    }

    private static void DeduplicateReasons(
        TelemetryCalibrationSuggestion suggestion)
    {
        if (
            suggestion.Reasons is null ||
            suggestion.Reasons.Count <= 1)
        {
            return;
        }

        suggestion.Reasons =
            suggestion.Reasons
                .Where(
                    reason =>
                        !string.IsNullOrWhiteSpace(
                            reason))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    private static double FiniteOrZero(
        double value)
    {
        return double.IsFinite(value)
            ? value
            : 0;
    }

    private static string Signed(
        int value)
    {
        return value >= 0
            ? $"+{value}"
            : value.ToString();
    }

    private static string SignedDouble(
        double value,
        string unit)
    {
        return value >= 0
            ? $"+{value:0.00}{unit}"
            : $"{value:0.00}{unit}";
    }
}
