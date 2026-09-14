using AtomicDriftTuner.Models;
using Frame = AtomicDriftTuner.Engine.DriftDiagnosisEngine.Frame;

namespace AtomicDriftTuner.Engine;

/// <summary>Observed holds within a saved body-slip target; no steering-lock or setup capability is assumed.</summary>
internal static class DriftAngleGoalEngine
{
    internal static DriftAngleDiagnosis Analyze(List<List<Frame>> blocks, CarBehaviorTarget? goal, DriftDiagnosis diagnosis)
    {
        var result = new DriftAngleDiagnosis();
        if (goal?.HasAngleGoal != true) return result;
        result.Enabled = true; result.TargetMinDeg = goal.AngleMinDeg; result.TargetMaxDeg = goal.AngleMaxDeg;
        bool InTarget(Frame f) => f.Sample.SpeedKmh >= 20 && (f.Sample.LongitudinalVelocityMs is null or >= 0) &&
            Math.Abs(f.Sample.SlipAngleDeg) >= goal.AngleMinDeg && Math.Abs(f.Sample.SlipAngleDeg) <= goal.AngleMaxDeg;
        foreach (var block in blocks)
        {
            result.TargetSeconds += block.Where(InTarget).Sum(f => f.Dt);
            double longest = 0;
            foreach (var f in block) { longest = InTarget(f) ? longest + f.Dt : 0; result.LongestHoldSeconds = Math.Max(result.LongestHoldSeconds, longest); }
            for (int i = 0; i < block.Count; i++)
            {
                if (!InTarget(block[i])) continue;
                int start = i, lastHigh = i, end = i;
                double belowSince = -1;
                for (; end < block.Count; end++)
                {
                    var s = block[end].Sample;
                    if (Math.Abs(s.SlipAngleDeg) >= goal.AngleMinDeg - 5 && s.SpeedKmh >= 15)
                    { lastHigh = end; belowSince = -1; }
                    else
                    {
                        if (belowSince < 0) belowSince = s.TimeSeconds;
                        if (s.TimeSeconds - belowSince >= .5) break;
                    }
                }
                var episode = block.GetRange(start, lastHigh - start + 1);
                var bandTime = episode.Where(InTarget).Sum(f => f.Dt);
                i = Math.Max(i, end - 1);
                if (bandTime < .5) continue;
                var a = new AngleHoldAttempt { StartSeconds = block[start].Sample.TimeSeconds, EndSeconds = block[lastHigh].Sample.TimeSeconds, InTargetSeconds = bandTime };
                double held = 0;
                foreach (var f in episode) { held = InTarget(f) ? held + f.Dt : 0; a.LongestHoldSeconds = Math.Max(a.LongestHoldSeconds, held); }
                var before = Window(block, a.StartSeconds - .3, a.StartSeconds);
                var after = Window(block, a.EndSeconds, a.EndSeconds + 2);
                a.Complete = block[0].Sample.TimeSeconds <= a.StartSeconds - .3 && block[^1].Sample.TimeSeconds >= a.EndSeconds + 2 &&
                    before.Count >= 3 && after.Count >= 10 && episode.Concat(before).Concat(after).All(f => f.Dt <= .1 && f.Sample.LongitudinalVelocityMs is not null);
                if (a.Complete)
                {
                    var entrySpeed = Mean(before, f => f.Sample.SpeedKmh);
                    a.SpeedRetentionPct = entrySpeed > 0 ? 100 * Mean(Window(block, Math.Max(a.StartSeconds, a.EndSeconds - .25), a.EndSeconds), f => f.Sample.SpeedKmh) / entrySpeed : null;
                    double settled = 0, overRotation = 0;
                    bool crossed90 = false;
                    foreach (var f in episode.Concat(after.Where(f => f.Sample.TimeSeconds > a.EndSeconds)))
                    {
                        overRotation = Math.Abs(f.Sample.SlipAngleDeg) >= 90 || f.Sample.LongitudinalVelocityMs < -.5 ? overRotation + f.Dt : 0;
                        crossed90 |= overRotation >= .2;
                    }
                    foreach (var f in after)
                    {
                        settled = Math.Abs(f.Sample.SlipAngleDeg) < goal.AngleMinDeg - 5 && f.Sample.LongitudinalVelocityMs > .5 &&
                            f.Sample.SpeedKmh >= Math.Max(20, entrySpeed * .5) ? settled + f.Dt : 0;
                        if (settled >= .5) a.RecoveryObserved = true;
                    }
                    a.ControlConcern = crossed90 || a.SpeedRetentionPct < 60;
                    a.Evidence = $"{a.InTargetSeconds:0.0}s in the requested band; longest continuous hold {a.LongestHoldSeconds:0.0}s; " +
                        $"end-of-attempt speed {a.SpeedRetentionPct:0}% of entry speed. " +
                        (a.RecoveryObserved ? "A return below the target band with useful speed was observed." : "No settled return below the band was observed within 2s.") +
                        (crossed90 ? " Backward travel or ≥90° body slip was recorded for at least 0.2s; inspect the recovery." : "") +
                        (a.SpeedRetentionPct < 60 ? " Speed fell by more than 40%; planned deceleration can also explain this." : "");
                }
                else a.Evidence = $"{a.InTargetSeconds:0.0}s in the requested band; the entry/recovery window is incomplete, sparse or missing travel-direction data. Recovery is unknown; use a fresh recording.";
                result.Attempts.Add(a);
                diagnosis.Events.Add(new DriftEvent { Phase = "Requested angle hold", StartSeconds = a.StartSeconds, EndSeconds = a.EndSeconds,
                    SpeedKmh = block[start].Sample.SpeedKmh, Evidence = a.Evidence });
            }
        }
        var complete = result.Attempts.Where(a => a.Complete).ToList();
        result.CompletedAttempts = complete.Count;
        result.IncompleteAttempts = result.Attempts.Count - complete.Count;
        result.RecoveredAttempts = complete.Count(a => a.RecoveryObserved && !a.ControlConcern);
        result.ControlConcerns = complete.Count(a => a.ControlConcern || !a.RecoveryObserved);
        if (complete.Count > 0) result.SpeedRetentionPct = Median(complete.Select(a => a.SpeedRetentionPct ?? 0));
        var confidence = complete.Count >= 3 && result.IncompleteAttempts <= complete.Count && diagnosis.UsableDriftSeconds >= 20 ? "MEDIUM" : "LOW";
        void Metric(string key, string name, double? value, string unit, string evidence) => diagnosis.Metrics.Add(new RunMetric
        { Key = key, Name = name, Value = value, Unit = unit, Events = complete.Count, EvidenceSeconds = diagnosis.UsableDriftSeconds, Confidence = confidence, Evidence = evidence });
        Metric("angle-time", "Time in requested angle band", diagnosis.UsableDriftSeconds > 0 ? result.TargetSeconds * 100 / diagnosis.UsableDriftSeconds : null, "%",
            $"Requested body slip {goal.AngleMinDeg:0}–{goal.AngleMaxDeg:0}° at ≥20 km/h. Above-band angle is not rewarded.");
        Metric("angle-hold", "Typical continuous angle hold", complete.Count > 0 ? Median(complete.Select(a => a.LongestHoldSeconds)) : null, "s",
            "Median longest hold per complete attempt. Brief peaks do not establish sustained control.");
        Metric("angle-speed", "Speed retained during angle attempts", result.SpeedRetentionPct, "%", "Median end/entry speed. Track deceleration and inputs remain confounds.");
        Metric("angle-recovery", "Observed settled returns", complete.Count > 0 ? 100.0 * result.RecoveredAttempts / complete.Count : null, "%",
            "Forward travel below the band for ≥0.5s within a complete 2s window, without major speed loss or backward travel. This is a recovery proxy, not a confirmed spin detector.");
        result.Summary = $"{result.TargetSeconds:0.0}s in your {goal.AngleMinDeg:0}–{goal.AngleMaxDeg:0}° band; longest hold {result.LongestHoldSeconds:0.0}s. " +
            $"{result.RecoveredAttempts}/{result.CompletedAttempts} complete attempts had a settled return; {result.IncompleteAttempts} have unknown recovery.";
        return result;
    }
    private static List<Frame> Window(List<Frame> block, double start, double end)
    {
        int lo = 0, hi = block.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (block[mid].Sample.TimeSeconds < start) lo = mid + 1; else hi = mid; }
        var list = new List<Frame>();
        for (; lo < block.Count && block[lo].Sample.TimeSeconds <= end; lo++) list.Add(block[lo]);
        return list;
    }
    private static double Mean(List<Frame> frames, Func<Frame, double> value) => frames.Sum(f => f.Dt) is > 0 and var seconds ? frames.Sum(f => value(f) * f.Dt) / seconds : 0;
    private static double Median(IEnumerable<double> source) { var a = source.Order().ToArray(); return a.Length % 2 == 0 ? (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2 : a[a.Length / 2]; }
}
