using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TelemetryTuningAssistantEngine
{
    public TuningAssistantReport Build(
        TuneInput input,
        CarBehaviorTarget behavior,
        SavedTelemetrySession selected,
        SavedTelemetrySession? previous = null)
    {
        behavior.Normalize();

        var analysis = selected.Analysis;
        var report = new TuningAssistantReport
        {
            OverallConfidence = SessionConfidence(analysis),
            ConfidenceReason = ConfidenceReason(analysis),
            ProposedCalibration = CloneSuggestion(analysis.CalibrationSuggestion),
            SuggestedBehaviorTarget = CloneBehavior(behavior)
        };

        BuildTransitionAssessment(report, behavior, analysis);
        BuildSelfSteerAssessment(report, behavior, analysis);
        BuildAngleStabilityAssessment(report, behavior, analysis);
        BuildOscillationAssessment(report, behavior, analysis);
        BuildFfbAssessment(report, analysis);

        AddTargetOnlyAssessment(
            report,
            "Front-end bite",
            BehaviorLabel(
                behavior.FrontEndBite,
                "calmer",
                "neutral",
                "more aggressive"),
            "Telemetry v0.7.0 does not yet isolate front tire response from driver steering input strongly enough to auto-correct this axis.");

        AddTargetOnlyAssessment(
            report,
            "Rear grip",
            BehaviorLabel(
                behavior.RearGrip,
                "looser",
                "neutral",
                "more planted"),
            "Rear wheel-slip data is recorded, but absolute slip varies too much by car/tyre model to treat it as a reliable standalone grip target yet.");

        AddTargetOnlyAssessment(
            report,
            "Throttle steering",
            BehaviorLabel(
                behavior.ThrottleSteering,
                "less rotation",
                "neutral",
                "more rotation"),
            "Throttle-to-yaw causality is not isolated strongly enough in this telemetry revision for automatic setup changes.");

        AddTargetOnlyAssessment(
            report,
            "Initiation",
            BehaviorLabel(
                behavior.InitiationSharpness,
                "progressive",
                "neutral",
                "sharper"),
            "Drift entries are counted, but entry rise-time/driver technique separation is not yet strong enough for automatic initiation corrections.");

        ClampSuggestion(report.ProposedCalibration);

        AddCalibrationRecommendations(report);
        AddCarSetupRecommendation(report, behavior);
        AddPreserveRecommendation(report);

        if (previous is not null)
            BuildComparison(report, previous.Analysis, analysis);

        int needsWork =
            report.Assessments.Count(
                x => x.Status == "NEEDS WORK");

        int onTarget =
            report.Assessments.Count(
                x => x.Status == "ON TARGET");

        if (analysis.DriftTimeSeconds < 5)
        {
            report.OverallAssessment =
                "The session is too short for strong tuning conclusions. Atomic will preserve the current tune and recommends recording a longer drift run before applying changes.";
        }
        else if (needsWork == 0)
        {
            report.OverallAssessment =
                $"The measured areas are controlled for the current target. {onTarget} telemetry check(s) are on target; preserve those settings and make only small changes to unmeasured behavior axes.";
        }
        else
        {
            report.OverallAssessment =
                $"Atomic found {needsWork} measured area(s) that do not currently match the desired behavior closely. Recommendations are deliberately small and preserve areas that already look controlled.";
        }

        report.HasSuggestedBehaviorChange =
            !SameBehavior(
                behavior,
                report.SuggestedBehaviorTarget);

        if (report.HasSuggestedBehaviorChange)
        {
            report.SuggestedBehaviorSummary =
                BuildBehaviorChangeSummary(
                    behavior,
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
        TelemetryAnalysis a)
    {
        string desired =
            BehaviorLabel(
                behavior.TransitionSpeed,
                "smooth / slower",
                "balanced",
                "quick");

        if (a.TransitionCount < 2)
        {
            AddAssessment(
                report,
                "Transition speed",
                desired,
                "Not enough complete transitions",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                $"Only {a.TransitionCount} complete transition(s) were detected. Two or more are required before Atomic biases the car setup from transition telemetry.");
            return;
        }

        int observedLevel =
            TransitionLevel(
                a.AverageTransitionSeconds);

        var confidence =
            a.TransitionCount >= 6
                ? AssistantConfidence.High
                : AssistantConfidence.Medium;

        int difference =
            behavior.TransitionSpeed -
            observedLevel;

        var status =
            Math.Abs(difference) == 0
                ? AssistantFindingStatus.OnTarget
                : Math.Abs(difference) == 1
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        AddAssessment(
            report,
            "Transition speed",
            desired,
            $"{TransitionObservedLabel(observedLevel)} ({a.AverageTransitionSeconds:0.00}s crossover)",
            status,
            confidence,
            $"{a.TransitionCount} complete direction changes were detected. Lower crossover time is treated as quicker transition response.");

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
                    Domain = "AC CAR SETUP",
                    Priority = difference >= 2 ? "HIGH" : "MEDIUM",
                    Change = "Bias the AC setup one step toward quicker transitions.",
                    Why = $"Observed transition crossover ({a.AverageTransitionSeconds:0.00}s) is slower than the saved Desired Behavior target. Atomic will hand this to the existing behavior-blending/range-safe setup tuner rather than writing raw setup values directly.",
                    Confidence = confidence.ToString().ToUpperInvariant()
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
                    Domain = "AC CAR SETUP",
                    Priority = "MEDIUM",
                    Change = "Bias the AC setup one step toward smoother transitions.",
                    Why = "The car is transitioning materially quicker than the saved Desired Behavior target.",
                    Confidence = confidence.ToString().ToUpperInvariant()
                });
        }
        else if (status == AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "Transition response is on target; preserve transition-oriented rear platform settings.");
        }
    }

    private static void BuildSelfSteerAssessment(
        TuningAssistantReport report,
        CarBehaviorTarget behavior,
        TelemetryAnalysis a)
    {
        string desired =
            BehaviorLabel(
                behavior.SelfSteerSpeed,
                "slower",
                "balanced",
                "faster");

        if (a.DriftTimeSeconds < 5)
        {
            AddAssessment(
                report,
                "Self-steer speed",
                desired,
                "Not enough sustained drift",
                AssistantFindingStatus.InsufficientData,
                AssistantConfidence.Low,
                "At least several seconds of sustained drift are needed before average steering-return speed is useful.");
            return;
        }

        int observedLevel =
            SelfSteerLevel(
                a.AverageSteeringRateDegPerSec);

        int difference =
            behavior.SelfSteerSpeed -
            observedLevel;

        var confidence =
            a.DriftTimeSeconds >= 20
                ? AssistantConfidence.Medium
                : AssistantConfidence.Low;

        var status =
            Math.Abs(difference) == 0
                ? AssistantFindingStatus.OnTarget
                : Math.Abs(difference) == 1
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        AddAssessment(
            report,
            "Self-steer speed",
            desired,
            $"{SelfSteerObservedLabel(observedLevel)} ({a.AverageSteeringRateDegPerSec:0}°/s average)",
            status,
            confidence,
            $"Average and peak steering rate ({a.PeakSteeringRateDegPerSec:0}°/s) are used as a heuristic. Driver corrections can influence this metric, so confidence is intentionally capped.");

        if (difference >= 1 && a.OscillationEvents <= 1)
        {
            if (report.ProposedCalibration.WheelSpeedDelta <= 0)
                report.ProposedCalibration.WheelSpeedDelta +=
                    difference >= 2 ? 3 : 2;

            if (report.ProposedCalibration.DampingDelta >= 0)
                report.ProposedCalibration.DampingDelta -= 1;

            report.ProposedCalibration.Reasons.Add(
                "Desired self-steer is faster than the measured steering-return heuristic and oscillation is low: allow a small wheel-speed increase with slightly less damping.");
        }
        else if (difference <= -1 || a.PeakSteeringRateDegPerSec > 1000)
        {
            if (report.ProposedCalibration.WheelSpeedDelta >= 0)
                report.ProposedCalibration.WheelSpeedDelta -=
                    difference <= -2 ? 3 : 2;

            if (report.ProposedCalibration.DampingDelta <= 0)
                report.ProposedCalibration.DampingDelta += 1;

            report.ProposedCalibration.Reasons.Add(
                "Measured steering return is faster than the desired target or has a very high peak: add a small amount of control.");
        }
        else if (status == AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "Self-steer rate is near the saved target; preserve wheel-speed/damping balance unless another problem requires a change.");
        }
    }

    private static void BuildAngleStabilityAssessment(
        TuningAssistantReport report,
        CarBehaviorTarget behavior,
        TelemetryAnalysis a)
    {
        string desired =
            BehaviorLabel(
                behavior.AngleStability,
                "lively",
                "balanced",
                "stable");

        if (a.DriftTimeSeconds < 8)
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

        double driftMinutes =
            Math.Max(
                a.DriftTimeSeconds / 60.0,
                0.15);

        double extremePerMinute =
            a.SpinEvents / driftMinutes;

        int observedLevel =
            extremePerMinute switch
            {
                <= 0.30 => 2,
                <= 0.80 => 1,
                <= 1.50 => 0,
                <= 2.50 => -1,
                _ => -2
            };

        int difference =
            behavior.AngleStability -
            observedLevel;

        var status =
            Math.Abs(difference) == 0
                ? AssistantFindingStatus.OnTarget
                : Math.Abs(difference) == 1
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        AddAssessment(
            report,
            "Angle stability",
            desired,
            $"{AngleObservedLabel(observedLevel)} ({extremePerMinute:0.0} extreme-angle events/min)",
            status,
            AssistantConfidence.Medium,
            $"Atomic saw {a.SpinEvents} event(s) above 72° body slip during {a.DriftTimeSeconds:0}s of detected drift. These can include intentional extreme entries, so confidence is capped at MEDIUM.");

        if (difference >= 1)
        {
            report.SuggestedBehaviorTarget.AngleStability =
                Math.Clamp(
                    report.SuggestedBehaviorTarget.AngleStability + 1,
                    -2,
                    2);

            if (report.SuggestedBehaviorTarget.RearGrip < 2 &&
                extremePerMinute >= 1.5)
            {
                report.SuggestedBehaviorTarget.RearGrip++;
            }

            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain = "AC CAR SETUP",
                    Priority = difference >= 2 ? "HIGH" : "MEDIUM",
                    Change = "Bias the setup one step toward more high-angle stability.",
                    Why = "Extreme-angle event rate is higher than the saved stability target. The temporary behavior guidance may also add one rear-grip step when the event rate is high.",
                    Confidence = "MEDIUM"
                });

            if (a.OscillationEvents > 0 || a.PeakSteeringRateDegPerSec > 900)
            {
                report.ProposedCalibration.SpeedDampingDelta += 1;
                report.ProposedCalibration.Reasons.Add(
                    "Angle stability is below target and steering has fast/oscillatory evidence: add a small high-speed damping increment.");
            }
        }
        else if (status == AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "High-angle stability evidence is on target; preserve stability-oriented car setup settings.");
        }
    }

    private static void BuildOscillationAssessment(
        TuningAssistantReport report,
        CarBehaviorTarget behavior,
        TelemetryAnalysis a)
    {
        if (a.DriftTimeSeconds < 5)
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

        double driftMinutes =
            Math.Max(
                a.DriftTimeSeconds / 60.0,
                0.15);

        double perMinute =
            a.OscillationEvents / driftMinutes;

        var status =
            perMinute <= 0.5
                ? AssistantFindingStatus.OnTarget
                : perMinute <= 1.5
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        AddAssessment(
            report,
            "Oscillation control",
            "Low / controlled",
            $"{a.OscillationEvents} cluster(s), {perMinute:0.0}/min",
            status,
            a.DriftTimeSeconds >= 20
                ? AssistantConfidence.High
                : AssistantConfidence.Medium,
            "Fast steering-direction reversals inside short time windows are grouped as oscillation evidence.");

        if (status == AssistantFindingStatus.NeedsWork)
        {
            if (report.ProposedCalibration.DampingDelta < 2)
                report.ProposedCalibration.DampingDelta = 2;

            if (report.ProposedCalibration.SpeedDampingDelta < 2)
                report.ProposedCalibration.SpeedDampingDelta = 2;

            if (report.ProposedCalibration.WheelSpeedDelta > -4)
                report.ProposedCalibration.WheelSpeedDelta = -4;

            report.ProposedCalibration.Reasons.Add(
                "Oscillation rate is high: add wheelbase control and reduce maximum wheel speed slightly before chasing faster self-steer.");
        }
        else if (status == AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "Oscillation control is good; do not add damping solely for stability if the car already meets the target.");
        }
    }

    private static void BuildFfbAssessment(
        TuningAssistantReport report,
        TelemetryAnalysis a)
    {
        if (a.DriftTimeSeconds < 5)
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
            a.FfbClippingPctWhileDrifting < 4
                ? AssistantFindingStatus.OnTarget
                : a.FfbClippingPctWhileDrifting < 8
                    ? AssistantFindingStatus.NearTarget
                    : AssistantFindingStatus.NeedsWork;

        AddAssessment(
            report,
            "FFB headroom",
            "Low sustained clipping",
            $"{a.FfbClippingPctWhileDrifting:0.0}% of drift samples ≥ 98% |FFB|",
            status,
            a.DriftTimeSeconds >= 15
                ? AssistantConfidence.High
                : AssistantConfidence.Medium,
            $"Average absolute FFB during detected drift was {a.AverageFfbAbsWhileDrifting:0.000}. This is a clipping/headroom heuristic, not a force-quality score.");

        if (status == AssistantFindingStatus.OnTarget)
        {
            report.PreserveNotes.Add(
                "FFB headroom looks healthy; preserve AC gain unless the driver reports the wheel is too weak/strong.");
        }
    }

    private static void AddCalibrationRecommendations(
        TuningAssistantReport report)
    {
        var q = report.ProposedCalibration;

        if (q.WheelSpeedDelta != 0 ||
            q.DampingDelta != 0 ||
            q.FrictionDelta != 0 ||
            q.SpeedDampingDelta != 0 ||
            q.TorqueLimitDelta != 0 ||
            q.InterpolationDelta != 0)
        {
            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain = "AZOM / CALIBRATION",
                    Priority =
                        Math.Abs(q.WheelSpeedDelta) >= 5 ||
                        Math.Abs(q.DampingDelta) >= 3
                            ? "HIGH"
                            : "MEDIUM",
                    Change =
                        $"Wheel speed {Signed(q.WheelSpeedDelta)}, damper {Signed(q.DampingDelta)}, friction {Signed(q.FrictionDelta)}, high-speed damping {Signed(q.SpeedDampingDelta)}, torque {Signed(q.TorqueLimitDelta)}, interpolation {Signed(q.InterpolationDelta)}.",
                    Why = "Telemetry evidence is converted into the same bounded Atomic calibration layer already used by the main tuning engine. Applying this does not directly write the wheelbase.",
                    Confidence = report.OverallConfidence.ToString().ToUpperInvariant()
                });
        }

        if (q.AcGainDelta != 0)
        {
            report.Recommendations.Add(
                new AssistantRecommendation
                {
                    Domain = "AC FFB",
                    Priority = Math.Abs(q.AcGainDelta) >= 3 ? "HIGH" : "MEDIUM",
                    Change = $"AC Gain calibration {Signed(q.AcGainDelta)}.",
                    Why = "The telemetry FFB headroom heuristic found sustained output saturation. Atomic adjusts its generated AC gain rather than hiding clipping with unrelated wheelbase settings.",
                    Confidence = report.OverallConfidence.ToString().ToUpperInvariant()
                });
        }
    }

    private static void AddCarSetupRecommendation(
        TuningAssistantReport report,
        CarBehaviorTarget original)
    {
        if (SameBehavior(original, report.SuggestedBehaviorTarget))
            return;

        report.Recommendations.Add(
            new AssistantRecommendation
            {
                Domain = "AC CAR SETUP",
                Priority = "REVIEW",
                Change = "Open the AC Car Setup Tuner with temporary telemetry guidance.",
                Why = "The assistant changes Desired Behavior targets only one step at a time, then lets the existing v0.6.2 behavior-blending and setup.ini range safeguards convert those goals into car-specific setup values. Nothing is saved automatically.",
                Confidence = report.OverallConfidence.ToString().ToUpperInvariant()
            });
    }

    private static void AddPreserveRecommendation(
        TuningAssistantReport report)
    {
        if (report.PreserveNotes.Count == 0)
            return;

        report.Recommendations.Add(
            new AssistantRecommendation
            {
                Domain = "PRESERVE",
                Priority = "KEEP",
                Change = "Keep settings associated with areas that are already on target.",
                Why = string.Join(" ", report.PreserveNotes.Distinct()),
                Confidence = report.OverallConfidence.ToString().ToUpperInvariant()
            });
    }

    private static void BuildComparison(
        TuningAssistantReport report,
        TelemetryAnalysis previous,
        TelemetryAnalysis current)
    {
        report.Comparison.Add(
            Compare(
                "Drift time",
                previous.DriftTimePct,
                current.DriftTimePct,
                "%",
                higherIsBetter: true,
                "More sustained detected drift is usually useful context, but it can also reflect track/session differences."));

        if (previous.TransitionCount > 0 &&
            current.TransitionCount > 0)
        {
            report.Comparison.Add(
                Compare(
                    "Avg transition",
                    previous.AverageTransitionSeconds,
                    current.AverageTransitionSeconds,
                    "s",
                    higherIsBetter: false,
                    "Lower is a quicker low-angle crossover; compare only similar driving/track sections."));
        }

        report.Comparison.Add(
            CompareRate(
                "Oscillation rate",
                EventRate(previous.OscillationEvents, previous.DriftTimeSeconds),
                EventRate(current.OscillationEvents, current.DriftTimeSeconds),
                "/min",
                higherIsBetter: false,
                "Lower fast steering-reversal clustering is generally better."));

        report.Comparison.Add(
            CompareRate(
                "Extreme-angle rate",
                EventRate(previous.SpinEvents, previous.DriftTimeSeconds),
                EventRate(current.SpinEvents, current.DriftTimeSeconds),
                "/min",
                higherIsBetter: false,
                "Lower can indicate more stability, but intentional extreme entries can affect this metric."));

        report.Comparison.Add(
            Compare(
                "FFB clipping",
                previous.FfbClippingPctWhileDrifting,
                current.FfbClippingPctWhileDrifting,
                "%",
                higherIsBetter: false,
                "Lower sustained saturation usually preserves more FFB headroom/detail."));
    }

    private static AssistantComparisonRow Compare(
        string metric,
        double previous,
        double current,
        string unit,
        bool higherIsBetter,
        string note)
    {
        double delta = current - previous;
        bool improved =
            Math.Abs(delta) < 0.0001 ||
            (higherIsBetter
                ? delta > 0
                : delta < 0);

        string interpretation =
            Math.Abs(delta) < 0.01
                ? "Essentially unchanged. " + note
                : (improved
                    ? "Moved in the generally favorable direction. "
                    : "Moved in the generally unfavorable direction. ") + note;

        return new AssistantComparisonRow
        {
            Metric = metric,
            Previous = $"{previous:0.00}{unit}",
            Current = $"{current:0.00}{unit}",
            Change =
                delta >= 0
                    ? $"+{delta:0.00}{unit}"
                    : $"{delta:0.00}{unit}",
            Interpretation = interpretation
        };
    }

    private static AssistantComparisonRow CompareRate(
        string metric,
        double previous,
        double current,
        string unit,
        bool higherIsBetter,
        string note) =>
        Compare(
            metric,
            previous,
            current,
            unit,
            higherIsBetter,
            note);

    private static double EventRate(
        int events,
        double driftSeconds) =>
        driftSeconds <= 0
            ? 0
            : events / Math.Max(
                driftSeconds / 60.0,
                0.15);

    private static AssistantConfidence SessionConfidence(
        TelemetryAnalysis a)
    {
        int score = 0;

        if (a.DriftTimeSeconds >= 45) score += 2;
        else if (a.DriftTimeSeconds >= 15) score += 1;

        if (a.TransitionCount >= 6) score += 2;
        else if (a.TransitionCount >= 2) score += 1;

        if (a.EffectiveSampleRateHz >= 35) score += 1;
        if (a.DriftEntries >= 3) score += 1;

        return score >= 5
            ? AssistantConfidence.High
            : score >= 3
                ? AssistantConfidence.Medium
                : AssistantConfidence.Low;
    }

    private static string ConfidenceReason(
        TelemetryAnalysis a) =>
        $"{a.DriftTimeSeconds:0}s detected drift • {a.TransitionCount} transition(s) • {a.DriftEntries} drift entr{(a.DriftEntries == 1 ? "y" : "ies")} • {a.EffectiveSampleRateHz:0.0} Hz effective sample rate.";

    private static int TransitionLevel(double seconds) =>
        seconds switch
        {
            <= 0.45 => 2,
            <= 0.65 => 1,
            <= 0.90 => 0,
            <= 1.20 => -1,
            _ => -2
        };

    private static string TransitionObservedLabel(int level) =>
        level switch
        {
            2 => "very quick",
            1 => "quick",
            0 => "balanced",
            -1 => "smooth / slower",
            _ => "very slow"
        };

    private static int SelfSteerLevel(double rate) =>
        rate switch
        {
            >= 350 => 2,
            >= 220 => 1,
            >= 130 => 0,
            >= 80 => -1,
            _ => -2
        };

    private static string SelfSteerObservedLabel(int level) =>
        level switch
        {
            2 => "very fast",
            1 => "fast",
            0 => "balanced",
            -1 => "slow",
            _ => "very slow"
        };

    private static string AngleObservedLabel(int level) =>
        level switch
        {
            2 => "very stable evidence",
            1 => "stable evidence",
            0 => "mixed / balanced evidence",
            -1 => "lively / loss-prone evidence",
            _ => "unstable evidence"
        };

    private static string BehaviorLabel(
        int value,
        string negative,
        string neutral,
        string positive) =>
        value switch
        {
            <= -2 => $"strongly {negative}",
            -1 => negative,
            0 => neutral,
            1 => positive,
            _ => $"strongly {positive}"
        };

    private static void AddTargetOnlyAssessment(
        TuningAssistantReport report,
        string behavior,
        string desired,
        string evidence) =>
        AddAssessment(
            report,
            behavior,
            desired,
            "Not isolated reliably from telemetry yet",
            AssistantFindingStatus.TargetOnly,
            AssistantConfidence.Low,
            evidence);

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
                Behavior = behavior,
                Desired = desired,
                Observed = observed,
                Status = StatusText(status),
                Confidence = confidence.ToString().ToUpperInvariant(),
                Evidence = evidence
            });
    }

    private static string StatusText(
        AssistantFindingStatus status) =>
        status switch
        {
            AssistantFindingStatus.OnTarget => "ON TARGET",
            AssistantFindingStatus.NearTarget => "NEAR TARGET",
            AssistantFindingStatus.NeedsWork => "NEEDS WORK",
            AssistantFindingStatus.TargetOnly => "TARGET SAVED",
            _ => "INSUFFICIENT"
        };

    private static TelemetryCalibrationSuggestion CloneSuggestion(
        TelemetryCalibrationSuggestion source) =>
        new()
        {
            TorqueLimitDelta = source.TorqueLimitDelta,
            WheelSpeedDelta = source.WheelSpeedDelta,
            DampingDelta = source.DampingDelta,
            FrictionDelta = source.FrictionDelta,
            SpeedDampingDelta = source.SpeedDampingDelta,
            InterpolationDelta = source.InterpolationDelta,
            AcGainDelta = source.AcGainDelta,
            Reasons = new List<string>(source.Reasons)
        };

    private static CarBehaviorTarget CloneBehavior(
        CarBehaviorTarget source) =>
        new()
        {
            Key = source.Key,
            DisplayName = source.DisplayName,
            UpdatedUtc = source.UpdatedUtc,
            FrontEndBite = source.FrontEndBite,
            RearGrip = source.RearGrip,
            SelfSteerSpeed = source.SelfSteerSpeed,
            TransitionSpeed = source.TransitionSpeed,
            AngleStability = source.AngleStability,
            ThrottleSteering = source.ThrottleSteering,
            InitiationSharpness = source.InitiationSharpness
        };

    private static bool SameBehavior(
        CarBehaviorTarget a,
        CarBehaviorTarget b) =>
        a.FrontEndBite == b.FrontEndBite &&
        a.RearGrip == b.RearGrip &&
        a.SelfSteerSpeed == b.SelfSteerSpeed &&
        a.TransitionSpeed == b.TransitionSpeed &&
        a.AngleStability == b.AngleStability &&
        a.ThrottleSteering == b.ThrottleSteering &&
        a.InitiationSharpness == b.InitiationSharpness;

    private static string BuildBehaviorChangeSummary(
        CarBehaviorTarget before,
        CarBehaviorTarget after)
    {
        var changes = new List<string>();

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
            return;

        changes.Add(
            $"{name} {Signed(before)} → {Signed(after)}");
    }

    private static void ClampSuggestion(
        TelemetryCalibrationSuggestion q)
    {
        q.TorqueLimitDelta =
            Math.Clamp(
                q.TorqueLimitDelta,
                -10,
                10);

        q.WheelSpeedDelta =
            Math.Clamp(
                q.WheelSpeedDelta,
                -10,
                10);

        q.DampingDelta =
            Math.Clamp(
                q.DampingDelta,
                -5,
                5);

        q.FrictionDelta =
            Math.Clamp(
                q.FrictionDelta,
                -3,
                3);

        q.SpeedDampingDelta =
            Math.Clamp(
                q.SpeedDampingDelta,
                -5,
                5);

        q.InterpolationDelta =
            Math.Clamp(
                q.InterpolationDelta,
                -2,
                2);

        q.AcGainDelta =
            Math.Clamp(
                q.AcGainDelta,
                -5,
                2);
    }

    private static string Signed(int value) =>
        value >= 0
            ? $"+{value}"
            : value.ToString();
}
