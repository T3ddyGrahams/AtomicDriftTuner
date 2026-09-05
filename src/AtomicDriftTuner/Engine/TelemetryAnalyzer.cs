using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TelemetryAnalyzer
{
    private const double DriftAngleThresholdDeg =
        10.0;

    private const double DriftSpeedThresholdKmh =
        20.0;

    private const double TransitionStableAngleDeg =
        14.0;

    private const double TransitionCrossoverAngleDeg =
        5.0;

    private const double SpinAngleThresholdDeg =
        72.0;

    private const double SpinSpeedThresholdKmh =
        15.0;

    private const double OscillationSteeringRateThresholdDegPerSec =
        140.0;

    private const double FfbClipThreshold =
        0.98;

    private const double MaximumContinuousSampleGapSeconds =
        0.25;

    private const double MinimumDriftEvidenceSeconds =
        2.0;

    private const double StrongDriftEvidenceSeconds =
        5.0;

    private const double MinimumEntryDurationSeconds =
        0.10;

    private const double MinimumSpinDurationSeconds =
        0.12;

    private const double MinimumTransitionSeconds =
        0.05;

    private const double MaximumTransitionSeconds =
        2.0;

    private const double OscillationFlipWindowSeconds =
        0.55;

    private const double OscillationClusterResetSeconds =
        1.2;

    public TelemetryAnalysis Analyze(
        TelemetrySession session)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        var result =
            new TelemetryAnalysis();

        var samples =
            PrepareSamples(
                session);

        result.SampleCount =
            samples.Count;

        if (samples.Count < 3)
        {
            result.Assessment =
                "Not enough valid telemetry was recorded to analyze the session.";

            result.Findings.Add(
                "Record at least several seconds of driving before analyzing.");

            return result;
        }

        result.DurationSeconds =
            CalculateRecordedDuration(
                samples);

        result.EffectiveSampleRateHz =
            CalculateEffectiveSampleRate(
                samples);

        result.DriftTimeSeconds =
            SumConditionTime(
                samples,
                IsDrifting);

        result.DriftTimePct =
            result.DurationSeconds > 0
                ? Math.Clamp(
                    result.DriftTimeSeconds /
                    result.DurationSeconds *
                    100.0,
                    0,
                    100)
                : 0;

        result.DriftEntries =
            CountQualifiedEvents(
                samples,
                IsDrifting,
                MinimumEntryDurationSeconds);

        var driftSamples =
            samples
                .Where(
                    IsDrifting)
                .ToList();

        if (driftSamples.Count > 0)
        {
            result.AverageDriftAngleDeg =
                Average(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.SlipAngleDeg)));

            result.PeakDriftAngleDeg =
                Maximum(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.SlipAngleDeg)));

            result.AverageSteeringRateDegPerSec =
                Average(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.SteeringRateDegPerSec)));

            result.PeakSteeringRateDegPerSec =
                Maximum(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.SteeringRateDegPerSec)));

            result.AverageYawRateDegPerSec =
                Average(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.YawRateDegPerSec)));

            result.PeakYawRateDegPerSec =
                Maximum(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.YawRateDegPerSec)));

            result.AverageSpeedWhileDriftingKmh =
                Average(
                    driftSamples,
                    x => FiniteOrZero(
                        x.SpeedKmh));

            result.AverageFrontWheelSlipWhileDrifting =
                Average(
                    driftSamples,
                    x => FiniteOrZero(
                        x.FrontWheelSlipAvg));

            result.AverageRearWheelSlipWhileDrifting =
                Average(
                    driftSamples,
                    x => FiniteOrZero(
                        x.RearWheelSlipAvg));

            result.AverageFfbAbsWhileDrifting =
                Average(
                    driftSamples,
                    x => Math.Abs(
                        FiniteOrZero(
                            x.FinalFfb)));

            var validFfbSamples =
                driftSamples
                    .Select(
                        x => Math.Abs(
                            FiniteOrZero(
                                x.FinalFfb)))
                    .ToList();

            result.FfbClippingPctWhileDrifting =
                validFfbSamples.Count > 0
                    ? validFfbSamples.Count(
                          x =>
                              x >=
                              FfbClipThreshold) *
                      100.0 /
                      validFfbSamples.Count
                    : 0;
        }

        var transitions =
            FindTransitions(
                samples);

        result.TransitionCount =
            transitions.Count;

        result.AverageTransitionSeconds =
            transitions.Count > 0
                ? transitions.Average()
                : 0;

        result.OscillationEvents =
            CountOscillations(
                samples);

        result.SpinEvents =
            CountQualifiedEvents(
                samples,
                IsExtremeAngleEvent,
                MinimumSpinDurationSeconds);

        BuildAssessment(
            result);

        BuildSuggestion(
            result);

        return result;
    }

    private static List<TelemetrySample> PrepareSamples(
        TelemetrySession session)
    {
        if (session.Samples is null)
        {
            return [];
        }

        return session.Samples
            .Where(
                sample =>
                    sample is not null &&
                    double.IsFinite(
                        sample.TimeSeconds))
            .OrderBy(
                sample =>
                    sample.TimeSeconds)
            .ToList();
    }

    private static bool IsDrifting(
        TelemetrySample sample)
    {
        var speed =
            FiniteOrZero(
                sample.SpeedKmh);

        var slip =
            FiniteOrZero(
                sample.SlipAngleDeg);

        return
            speed >= DriftSpeedThresholdKmh &&
            Math.Abs(slip) >= DriftAngleThresholdDeg;
    }

    private static bool IsExtremeAngleEvent(
        TelemetrySample sample)
    {
        var speed =
            FiniteOrZero(
                sample.SpeedKmh);

        var slip =
            FiniteOrZero(
                sample.SlipAngleDeg);

        return
            speed >= SpinSpeedThresholdKmh &&
            Math.Abs(slip) >= SpinAngleThresholdDeg;
    }

    private static double CalculateRecordedDuration(
        List<TelemetrySample> samples)
    {
        double total =
            0;

        for (
            var i = 1;
            i < samples.Count;
            i++)
        {
            var dt =
                samples[i].TimeSeconds -
                samples[i - 1].TimeSeconds;

            if (IsContinuousInterval(dt))
            {
                total +=
                    dt;
            }
        }

        return Math.Max(
            0,
            total);
    }

    private static double CalculateEffectiveSampleRate(
        List<TelemetrySample> samples)
    {
        var intervals =
            new List<double>();

        for (
            var i = 1;
            i < samples.Count;
            i++)
        {
            var dt =
                samples[i].TimeSeconds -
                samples[i - 1].TimeSeconds;

            if (IsContinuousInterval(dt))
            {
                intervals.Add(
                    dt);
            }
        }

        if (intervals.Count == 0)
        {
            return 0;
        }

        // Median interval is much less sensitive than whole-session duration
        // to a temporary telemetry disconnect or recording pause.
        intervals.Sort();

        double median;

        var middle =
            intervals.Count /
            2;

        if (intervals.Count % 2 == 0)
        {
            median =
                (
                    intervals[middle - 1] +
                    intervals[middle]
                ) /
                2.0;
        }
        else
        {
            median =
                intervals[middle];
        }

        return median > 0
            ? 1.0 / median
            : 0;
    }

    private static double SumConditionTime(
        List<TelemetrySample> samples,
        Func<TelemetrySample, bool> condition)
    {
        double total =
            0;

        for (
            var i = 1;
            i < samples.Count;
            i++)
        {
            var previous =
                samples[i - 1];

            var current =
                samples[i];

            var dt =
                current.TimeSeconds -
                previous.TimeSeconds;

            if (
                IsContinuousInterval(dt) &&
                condition(previous) &&
                condition(current))
            {
                total +=
                    dt;
            }
        }

        return total;
    }

    private static int CountQualifiedEvents(
        List<TelemetrySample> samples,
        Func<TelemetrySample, bool> condition,
        double minimumDurationSeconds)
    {
        var count =
            0;

        double? eventStart =
            null;

        double? lastQualifyingTime =
            null;

        for (
            var i = 0;
            i < samples.Count;
            i++)
        {
            var sample =
                samples[i];

            if (
                i > 0 &&
                !IsContinuousInterval(
                    sample.TimeSeconds -
                    samples[i - 1].TimeSeconds))
            {
                FinalizeEvent();

                eventStart =
                    null;

                lastQualifyingTime =
                    null;
            }

            if (condition(sample))
            {
                eventStart ??=
                    sample.TimeSeconds;

                lastQualifyingTime =
                    sample.TimeSeconds;
            }
            else
            {
                FinalizeEvent();

                eventStart =
                    null;

                lastQualifyingTime =
                    null;
            }
        }

        FinalizeEvent();

        return count;

        void FinalizeEvent()
        {
            if (
                eventStart is double start &&
                lastQualifyingTime is double end &&
                end - start >= minimumDurationSeconds)
            {
                count++;
            }
        }
    }

    private static List<double> FindTransitions(
        List<TelemetrySample> samples)
    {
        var transitionTimes =
            new List<double>();

        var stableSign =
            0;

        double? transitionStart =
            null;

        for (
            var i = 0;
            i < samples.Count;
            i++)
        {
            var sample =
                samples[i];

            if (
                i > 0 &&
                !IsContinuousInterval(
                    sample.TimeSeconds -
                    samples[i - 1].TimeSeconds))
            {
                stableSign =
                    0;

                transitionStart =
                    null;
            }

            var speed =
                FiniteOrZero(
                    sample.SpeedKmh);

            if (speed < DriftSpeedThresholdKmh)
            {
                continue;
            }

            var slip =
                FiniteOrZero(
                    sample.SlipAngleDeg);

            var absoluteSlip =
                Math.Abs(
                    slip);

            var sign =
                absoluteSlip >= TransitionStableAngleDeg
                    ? Math.Sign(slip)
                    : 0;

            if (
                stableSign == 0 &&
                sign != 0)
            {
                stableSign =
                    sign;

                continue;
            }

            if (
                stableSign != 0 &&
                absoluteSlip <= TransitionCrossoverAngleDeg &&
                transitionStart is null)
            {
                transitionStart =
                    sample.TimeSeconds;
            }

            if (
                transitionStart is double start &&
                sign != 0 &&
                sign != stableSign)
            {
                var duration =
                    sample.TimeSeconds -
                    start;

                if (
                    duration >= MinimumTransitionSeconds &&
                    duration <= MaximumTransitionSeconds)
                {
                    transitionTimes.Add(
                        duration);
                }

                stableSign =
                    sign;

                transitionStart =
                    null;
            }
            else if (
                transitionStart is double old &&
                sample.TimeSeconds -
                old >
                MaximumTransitionSeconds)
            {
                transitionStart =
                    null;

                if (sign != 0)
                {
                    stableSign =
                        sign;
                }
            }
        }

        return transitionTimes;
    }

    private static int CountOscillations(
        List<TelemetrySample> samples)
    {
        var events =
            0;

        var previousRateSign =
            0;

        double? previousFlipTime =
            null;

        double? clusterLastFlipTime =
            null;

        var clusterActive =
            false;

        for (
            var i = 0;
            i < samples.Count;
            i++)
        {
            var sample =
                samples[i];

            if (
                i > 0 &&
                !IsContinuousInterval(
                    sample.TimeSeconds -
                    samples[i - 1].TimeSeconds))
            {
                previousRateSign =
                    0;

                previousFlipTime =
                    null;

                clusterLastFlipTime =
                    null;

                clusterActive =
                    false;
            }

            if (!IsDrifting(sample))
            {
                continue;
            }

            var steeringRate =
                FiniteOrZero(
                    sample.SteeringRateDegPerSec);

            if (
                Math.Abs(steeringRate) <
                OscillationSteeringRateThresholdDegPerSec)
            {
                continue;
            }

            var sign =
                Math.Sign(
                    steeringRate);

            if (sign == 0)
            {
                continue;
            }

            if (
                previousRateSign != 0 &&
                sign != previousRateSign)
            {
                var now =
                    sample.TimeSeconds;

                if (
                    previousFlipTime is double previousFlip &&
                    now - previousFlip <=
                    OscillationFlipWindowSeconds)
                {
                    var startsNewCluster =
                        !clusterActive ||
                        clusterLastFlipTime is null ||
                        now -
                        clusterLastFlipTime.Value >
                        OscillationClusterResetSeconds;

                    if (startsNewCluster)
                    {
                        events++;
                    }

                    clusterActive =
                        true;

                    clusterLastFlipTime =
                        now;
                }
                else if (
                    clusterLastFlipTime is double clusterLast &&
                    now - clusterLast >
                    OscillationClusterResetSeconds)
                {
                    clusterActive =
                        false;
                }

                previousFlipTime =
                    now;
            }

            previousRateSign =
                sign;
        }

        return events;
    }

    private static void BuildAssessment(
        TelemetryAnalysis result)
    {
        if (
            result.DriftTimeSeconds <
            MinimumDriftEvidenceSeconds)
        {
            result.Assessment =
                "Very little sustained drift was detected. Record a longer drift run before applying telemetry-based corrections.";

            result.Findings.Add(
                "Less than 2 seconds met the current drift threshold (20 km/h and 10° body slip angle).");

            return;
        }

        if (result.OscillationEvents >= 4)
        {
            result.Findings.Add(
                "Repeated fast steering reversals suggest wheel oscillation or over-aggressive self-steer.");
        }
        else if (result.OscillationEvents == 0)
        {
            result.Findings.Add(
                "No clear steering-oscillation clusters were detected.");
        }

        if (
            result.PeakSteeringRateDegPerSec >
            900)
        {
            result.Findings.Add(
                "Peak steering return speed is very high; watch for snap transitions or hands-off oscillation.");
        }
        else if (
            result.AverageSteeringRateDegPerSec <
            80 &&
            result.AverageDriftAngleDeg >
            18)
        {
            result.Findings.Add(
                "Steering movement is relatively slow during sustained angle; self-steer may be too damped for this setup.");
        }

        if (result.SpinEvents > 0)
        {
            result.Findings.Add(
                $"Detected {result.SpinEvents} sustained high-angle event(s) above 72° body slip. These can include spins or extreme entries.");
        }

        if (result.TransitionCount > 0)
        {
            result.Findings.Add(
                $"Detected {result.TransitionCount} direction change(s), averaging {result.AverageTransitionSeconds:0.00}s through the low-angle crossover.");
        }
        else
        {
            result.Findings.Add(
                "No complete left-to-right/right-to-left transitions were confidently detected in this recording.");
        }

        if (
            result.FfbClippingPctWhileDrifting >=
            8)
        {
            result.Findings.Add(
                $"FFB output was at or above 98% magnitude for {result.FfbClippingPctWhileDrifting:0.0}% of detected drift samples; AC gain may be clipping sustained detail.");
        }
        else if (
            result.DriftTimeSeconds >=
            StrongDriftEvidenceSeconds)
        {
            result.Findings.Add(
                $"FFB clipping heuristic: {result.FfbClippingPctWhileDrifting:0.0}% of detected drift samples were at or above 98% magnitude.");
        }

        // Stability problems take priority over attempts to make the wheel
        // faster. ADT should control a repeated-loss condition before
        // recommending additional response speed.
        if (result.SpinEvents >= 3)
        {
            result.Assessment =
                "The session shows repeated extreme-angle losses; prioritize stability before speed.";
        }
        else if (result.OscillationEvents >= 4)
        {
            result.Assessment =
                "Self-steer looks aggressive or oscillatory. Add control before adding more wheel speed.";
        }
        else if (
            result.AverageSteeringRateDegPerSec <
            80 &&
            result.AverageDriftAngleDeg >
            18)
        {
            result.Assessment =
                "Self-steer may be slower than ideal for the observed drift angle.";
        }
        else
        {
            result.Assessment =
                "Telemetry looks reasonably controlled. Make small changes and compare another session rather than making a large correction.";
        }
    }

    private static void BuildSuggestion(
        TelemetryAnalysis result)
    {
        var suggestion =
            result.CalibrationSuggestion;

        if (
            result.DriftTimeSeconds <
            MinimumDriftEvidenceSeconds)
        {
            return;
        }

        if (result.OscillationEvents >= 4)
        {
            suggestion.DampingDelta +=
                2;

            suggestion.SpeedDampingDelta +=
                2;

            suggestion.WheelSpeedDelta -=
                5;

            suggestion.Reasons.Add(
                "Oscillation clusters: add wheelbase damping/control and reduce wheel-speed target slightly.");
        }
        else if (
            result.OscillationEvents == 0 &&
            result.AverageSteeringRateDegPerSec <
            80 &&
            result.AverageDriftAngleDeg >
            18)
        {
            suggestion.WheelSpeedDelta +=
                4;

            suggestion.DampingDelta -=
                1;

            suggestion.Reasons.Add(
                "Slow steering movement at sustained angle: allow slightly faster self-steer.");
        }

        if (
            result.PeakSteeringRateDegPerSec >
            1000)
        {
            suggestion.WheelSpeedDelta -=
                3;

            suggestion.DampingDelta +=
                1;

            suggestion.Reasons.Add(
                "Very high peak steering rate: soften the return peak.");
        }

        if (result.SpinEvents >= 3)
        {
            suggestion.SpeedDampingDelta +=
                2;

            suggestion.FrictionDelta +=
                1;

            suggestion.Reasons.Add(
                "Repeated extreme-angle events: add a small amount of stability.");
        }

        if (
            result.FfbClippingPctWhileDrifting >=
            8)
        {
            suggestion.AcGainDelta -=
                3;

            suggestion.Reasons.Add(
                "Sustained FFB saturation: reduce AC gain slightly to recover force detail/headroom.");
        }
        else if (
            result.FfbClippingPctWhileDrifting >=
            4)
        {
            suggestion.AcGainDelta -=
                1;

            suggestion.Reasons.Add(
                "Moderate FFB saturation: make a small AC gain reduction and compare another session.");
        }

        suggestion.WheelSpeedDelta =
            Math.Clamp(
                suggestion.WheelSpeedDelta,
                -10,
                10);

        suggestion.DampingDelta =
            Math.Clamp(
                suggestion.DampingDelta,
                -4,
                5);

        suggestion.FrictionDelta =
            Math.Clamp(
                suggestion.FrictionDelta,
                -3,
                3);

        suggestion.SpeedDampingDelta =
            Math.Clamp(
                suggestion.SpeedDampingDelta,
                -3,
                5);

        suggestion.AcGainDelta =
            Math.Clamp(
                suggestion.AcGainDelta,
                -5,
                2);
    }

    private static bool IsContinuousInterval(
        double deltaSeconds)
    {
        return
            double.IsFinite(deltaSeconds) &&
            deltaSeconds > 0 &&
            deltaSeconds <
            MaximumContinuousSampleGapSeconds;
    }

    private static double Average(
        IEnumerable<TelemetrySample> samples,
        Func<TelemetrySample, double> selector)
    {
        double total =
            0;

        var count =
            0;

        foreach (var sample in samples)
        {
            var value =
                selector(
                    sample);

            if (!double.IsFinite(value))
            {
                continue;
            }

            total +=
                value;

            count++;
        }

        return count > 0
            ? total / count
            : 0;
    }

    private static double Maximum(
        IEnumerable<TelemetrySample> samples,
        Func<TelemetrySample, double> selector)
    {
        var maximum =
            0.0;

        foreach (var sample in samples)
        {
            var value =
                selector(
                    sample);

            if (!double.IsFinite(value))
            {
                continue;
            }

            if (value > maximum)
            {
                maximum =
                    value;
            }
        }

        return maximum;
    }

    private static double FiniteOrZero(
        double value)
    {
        return double.IsFinite(value)
            ? value
            : 0;
    }
}
