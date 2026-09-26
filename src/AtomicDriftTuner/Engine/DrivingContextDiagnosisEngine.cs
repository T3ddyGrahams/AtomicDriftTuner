using AtomicDriftTuner.Models;
using Frame = AtomicDriftTuner.Engine.DriftDiagnosisEngine.Frame;

namespace AtomicDriftTuner.Engine;

/// <summary>Descriptive partitions of already-clean evidence. Never re-admits excluded frames.</summary>
internal static class DrivingContextDiagnosisEngine
{
    internal static DrivingContextDiagnosis Analyze(List<Frame> frames, List<Frame> drift, List<Frame> steady,
        List<Frame> front, List<DriftEvent> events)
    {
        var result = new DrivingContextDiagnosis();
        void Add(string key, string phase, int band, string direction, List<Frame> samples, double? value, int eventCount = -1)
        {
            var seconds = samples.Sum(f => f.Dt);
            var pedals = samples.Where(f => f.Sample.HasExtendedSignals).ToList();
            var known = pedals.Sum(f => f.Dt);
            bool enough = eventCount >= 0 ? eventCount >= 3 : seconds >= 10;
            bool supported = enough && seconds > 0 && value is double n && double.IsFinite(n);
            result.Observations.Add(new() { MetricKey = key, Phase = phase, SpeedBand = band, Direction = direction,
                Value = supported ? value : null,
                EvidenceSeconds = seconds, Events = eventCount, Confidence = supported ? "MEDIUM" : "LOW",
                KnownPedalSeconds = known, ThrottlePct = known > 0 && known >= .9 * seconds ? Mean(pedals, s => s.Throttle * 100) : null,
                BrakePct = known > 0 && known >= .9 * seconds ? Mean(pedals, s => s.Brake * 100) : null,
                ClutchSignalPct = known > 0 && known >= .9 * seconds ? Mean(pedals, s => s.Clutch * 100) : null,
                UnknownTravelSeconds = samples.Where(f => f.Sample.LongitudinalVelocityMs is null).Sum(f => f.Dt) });
        }
        foreach (var group in steady.Where(f => f.Dt > 0).GroupBy(f => (Band(f.Sample.SpeedKmh), Direction(f.Sample.SlipAngleDeg))))
        {
            var samples = group.ToList(); var (band, direction) = group.Key;
            var slip = samples.Where(f => DriftDiagnosisEngine.ValidWheelSlip(f.Sample) && Math.Abs(f.Sample.FrontWheelSlipAvg) + Math.Abs(f.Sample.RearWheelSlipAvg) > .01).ToList();
            if (slip.Count > 0)
            {
                double share = Mean(slip, s => 100 * Math.Abs(s.FrontWheelSlipAvg) / (Math.Abs(s.FrontWheelSlipAvg) + Math.Abs(s.RearWheelSlipAvg)));
                Add("front-slip-share", "Sustained drift", band, direction, slip, share);
                Add("rear-slip-share", "Sustained drift", band, direction, slip, 100 - share);
            }
            var powered = samples.Where(f => f.Sample.Throttle >= .7 && f.Sample.HasExtendedSignals).ToList();
            if (powered.Count > 0) Add("throttle-rotation", "Sustained drift", band, direction, powered, Mean(powered, s => Math.Abs(s.YawRateDegPerSec)));
            int oscillations = events.Count(e => e.Phase == "Steering oscillation proxy" && Band(e.SpeedKmh) == band && e.Direction == direction);
            Add("oscillation", "Sustained drift", band, direction, samples, oscillations * 60 / samples.Sum(f => f.Dt));
        }
        // Variation only spans adjacent steady frames in the same bucket. Filtering or a
        // continuity break must not turn two unrelated sections into one derivative.
        var variations = new Dictionary<(int, string), List<(Frame Frame, double Change)>>();
        Frame? last = null;
        foreach (var f in steady)
        {
            var key = (Band(f.Sample.SpeedKmh), Direction(f.Sample.SlipAngleDeg));
            if (last is not null && f.Dt > 0 && Math.Abs(f.Sample.TimeSeconds - last.Sample.TimeSeconds - f.Dt) < .000001 &&
                key == (Band(last.Sample.SpeedKmh), Direction(last.Sample.SlipAngleDeg)))
            {
                if (!variations.TryGetValue(key, out var list)) variations[key] = list = [];
                list.Add((f, Math.Abs(f.Sample.SlipAngleDeg - last.Sample.SlipAngleDeg)));
            }
            last = f;
        }
        foreach (var (key, values) in variations)
            Add("stability", "Sustained drift", key.Item1, key.Item2, values.Select(v => v.Frame).ToList(), values.Sum(v => v.Change) / values.Sum(v => v.Frame.Dt));
        foreach (var group in front.Where(f => f.Dt > 0 && Math.Abs(f.Sample.YawRateDegPerSec) > .01)
            .GroupBy(f => (Band(f.Sample.SpeedKmh), Direction(f.Sample.YawRateDegPerSec))))
        {
            var samples = group.ToList();
            Add("front-response", "Low-angle cornering", group.Key.Item1, "Yaw " + group.Key.Item2.ToLowerInvariant(), samples,
                Mean(samples, s => Math.Abs(s.YawRateDegPerSec) / (Math.Abs(s.SteeringAngleDeg) + 5)));
        }
        foreach (var group in drift.Where(f => f.Dt > 0).GroupBy(f => (Band(f.Sample.SpeedKmh), Direction(f.Sample.SlipAngleDeg))))
        {
            var samples = group.ToList();
            Add("clipping", "Usable drift", group.Key.Item1, group.Key.Item2, samples, Mean(samples, s => Math.Abs(s.FinalFfb) >= .98 ? 100 : 0));
        }
        var phaseGroups = new Dictionary<(string Phase, int Band, string Direction), List<(DriftEvent Event, List<Frame> Samples)>>();
        foreach (var e in events.Where(e => e.Phase is "Initiation" or "Transition"))
        {
            var samples = Slice(frames, e.StartSeconds, e.EndSeconds);
            if (samples.Count == 0 || string.IsNullOrEmpty(e.Direction)) continue;
            var bands = samples.Select(f => Band(f.Sample.SpeedKmh)).Distinct().ToArray();
            var key = (e.Phase, bands.Length == 1 ? bands[0] : 3, e.Direction);
            if (!phaseGroups.TryGetValue(key, out var list)) phaseGroups[key] = list = [];
            list.Add((e, samples));
        }
        foreach (var (key, values) in phaseGroups)
        {
            var samples = values.SelectMany(v => v.Samples).ToList();
            var durations = values.Select(v => v.Event.DurationSeconds).Order().ToArray();
            double median = durations.Length % 2 == 0 ? (durations[durations.Length / 2 - 1] + durations[durations.Length / 2]) / 2 : durations[durations.Length / 2];
            Add(key.Phase == "Initiation" ? "initiation" : "transition", key.Phase, key.Band, key.Direction, samples, median, values.Count);
            if (key.Phase == "Transition")
                Add("self-steer", "Transition", key.Band, key.Direction, samples, Mean(samples, s => Math.Abs(s.SteeringRateDegPerSec)), values.Count);
        }
        result.Observations = result.Observations.OrderBy(o => o.Phase).ThenBy(o => o.MetricKey).ThenBy(o => o.SpeedBand).ThenBy(o => o.Direction).ToList();
        return result;
    }

    private static List<Frame> Slice(List<Frame> frames, double start, double end)
    {
        int low = 0, high = frames.Count;
        while (low < high) { int mid = low + (high - low) / 2; if (frames[mid].Sample.TimeSeconds <= start) low = mid + 1; else high = mid; }
        var result = new List<Frame>();
        for (int i = low; i < frames.Count && frames[i].Sample.TimeSeconds <= end + .000001; i++)
        {
            var f = frames[i]; double dt = Math.Min(f.Dt, Math.Max(0, f.Sample.TimeSeconds - start));
            if (dt > 0) result.Add(f with { Dt = dt });
        }
        return result;
    }
    private static int Band(double speed) => speed < 50 ? 0 : speed < 90 ? 1 : 2;
    private static string Direction(double sign) => sign < 0 ? "Left" : "Right";
    private static double Mean(List<Frame> frames, Func<TelemetrySample, double> value)
    {
        double seconds = frames.Sum(f => f.Dt);
        return seconds > 0 ? frames.Sum(f => f.Dt * value(f.Sample)) / seconds : 0;
    }
}
