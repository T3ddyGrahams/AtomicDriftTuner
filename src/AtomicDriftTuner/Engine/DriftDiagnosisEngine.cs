using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

/// <summary>Time-weighted phase observations; not tire-force or hands-off measurements.</summary>
public sealed class DriftDiagnosisEngine
{
    private sealed record Frame(TelemetrySample Sample, double Dt);
    public TelemetryAnalysis Analyze(TelemetrySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var r = new TelemetryAnalysis();
        var d = r.Diagnosis;
        var blocks = new List<List<Frame>>();
        var block = new List<Frame>();
        TelemetrySample? previous = null;
        double collisionUntil = double.NegativeInfinity;
        void Break() { if (block.Count > 0) blocks.Add(block); block = []; }
        foreach (var s in session.Samples)
        {
            if (s is null || !Valid(s)) { d.InvalidSamples++; Break(); previous = null; continue; }
            var dt = previous is null ? 0 : s.TimeSeconds - previous.TimeSeconds;
            if (previous is not null && (dt < 0 || s.PacketId < previous.PacketId))
            {
                d.TimelineReset = true; d.Discontinuities++; Break();
                // A restart would put two different driving episodes on the same timeline.
                // Retain only the first episode for inspection, never for attribution.
                break;
            }
            if (previous is not null && (dt <= 0 || dt > .25 || s.PacketId < previous.PacketId || (s.PacketId != 0 && s.PacketId == previous.PacketId)))
            { d.Discontinuities++; Break(); dt = 0; }
            if (dt > 0) r.DurationSeconds += dt;
            if (dt > 0 && s.HasExtendedSignals && previous?.HasExtendedSignals == true && s.DamageTotal > previous.DamageTotal + .01)
                collisionUntil = s.TimeSeconds + 1;
            bool excluded = (s.HasExtendedSignals && (s.PitLimiterOn || s.IsAiControlled || s.Gear == 0 || s.WheelsOutsideTrack >= 4)) ||
                s.TimeSeconds < collisionUntil || Math.Abs(s.LateralG) > 5 || Math.Abs(s.LongitudinalG) > 5;
            if (excluded) { d.ExcludedSeconds += dt; Break(); previous = s; continue; }
            // A first frame never attributes the preceding excluded interval to valid driving.
            block.Add(new Frame(s, block.Count == 0 ? 0 : dt)); previous = s;
        }
        Break();
        var frames = blocks.SelectMany(x => x).ToList();
        r.SampleCount = frames.Count;
        r.EffectiveSampleRateHz = Time(frames) > 0 ? frames.Count(f => f.Dt > 0) / Time(frames) : 0;
        var drift = frames.Where(f => Drifting(f.Sample)).ToList();
        r.DriftTimeSeconds = Time(drift);
        r.DriftTimePct = r.DurationSeconds > 0 ? 100 * r.DriftTimeSeconds / r.DurationSeconds : 0;
        d.UsableDriftSeconds = r.DriftTimeSeconds;
        d.LeftDriftSeconds = drift.Where(f => f.Sample.SlipAngleDeg < 0).Sum(f => f.Dt);
        d.RightDriftSeconds = drift.Where(f => f.Sample.SlipAngleDeg > 0).Sum(f => f.Dt);
        d.LowSpeedSeconds = drift.Where(f => f.Sample.SpeedKmh < 50).Sum(f => f.Dt);
        d.MediumSpeedSeconds = drift.Where(f => f.Sample.SpeedKmh >= 50 && f.Sample.SpeedKmh < 90).Sum(f => f.Dt);
        d.HighSpeedSeconds = drift.Where(f => f.Sample.SpeedKmh >= 90).Sum(f => f.Dt);
        r.AverageDriftAngleDeg = Mean(drift, s => Math.Abs(s.SlipAngleDeg));
        r.PeakDriftAngleDeg = Peak(drift, s => Math.Abs(s.SlipAngleDeg));
        r.AverageSteeringRateDegPerSec = Mean(drift, s => Math.Abs(s.SteeringRateDegPerSec));
        r.PeakSteeringRateDegPerSec = Peak(drift, s => Math.Abs(s.SteeringRateDegPerSec));
        r.AverageYawRateDegPerSec = Mean(drift, s => Math.Abs(s.YawRateDegPerSec));
        r.PeakYawRateDegPerSec = Peak(drift, s => Math.Abs(s.YawRateDegPerSec));
        r.AverageSpeedWhileDriftingKmh = Mean(drift, s => s.SpeedKmh);
        r.AverageFrontWheelSlipWhileDrifting = Mean(drift, s => Math.Abs(s.FrontWheelSlipAvg));
        r.AverageRearWheelSlipWhileDrifting = Mean(drift, s => Math.Abs(s.RearWheelSlipAvg));
        r.AverageFfbAbsWhileDrifting = Mean(drift, s => Math.Abs(s.FinalFfb));
        r.FfbClippingPctWhileDrifting = Mean(drift, s => Math.Abs(s.FinalFfb) >= .98 ? 100 : 0);
        foreach (var continuous in blocks) FindPhases(continuous, d.Events);
        var entries = d.Events.Where(e => e.Phase == "Initiation").ToList();
        var transitions = d.Events.Where(e => e.Phase == "Transition").ToList();
        r.DriftEntries = entries.Count; r.TransitionCount = transitions.Count;
        r.AverageTransitionSeconds = transitions.Count > 0 ? transitions.Average(e => e.DurationSeconds) : 0;
        r.SpinEvents = d.Events.Count(e => e.Phase == "Extreme angle");
        var windows = entries.Concat(transitions).OrderBy(e => e.StartSeconds).ToList();
        var steady = new List<Frame>();
        double variationSum = 0, variationTime = 0;
        foreach (var continuous in blocks)
        {
            Frame? last = null;
            int lastRateSign = 0, flips = 0, phaseIndex = 0;
            double lastFlip = -100, clusterStart = 0, lastFlipAngle = 0;
            bool cluster = false;
            foreach (var f in continuous)
            {
                var s = f.Sample;
                while (phaseIndex < windows.Count && windows[phaseIndex].EndSeconds + .4 < s.TimeSeconds) phaseIndex++;
                bool inPhase = phaseIndex < windows.Count && windows[phaseIndex].StartSeconds - .4 <= s.TimeSeconds;
                if (!Drifting(s) || Math.Abs(s.SlipAngleDeg) < 20 || inPhase)
                { last = null; lastRateSign = 0; flips = 0; cluster = false; continue; }
                steady.Add(f);
                if (last is not null && f.Dt > 0 && Math.Sign(s.SlipAngleDeg) == Math.Sign(last.Sample.SlipAngleDeg))
                { variationSum += Math.Abs(s.SlipAngleDeg - last.Sample.SlipAngleDeg); variationTime += f.Dt; }
                last = f;
                if (s.TimeSeconds - lastFlip > .6) { flips = 0; cluster = false; }
                if (Math.Abs(s.SteeringRateDegPerSec) < 140) continue;
                var sign = Math.Sign(s.SteeringRateDegPerSec);
                if (lastRateSign != 0 && sign != lastRateSign && Math.Abs(s.SteeringAngleDeg - lastFlipAngle) >= 3)
                {
                    if (flips == 0) clusterStart = s.TimeSeconds;
                    flips++;
                    if (flips >= 4 && !cluster)
                    {
                        d.Events.Add(new DriftEvent { Phase = "Steering oscillation proxy", StartSeconds = clusterStart, EndSeconds = s.TimeSeconds,
                            SpeedKmh = s.SpeedKmh, Evidence = "Four rapid steering-rate reversals outside entry/transition windows. Driver corrections can produce the same pattern." });
                        cluster = true;
                    }
                    lastFlip = s.TimeSeconds; lastFlipAngle = s.SteeringAngleDeg;
                }
                lastRateSign = sign;
            }
        }
        d.Events = d.Events.OrderBy(e => e.StartSeconds).ToList();
        r.OscillationEvents = d.Events.Count(e => e.Phase == "Steering oscillation proxy");
        var slip = steady.Where(f => Math.Abs(f.Sample.FrontWheelSlipAvg) + Math.Abs(f.Sample.RearWheelSlipAvg) > .01).ToList();
        var front = frames.Where(f => f.Sample.SpeedKmh >= 30 && Math.Abs(f.Sample.SlipAngleDeg) <= 8 && Math.Abs(f.Sample.SteeringAngleDeg) is >= 12 and <= 120).ToList();
        var response = new List<Frame>();
        foreach (var continuous in blocks)
        {
            int index = 0;
            foreach (var f in continuous)
            {
                while (index < transitions.Count && transitions[index].EndSeconds < f.Sample.TimeSeconds) index++;
                if (index < transitions.Count && transitions[index].StartSeconds <= f.Sample.TimeSeconds) response.Add(f);
            }
        }
        var powered = steady.Where(f => f.Sample.Throttle >= .7).ToList();
        void Metric(string key, string name, double? value, string unit, double seconds, int events, string evidence, bool proxy = true)
        {
            d.Metrics.Add(new RunMetric { Key = key, Name = name, Value = value, Unit = unit, EvidenceSeconds = seconds, Events = events, Evidence = evidence,
                Confidence = value is null || seconds < 10 || (events >= 0 && events < 3) ? "LOW" : !proxy && seconds >= 40 && (events < 0 || events >= 8) ? "HIGH" : "MEDIUM" });
        }
        Metric("initiation", "Initiation rise time", entries.Count >= 3 ? Median(entries.Select(e => e.DurationSeconds)) : null, "s", r.DriftTimeSeconds, entries.Count,
            "Median 5° to sustained 20° body-slip rise; technique clues do not prove clutch-kick or handbrake use.", false);
        Metric("transition", "Transition crossover time", transitions.Count >= 3 ? Median(transitions.Select(e => e.DurationSeconds)) : null, "s", r.DriftTimeSeconds, transitions.Count,
            "Median departure below 15° to sustained opposite 20°; false starts and incomplete reversals are excluded.", false);
        Metric("front-response", "Front response proxy", Time(front) >= 5 ? Mean(front, s => Math.Abs(s.YawRateDegPerSec) / (Math.Abs(s.SteeringAngleDeg) + 5)) : null,
            "yaw/steer", Time(front), -1, "Low-body-slip yaw response per steering input. Steering ratio, speed, line and driver input confound front-grip diagnosis; compare only this car.");
        Metric("front-slip-share", "Front / rear slip balance", Time(slip) >= 5 ? Mean(slip, FrontShare) : null, "% front", Time(slip), -1,
            "Relative axle wheel-slip share in sustained drift. It is not tire grip or a universal 50/50 target; normal drifting often has rear-biased slip.");
        Metric("rear-slip-share", "Rear slip share proxy", Time(slip) >= 5 ? 100 - Mean(slip, FrontShare) : null, "% rear", Time(slip), -1,
            "Lower share may accompany a more planted rear on comparable runs; throttle, tire model and load also change wheel slip.");
        Metric("self-steer", "Steering response proxy", transitions.Count >= 3 ? Mean(response, s => Math.Abs(s.SteeringRateDegPerSec)) : null, "°/s", Time(response), transitions.Count,
            "Steering movement during transitions. AC does not measure driver hand torque, so this cannot isolate hands-off self-steer.");
        Metric("stability", "Sustained-angle variation", variationTime >= 8 ? variationSum / variationTime : null, "°/s", variationTime, -1,
            "Time-normalized angle movement outside entries and transitions. Changing track curvature can also increase this proxy.");
        Metric("oscillation", "Steering oscillation clusters", Time(steady) >= 8 ? r.OscillationEvents * 60 / Time(steady) : null, "/min", Time(steady), -1,
            "Cluster rate in sustained drift; entry/transition steering reversals are not counted.");
        Metric("extreme-angle", "Extreme-angle events", r.DriftTimeSeconds >= 8 ? r.SpinEvents * 60 / r.DriftTimeSeconds : null, "/min", r.DriftTimeSeconds, -1,
            "Sustained ≥72° body slip. This is a loss/extreme-entry proxy, not a confirmed spin.");
        Metric("throttle-rotation", "Powered rotation proxy", Time(powered) >= 5 ? Mean(powered, s => Math.Abs(s.YawRateDegPerSec)) : null, "°/s", Time(powered), -1,
            "Yaw in sustained drift above 70% throttle. An association, not a measured throttle-to-yaw causal effect.");
        Metric("clipping", "FFB saturation", r.DriftTimeSeconds >= 8 ? r.FfbClippingPctWhileDrifting : null, "%", r.DriftTimeSeconds, -1,
            "Time at ≥98% FFB magnitude during usable drift.", false);
        Metric("throttle", "Drift throttle context", drift.Count > 0 ? Mean(drift, s => s.Throttle) : null, "0–1", r.DriftTimeSeconds, -1,
            "Used to reject substantially different driving inputs.");
        d.QualityNotes.Add($"{d.InvalidSamples} invalid frames; {d.Discontinuities} continuity breaks; {d.ExcludedSeconds:0.0}s excluded (pit limiter, AI/reverse, off-track or impact evidence).");
        if (!session.Samples.Any(s => s?.HasExtendedSignals == true)) d.QualityNotes.Add("Legacy recording: pit limiter, AI, off-track and damage signals were not captured.");
        if (session.Context?.Interrupted == true) d.QualityNotes.Add("Recording was interrupted; improvement attribution is disabled.");
        if (d.TimelineReset) d.QualityNotes.Add("Recording time or packet sequence restarted. Later frames were ignored; record a fresh uninterrupted run.");
        r.Findings.AddRange(d.QualityNotes);
        r.Findings.AddRange(d.Metrics.Select(m => $"{m.Name}: {m.DisplayValue}. {m.Evidence}"));
        r.Assessment = r.DriftTimeSeconds < 10 ? "Insufficient clean drift evidence. Record several entries and both transition directions." :
            $"Analyzed {entries.Count} initiation(s), {transitions.Count} transition(s) and {Time(steady):0.0}s of sustained drift. Review the evidence against this car's Desired Behavior.";
        // Steering is driver-influenced. Only measured FFB saturation gets an automatic calibration delta.
        if (r.DriftTimeSeconds >= 15 && Reliable(session, r) && r.FfbClippingPctWhileDrifting >= 4)
        {
            r.CalibrationSuggestion.AcGainDelta = r.FfbClippingPctWhileDrifting >= 8 ? -3 : -1;
            r.CalibrationSuggestion.Reasons.Add("Sustained FFB saturation: test a small AC gain reduction and compare another run.");
        }
        return r;
    }

    public static bool Reliable(TelemetrySession session, TelemetryAnalysis analysis) =>
        !analysis.Diagnosis.TimelineReset && session.Context?.Interrupted != true && analysis.EffectiveSampleRateHz >= 15 &&
        analysis.Diagnosis.InvalidSamples <= session.Samples.Count * .1 &&
        analysis.Diagnosis.Discontinuities <= Math.Max(1, analysis.DurationSeconds / 10);

    private static void FindPhases(List<Frame> frames, List<DriftEvent> events)
    {
        double lowSince = -1, entryStart = -1, reached = -1, transitionStart = -1, oppositeSince = -1, extremeSince = -1;
        int stableSign = 0, pendingSign = 0;
        double pendingSince = -1;
        bool extremeReported = false;
        TelemetrySample? entryFirst = null;
        foreach (var f in frames)
        {
            var s = f.Sample; var t = s.TimeSeconds; var angle = Math.Abs(s.SlipAngleDeg); var sign = Math.Sign(s.SlipAngleDeg);
            if (s.SpeedKmh >= 15 && angle >= 72)
            {
                if (extremeSince < 0) extremeSince = t;
                if (!extremeReported && t - extremeSince >= .2)
                {
                    events.Add(new DriftEvent { Phase = "Extreme angle", StartSeconds = extremeSince, EndSeconds = t, SpeedKmh = s.SpeedKmh, Evidence = "Sustained body slip at or above 72°." });
                    extremeReported = true;
                }
            }
            else { extremeSince = -1; extremeReported = false; }
            if (s.SpeedKmh < 20 || angle >= 72)
            { lowSince = entryStart = reached = transitionStart = oppositeSince = pendingSince = -1; stableSign = pendingSign = 0; continue; }
            if (angle < 5)
            {
                if (lowSince < 0) lowSince = t;
                if (t - lowSince > 4) stableSign = 0;
                if (entryStart >= 0) { entryStart = reached = -1; entryFirst = null; }
            }
            else
            {
                if (entryStart < 0 && lowSince >= 0 && t - lowSince >= .35 && stableSign == 0) { entryStart = t; entryFirst = s; }
                lowSince = -1;
            }
            if (entryStart >= 0)
            {
                if (angle >= 20) { if (reached < 0) reached = t; } else reached = -1;
                if (reached >= 0 && t - reached >= .2)
                {
                    if (reached - entryStart >= .05 && reached - entryStart <= 4)
                        events.Add(new DriftEvent { Phase = "Initiation", StartSeconds = entryStart, EndSeconds = reached, SpeedKmh = s.SpeedKmh,
                            Evidence = Math.Abs(s.Clutch - entryFirst!.Clutch) > .4 ? "Clutch-input change accompanied angle build-up; technique not confirmed." :
                                s.Brake > .4 || entryFirst!.Brake > .4 ? "Brake input accompanied angle build-up; no separate handbrake signal is available." :
                                s.Throttle - entryFirst!.Throttle > .2 ? "Throttle rise accompanied angle build-up." : "Angle build-up without a distinct recorded pedal trigger." });
                    entryStart = reached = -1; stableSign = sign;
                }
                else if (t - entryStart > 4) entryStart = reached = -1;
            }
            if (stableSign == 0 && angle >= 20)
            {
                if (pendingSign != sign) { pendingSign = sign; pendingSince = t; }
                if (t - pendingSince >= .2) stableSign = sign;
            }
            else if (stableSign == 0) { pendingSign = 0; pendingSince = -1; }
            if (stableSign != 0 && angle < 15 && transitionStart < 0) transitionStart = t;
            if (transitionStart < 0) continue;
            if (sign == stableSign && angle >= 20) { transitionStart = oppositeSince = -1; continue; }
            if (sign != stableSign && angle >= 20)
            {
                if (oppositeSince < 0) oppositeSince = t;
                if (t - oppositeSince >= .2)
                {
                    if (oppositeSince - transitionStart >= .05 && oppositeSince - transitionStart <= 4)
                        events.Add(new DriftEvent { Phase = "Transition", StartSeconds = transitionStart, EndSeconds = oppositeSince, SpeedKmh = s.SpeedKmh,
                            Evidence = stableSign < 0 ? "Left-to-right sustained direction change." : "Right-to-left sustained direction change." });
                    stableSign = sign; transitionStart = oppositeSince = -1;
                }
            }
            else oppositeSince = -1;
            if (transitionStart >= 0 && t - transitionStart > 4) { stableSign = pendingSign = 0; transitionStart = oppositeSince = -1; }
        }
    }

    private static bool Valid(TelemetrySample s) => !s.InvalidSourceSignals && double.IsFinite(s.TimeSeconds) && s.TimeSeconds >= 0 &&
        new[] { s.SpeedKmh, s.SlipAngleDeg, s.SteeringAngleDeg, s.SteeringRateDegPerSec, s.YawRateDegPerSec,
            s.Throttle, s.Brake, s.Clutch, s.FinalFfb, s.FrontWheelSlipAvg, s.RearWheelSlipAvg, s.LateralG, s.LongitudinalG, s.DamageTotal }.All(double.IsFinite) &&
        s.SpeedKmh is >= 0 and <= 500 && Math.Abs(s.SlipAngleDeg) <= 180 && Math.Abs(s.SteeringAngleDeg) <= 3000 &&
        Math.Abs(s.SteeringRateDegPerSec) <= 15000 && Math.Abs(s.YawRateDegPerSec) <= 2000 && Math.Abs(s.FinalFfb) <= 10 &&
        Math.Abs(s.FrontWheelSlipAvg) <= 10000 && Math.Abs(s.RearWheelSlipAvg) <= 10000 &&
        s.Throttle is >= 0 and <= 1.01 && s.Brake is >= 0 and <= 1.01 && s.Clutch is >= 0 and <= 1.01;
    private static bool Drifting(TelemetrySample s) => s.SpeedKmh >= 20 && Math.Abs(s.SlipAngleDeg) is >= 10 and < 72;
    private static double Time(List<Frame> frames) => frames.Sum(f => f.Dt);
    private static double Mean(List<Frame> frames, Func<TelemetrySample, double> value) => Time(frames) > 0 ? frames.Sum(f => value(f.Sample) * f.Dt) / Time(frames) : 0;
    private static double Peak(List<Frame> frames, Func<TelemetrySample, double> value) => frames.Count > 0 ? frames.Max(f => value(f.Sample)) : 0;
    private static double FrontShare(TelemetrySample s) => 100 * Math.Abs(s.FrontWheelSlipAvg) / (Math.Abs(s.FrontWheelSlipAvg) + Math.Abs(s.RearWheelSlipAvg));
    private static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }
}
