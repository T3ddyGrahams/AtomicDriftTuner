using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class TelemetryAnalyzer
{
    private const double DriftAngleThreshold = 10.0;
    private const double DriftSpeedThreshold = 20.0;

    public TelemetryAnalysis Analyze(TelemetrySession session)
    {
        var result = new TelemetryAnalysis { SampleCount = session.Samples.Count };
        if (session.Samples.Count < 3)
        {
            result.Assessment = "Not enough telemetry was recorded to analyze the session.";
            result.Findings.Add("Record at least several seconds of driving before analyzing.");
            return result;
        }

        var s = session.Samples.OrderBy(x => x.TimeSeconds).ToList();
        result.DurationSeconds = Math.Max(0, s[^1].TimeSeconds - s[0].TimeSeconds);
        result.EffectiveSampleRateHz = result.DurationSeconds > 0 ? (s.Count - 1) / result.DurationSeconds : 0;

        var drift = s.Where(IsDrifting).ToList();
        result.DriftTimeSeconds = SumConditionTime(s, IsDrifting);
        result.DriftTimePct = result.DurationSeconds > 0 ? result.DriftTimeSeconds / result.DurationSeconds * 100 : 0;
        result.DriftEntries = CountEntries(s, IsDrifting);

        if (drift.Count > 0)
        {
            result.AverageDriftAngleDeg = drift.Average(x => Math.Abs(x.SlipAngleDeg));
            result.PeakDriftAngleDeg = drift.Max(x => Math.Abs(x.SlipAngleDeg));
            result.AverageSteeringRateDegPerSec = drift.Average(x => Math.Abs(x.SteeringRateDegPerSec));
            result.PeakSteeringRateDegPerSec = drift.Max(x => Math.Abs(x.SteeringRateDegPerSec));
            result.AverageYawRateDegPerSec = drift.Average(x => Math.Abs(x.YawRateDegPerSec));
            result.PeakYawRateDegPerSec = drift.Max(x => Math.Abs(x.YawRateDegPerSec));
            result.AverageSpeedWhileDriftingKmh = drift.Average(x => x.SpeedKmh);
            result.AverageFrontWheelSlipWhileDrifting = drift.Average(x => x.FrontWheelSlipAvg);
            result.AverageRearWheelSlipWhileDrifting = drift.Average(x => x.RearWheelSlipAvg);
            result.AverageFfbAbsWhileDrifting = drift.Average(x => Math.Abs(x.FinalFfb));
            result.FfbClippingPctWhileDrifting =
                drift.Count == 0
                    ? 0
                    : drift.Count(x => Math.Abs(x.FinalFfb) >= 0.98) * 100.0 / drift.Count;
        }

        var transitions = FindTransitions(s);
        result.TransitionCount = transitions.Count;
        result.AverageTransitionSeconds = transitions.Count > 0 ? transitions.Average() : 0;
        result.OscillationEvents = CountOscillations(s);
        result.SpinEvents = CountEvents(s, x => x.SpeedKmh >= 15 && Math.Abs(x.SlipAngleDeg) >= 72);

        BuildAssessment(result);
        BuildSuggestion(result);
        return result;
    }

    private static bool IsDrifting(TelemetrySample x) =>
        x.SpeedKmh >= DriftSpeedThreshold && Math.Abs(x.SlipAngleDeg) >= DriftAngleThreshold;

    private static double SumConditionTime(List<TelemetrySample> s, Func<TelemetrySample, bool> condition)
    {
        double total = 0;
        for (int i = 1; i < s.Count; i++)
        {
            double dt = s[i].TimeSeconds - s[i - 1].TimeSeconds;
            if (dt > 0 && dt < 0.25 && condition(s[i]) && condition(s[i - 1])) total += dt;
        }
        return total;
    }

    private static int CountEntries(List<TelemetrySample> s, Func<TelemetrySample, bool> condition)
    {
        int count = 0;
        bool prev = false;
        foreach (var sample in s)
        {
            bool now = condition(sample);
            if (now && !prev) count++;
            prev = now;
        }
        return count;
    }

    private static int CountEvents(List<TelemetrySample> s, Func<TelemetrySample, bool> condition)
    {
        int count = 0;
        bool active = false;
        foreach (var x in s)
        {
            bool now = condition(x);
            if (now && !active) count++;
            active = now;
        }
        return count;
    }

    private static List<double> FindTransitions(List<TelemetrySample> s)
    {
        var times = new List<double>();
        int stableSign = 0;
        double? transitionStart = null;

        foreach (var x in s)
        {
            if (x.SpeedKmh < 20) continue;
            int sign = Math.Abs(x.SlipAngleDeg) >= 14 ? Math.Sign(x.SlipAngleDeg) : 0;
            if (stableSign == 0 && sign != 0) { stableSign = sign; continue; }

            if (stableSign != 0 && Math.Abs(x.SlipAngleDeg) <= 5 && transitionStart is null)
                transitionStart = x.TimeSeconds;

            if (transitionStart is double start && sign != 0 && sign != stableSign)
            {
                double dt = x.TimeSeconds - start;
                if (dt is >= 0.05 and <= 2.0) times.Add(dt);
                stableSign = sign;
                transitionStart = null;
            }
            else if (transitionStart is double old && x.TimeSeconds - old > 2.0)
            {
                transitionStart = null;
                if (sign != 0) stableSign = sign;
            }
        }
        return times;
    }

    private static int CountOscillations(List<TelemetrySample> s)
    {
        int events = 0;
        int lastSign = 0;
        double lastFlip = -10;
        bool clusterActive = false;
        double clusterLast = -10;

        foreach (var x in s)
        {
            if (!IsDrifting(x) || Math.Abs(x.SteeringRateDegPerSec) < 140) continue;
            int sign = Math.Sign(x.SteeringRateDegPerSec);
            if (lastSign != 0 && sign != lastSign)
            {
                double gap = x.TimeSeconds - lastFlip;
                if (gap <= 0.55)
                {
                    if (!clusterActive || x.TimeSeconds - clusterLast > 1.2) events++;
                    clusterActive = true;
                    clusterLast = x.TimeSeconds;
                }
                else if (x.TimeSeconds - clusterLast > 1.2)
                    clusterActive = false;
                lastFlip = x.TimeSeconds;
            }
            else if (lastSign == 0)
                lastFlip = x.TimeSeconds;
            lastSign = sign;
        }
        return events;
    }

    private static void BuildAssessment(TelemetryAnalysis r)
    {
        if (r.DriftTimeSeconds < 2)
        {
            r.Assessment = "Very little sustained drift was detected. Record a longer drift run before applying telemetry-based corrections.";
            r.Findings.Add("Less than 2 seconds met the current drift threshold (20 km/h and 10° body slip angle)." );
            return;
        }

        if (r.OscillationEvents >= 4)
            r.Findings.Add("Repeated fast steering reversals suggest wheel oscillation or over-aggressive self-steer.");
        else if (r.OscillationEvents == 0)
            r.Findings.Add("No clear steering-oscillation clusters were detected.");

        if (r.PeakSteeringRateDegPerSec > 900)
            r.Findings.Add("Peak steering return speed is very high; watch for snap transitions or hands-off oscillation.");
        else if (r.AverageSteeringRateDegPerSec < 80 && r.AverageDriftAngleDeg > 18)
            r.Findings.Add("Steering movement is relatively slow during sustained angle; self-steer may be too damped for this setup.");

        if (r.SpinEvents > 0)
            r.Findings.Add($"Detected {r.SpinEvents} high-angle event(s) above 72° body slip. These can include spins or extreme entries.");

        if (r.TransitionCount > 0)
            r.Findings.Add($"Detected {r.TransitionCount} direction change(s), averaging {r.AverageTransitionSeconds:0.00}s through the low-angle crossover.");
        else
            r.Findings.Add("No complete left-to-right/right-to-left transitions were confidently detected in this recording.");

        if (r.FfbClippingPctWhileDrifting >= 8)
            r.Findings.Add($"FFB output was at or above 98% magnitude for {r.FfbClippingPctWhileDrifting:0.0}% of detected drift samples; AC gain may be clipping sustained detail.");
        else if (r.DriftTimeSeconds >= 5)
            r.Findings.Add($"FFB clipping heuristic: {r.FfbClippingPctWhileDrifting:0.0}% of detected drift samples were at or above 98% magnitude.");

        r.Assessment = r.OscillationEvents >= 4 ? "Self-steer looks aggressive/oscillatory. Add control before adding more wheel speed." :
                       r.AverageSteeringRateDegPerSec < 80 && r.AverageDriftAngleDeg > 18 ? "Self-steer may be slower than ideal for the observed drift angle." :
                       r.SpinEvents >= 3 ? "The session shows repeated extreme-angle losses; prioritize stability before speed." :
                       "Telemetry looks reasonably controlled. Make small changes and compare another session rather than making a large correction.";
    }

    private static void BuildSuggestion(TelemetryAnalysis r)
    {
        var q = r.CalibrationSuggestion;
        if (r.DriftTimeSeconds < 2) return;

        if (r.OscillationEvents >= 4)
        {
            q.DampingDelta += 2;
            q.SpeedDampingDelta += 2;
            q.WheelSpeedDelta -= 5;
            q.Reasons.Add("Oscillation clusters: add wheelbase damping/control and reduce wheel-speed target slightly.");
        }
        else if (r.OscillationEvents == 0 && r.AverageSteeringRateDegPerSec < 80 && r.AverageDriftAngleDeg > 18)
        {
            q.WheelSpeedDelta += 4;
            q.DampingDelta -= 1;
            q.Reasons.Add("Slow steering movement at sustained angle: allow slightly faster self-steer.");
        }

        if (r.PeakSteeringRateDegPerSec > 1000)
        {
            q.WheelSpeedDelta -= 3;
            q.DampingDelta += 1;
            q.Reasons.Add("Very high peak steering rate: soften the return peak.");
        }

        if (r.SpinEvents >= 3)
        {
            q.SpeedDampingDelta += 2;
            q.FrictionDelta += 1;
            q.Reasons.Add("Repeated extreme-angle events: add a small amount of stability.");
        }

        if (r.FfbClippingPctWhileDrifting >= 8)
        {
            q.AcGainDelta -= 3;
            q.Reasons.Add("Sustained FFB saturation: reduce AC gain slightly to recover force detail/headroom.");
        }
        else if (r.FfbClippingPctWhileDrifting >= 4)
        {
            q.AcGainDelta -= 1;
            q.Reasons.Add("Moderate FFB saturation: make a small AC gain reduction and compare another session.");
        }

        q.WheelSpeedDelta = Math.Clamp(q.WheelSpeedDelta, -10, 10);
        q.DampingDelta = Math.Clamp(q.DampingDelta, -4, 5);
        q.FrictionDelta = Math.Clamp(q.FrictionDelta, -3, 3);
        q.SpeedDampingDelta = Math.Clamp(q.SpeedDampingDelta, -3, 5);
        q.AcGainDelta = Math.Clamp(q.AcGainDelta, -5, 2);
    }
}
