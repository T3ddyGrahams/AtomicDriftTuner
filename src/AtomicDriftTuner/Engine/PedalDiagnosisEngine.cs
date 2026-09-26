using AtomicDriftTuner.Models;
using Frame = AtomicDriftTuner.Engine.DriftDiagnosisEngine.Frame;

namespace AtomicDriftTuner.Engine;

/// <summary>Input/response associations within the drift analyzer's filtered continuous blocks.</summary>
internal static class PedalDiagnosisEngine
{
    private sealed record Edge(double Start, double End, int From, double Before, double After);
    private const double Dwell = .12;

    internal static PedalDiagnosis Analyze(List<List<Frame>> blocks, List<DriftEvent> phases, double driftLimit = 72)
    {
        bool Drifting(TelemetrySample s) => s.SpeedKmh >= 20 && Math.Abs(s.SlipAngleDeg) >= 10 && Math.Abs(s.SlipAngleDeg) < driftLimit &&
            s.LongitudinalVelocityMs is null or >= 0;
        var result = new PedalDiagnosis();
        var drift = blocks.SelectMany(b => b).Where(f => Drifting(f.Sample)).ToList();
        result.DriftSeconds = drift.Sum(f => f.Dt);
        result.KnownSignalSeconds = drift.Where(f => f.Sample.HasExtendedSignals).Sum(f => f.Dt);
        foreach (var block in blocks.Where(b => b.Count > 1))
        {
            foreach (var edge in Edges(block, s => s.Throttle, .35, .65))
                Add(block, edge.Start, edge.End, edge.From < 0 ? "Throttle application" : "Throttle lift",
                    $"Throttle {edge.Before:P0} → {edge.After:P0}");
            foreach (var edge in Edges(block, s => s.Brake, .05, .20))
                Add(block, edge.Start, edge.End, edge.From < 0 ? "Brake application" : "Brake release",
                    $"Brake {edge.Before:P0} → {edge.After:P0}");
            var clutch = Edges(block, s => s.Clutch, .25, .75);
            for (var i = 0; i + 1 < clutch.Count; i++)
            {
                var first = clutch[i]; var last = clutch[i + 1];
                if (first.From == last.From || last.End - first.Start > 1.5) continue;
                Add(block, first.Start, last.End, "Clutch signal cycle",
                    $"Clutch signal {first.Before:P0} → {first.After:P0} → {last.After:P0} (raw direction)");
                i++;
            }
            double overlapStart = -1;
            foreach (var frame in block)
            {
                var s = frame.Sample;
                if (s.Throttle >= .20 && s.Brake >= .15)
                {
                    if (overlapStart < 0) overlapStart = s.TimeSeconds;
                }
                else if (overlapStart >= 0)
                {
                    if (s.TimeSeconds - overlapStart >= .20)
                        Add(block, overlapStart, s.TimeSeconds, "Throttle / brake overlap", "Throttle ≥20% and brake ≥15%");
                    overlapStart = -1;
                }
            }
            // An overlap still active at a block boundary has no observed release.
            if (overlapStart >= 0 && block[^1].Sample.TimeSeconds - overlapStart >= .20)
                Add(block, overlapStart, block[^1].Sample.TimeSeconds, "Throttle / brake overlap",
                    "Throttle ≥20% and brake ≥15%; release not captured");
        }
        result.Events = result.Events.OrderBy(e => e.StartSeconds).ThenBy(e => e.Kind).ToList();
        void Metric(string key, string name, double value, string unit, string evidence)
        {
            result.ContextMetrics.Add(new RunMetric { Key = key, Name = name,
                Value = result.DriftSeconds > 0 ? value : null, Unit = unit, EvidenceSeconds = result.DriftSeconds, Events = -1,
                Confidence = result.DriftSeconds >= 20 && result.KnownSignalSeconds >= result.DriftSeconds * .9 ? "MEDIUM" : "LOW",
                Evidence = evidence });
        }
        var throttleMean = Mean(drift, s => s.Throttle);
        var clutchMean = Mean(drift, s => s.Clutch);
        Metric("throttle-low", "Low-throttle time", Mean(drift, s => s.Throttle <= .20 ? 100 : 0), "%", "Share of clean drift at ≤20% throttle.");
        Metric("throttle-high", "High-throttle time", Mean(drift, s => s.Throttle >= .70 ? 100 : 0), "%", "Share of clean drift at ≥70% throttle.");
        Metric("throttle-variation", "Throttle variation", 100 * Math.Sqrt(Mean(drift, s => Math.Pow(s.Throttle - throttleMean, 2))), "pp", "Time-weighted standard deviation; input context, not a smoothness score.");
        Metric("brake-mean", "Average brake input", Mean(drift, s => s.Brake * 100), "%", "Raw brake signal during clean drift; no separate handbrake signal.");
        Metric("brake-active", "Braking time", Mean(drift, s => s.Brake >= .15 ? 100 : 0), "%", "Share of clean drift at ≥15% brake.");
        Metric("pedal-overlap", "Throttle / brake overlap time", Mean(drift, s => s.Throttle >= .20 && s.Brake >= .15 ? 100 : 0), "%", "Overlap may be intentional; it is not automatically a driver error.");
        Metric("clutch-mean", "Average clutch signal", clutchMean * 100, "%", "Raw signal; direction, pedal calibration and assists are not identified.");
        Metric("clutch-variation", "Clutch signal variation", 100 * Math.Sqrt(Mean(drift, s => Math.Pow(s.Clutch - clutchMean, 2))), "pp", "An unchanging signal cannot prove that a physical clutch pedal is present or working.");
        foreach (var (key, name, kind) in new[] { ("throttle-applications", "Throttle applications", "Throttle application"),
                     ("throttle-lifts", "Throttle lifts", "Throttle lift"), ("clutch-cycles", "Clutch signal cycles", "Clutch signal cycle") })
            Metric(key, name, result.DriftSeconds > 0 ? 60 * result.Events.Count(e => e.Kind == kind && e.InDrift) / result.DriftSeconds : 0, "/min",
                "Rate of substantial, sustained input changes starting in clean drift; smaller changes remain in the exposure metrics.");
        result.Summary = $"{result.Events.Count} pedal event(s) near drifting; {result.Events.Count(e => e.ResponseComplete)} with complete before/after windows. " +
            $"{result.DriftSeconds:0.0}s of drift input context. Compare pedal use before judging a setup change.";
        result.Limitations = "These are timing associations, not proof that a pedal caused the response or that technique is wrong. " +
            "The response compares 0.30–0.05s before an event with 0.10–0.65s after it; other inputs, line and tire state can change too. " +
            "Clutch direction/assists are unverified; there is no separate handbrake signal. Only complete clutch excursions and returns are counted. " +
            "Steady or zero signals do not prove that a pedal is available or calibrated.";
        if (result.KnownSignalSeconds < result.DriftSeconds * .9)
            result.Limitations += " Legacy signal coverage is unverified; record a fresh run for pedal comparison.";
        return result;

        void Add(List<Frame> block, double start, double end, string kind, string input)
        {
            var around = Window(block, start - .5, end + .65);
            if (!around.Any(f => Drifting(f.Sample))) return;
            var phase = phases.FirstOrDefault(p => p.Phase is "Initiation" or "Transition" && p.StartSeconds <= end + .5 && p.EndSeconds >= start - .5);
            var first = block[Math.Min(LowerBound(block, start), block.Count - 1)].Sample;
            var e = new PedalEvent { Kind = kind, StartSeconds = start, EndSeconds = end, Input = input,
                InDrift = Drifting(first), Phase = phase?.Phase ?? (Drifting(first) ? "Sustained drift" : "Approach / exit") };
            var before = Window(block, start - .30, start - .05);
            var after = Window(block, end + .10, end + .65);
            var span = Window(block, start - .30, end + .65);
            e.ResponseComplete = block[0].Sample.TimeSeconds <= start - .30 && block[^1].Sample.TimeSeconds >= end + .65 &&
                before.Count >= 3 && after.Count >= 3 && span.All(f => f.Sample.SpeedKmh >= 20 && f.Dt <= .10);
            e.GearChanged = span.Select(f => f.Sample.Gear).Distinct().Skip(1).Any();
            if (e.ResponseComplete)
            {
                e.AngleChangeDeg = Mean(after, s => Math.Abs(s.SlipAngleDeg)) - Mean(before, s => Math.Abs(s.SlipAngleDeg));
                e.YawChangeDegPerSec = Mean(after, s => Math.Abs(s.YawRateDegPerSec)) - Mean(before, s => Math.Abs(s.YawRateDegPerSec));
                bool wheelSlipKnown = span.All(f => DriftDiagnosisEngine.ValidWheelSlip(f.Sample)) &&
                    span.Any(f => Math.Abs(f.Sample.FrontWheelSlipAvg) + Math.Abs(f.Sample.RearWheelSlipAvg) > .01);
                if (wheelSlipKnown) e.RearSlipChange = Mean(after, s => Math.Abs(s.RearWheelSlipAvg)) - Mean(before, s => Math.Abs(s.RearWheelSlipAvg));
                if (span.All(f => f.Sample.Rpm > 0))
                    e.RpmPeakChange = span.Max(f => f.Sample.Rpm) - Mean(before, s => s.Rpm);
                e.Confidence = span.All(f => f.Sample.HasExtendedSignals) ? "MEDIUM" : "LOW";
                e.Evidence = $"After vs before: |drift angle| {Signed(e.AngleChangeDeg)}°; |yaw rate| {Signed(e.YawChangeDegPerSec)}°/s; " +
                    $"speed {Signed(Mean(after, s => s.SpeedKmh) - Mean(before, s => s.SpeedKmh))} km/h; " +
                    $"rear wheel-slip signal {Signed(e.RearSlipChange)}; peak RPM rise {Signed(e.RpmPeakChange)}. " +
                    $"|Steering| change {Signed(Mean(after, s => Math.Abs(s.SteeringAngleDeg)) - Mean(before, s => Math.Abs(s.SteeringAngleDeg)))}°. " +
                    $"Other input ranges: throttle {Range(span, s => s.Throttle)}; brake {Range(span, s => s.Brake)}; clutch {Range(span, s => s.Clutch)}. ";
                if (kind == "Clutch signal cycle")
                    e.Evidence += e.GearChanged ? "Gear changed: may accompany a shift; not classified as a clutch kick. " :
                        first.Gear >= 2 && e.RpmPeakChange >= 300 && span.Any(f => f.Sample.Throttle >= .35) ?
                        "Possible clutch-kick pattern: signal returned, RPM rose and no gear change was recorded; technique/assists are not confirmed. " :
                        "Clutch signal returned; insufficient context to identify a clutch kick. ";
                else if (e.GearChanged) e.Evidence += "A gear change also occurred in this window. ";
                e.Evidence += "Association only; inspect the line and other inputs.";
            }
            else e.Evidence = "Input event observed, but a complete continuous before/after response window at useful speed/rate is unavailable. No response conclusion.";
            result.Events.Add(e);
        }
    }

    // Hysteresis and a time-based dwell reject isolated spikes at different sample rates.
    private static List<Edge> Edges(List<Frame> block, Func<TelemetrySample, double> value, double low, double high)
    {
        var edges = new List<Edge>();
        int stable = 0, pending = 0;
        double pendingSince = 0, departure = 0, stableValue = 0;
        foreach (var f in block)
        {
            var v = value(f.Sample); var t = f.Sample.TimeSeconds;
            var side = v <= low ? -1 : v >= high ? 1 : 0;
            if (side == stable && stable != 0) { departure = t; stableValue = v; pending = 0; continue; }
            if (side == 0) { pending = 0; continue; }
            if (pending != side) { pending = side; pendingSince = t; }
            if (t - pendingSince + .000001 < Dwell) continue;
            if (stable != 0 && pendingSince - departure <= 1.2)
                edges.Add(new Edge(departure, pendingSince, stable, stableValue, v));
            stable = side; departure = t; stableValue = v; pending = 0;
        }
        return edges;
    }

    private static int LowerBound(List<Frame> block, double time)
    {
        int lo = 0, hi = block.Count;
        while (lo < hi) { var mid = (lo + hi) / 2; if (block[mid].Sample.TimeSeconds < time) lo = mid + 1; else hi = mid; }
        return lo;
    }
    private static List<Frame> Window(List<Frame> block, double start, double end)
    {
        var frames = new List<Frame>();
        for (int i = LowerBound(block, start); i < block.Count && block[i].Sample.TimeSeconds <= end; i++) frames.Add(block[i]);
        return frames;
    }
    private static double Mean(List<Frame> frames, Func<TelemetrySample, double> value)
    {
        var seconds = frames.Sum(f => f.Dt);
        return seconds > 0 ? frames.Sum(f => value(f.Sample) * f.Dt) / seconds : 0;
    }
    private static string Signed(double? value) => value is double v ? $"{v:+0.0;-0.0;0.0}" : "unknown";
    private static string Range(List<Frame> frames, Func<TelemetrySample, double> value) =>
        $"{frames.Min(f => value(f.Sample)):P0}–{frames.Max(f => value(f.Sample)):P0}";
}
