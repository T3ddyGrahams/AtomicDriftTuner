using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using Frame = AtomicDriftTuner.Engine.DriftDiagnosisEngine.Frame;

namespace AtomicDriftTuner.Engine;

/// <summary>Time-weighted observations from the shared clean timeline; no physics reconstruction or tune writes.</summary>
internal static class PowertrainDiagnosisEngine
{
    internal static PowertrainDiagnosis Analyze(List<List<Frame>> blocks, List<DriftEvent> phases,
        double driftLimit, TelemetrySession session, TelemetryAnalysis analysis)
    {
        var result = new PowertrainDiagnosis();
        var context = RunHistoryStore.ValidContext(session.Context) ? session.Context : null;
        var tune = context?.Tune;
        var recorded = tune?.Powertrain;
        var target = tune?.GearingTarget;
        if (target is null && tune is not null) result.TargetContext = tune.GearingTargetStatus + " " + result.TargetContext;
        var reliable = DriftDiagnosisEngine.Reliable(session, analysis);
        bool Useful(Frame f) => f.Dt > 0 && f.Sample.Rpm is > 0 and <= 30000 && f.Sample.Gear is >= 2 and <= 11 &&
            f.Sample.SpeedKmh >= 20 && Math.Abs(f.Sample.SlipAngleDeg) >= 10 && Math.Abs(f.Sample.SlipAngleDeg) < driftLimit &&
            f.Sample.LongitudinalVelocityMs is null or >= 0;
        var frames = blocks.SelectMany(b => b).Where(Useful).ToList();
        var seconds = frames.Sum(f => f.Dt);
        bool completeSignals = frames.Count > 0 && frames.All(f => f.Sample.HasExtendedSignals && f.Sample.LongitudinalVelocityMs is not null);
        bool InTargetSpeed(Frame f) => target is not null && f.Sample.SpeedKmh >= target.MinimumSpeedKmh && f.Sample.SpeedKmh <= target.MaximumSpeedKmh;
        var episodeTotals = new Dictionary<(string Kind, int Gear), (int Count, double Seconds)>();
        double? limiter = recorded is { AdjustableLimiter: false } ? recorded.BaseLimiterRpm : null;
        var phaseWindows = phases.Where(p => p.Phase is "Initiation" or "Transition").OrderBy(p => p.StartSeconds).ToArray();
        // The timeline is monotonic. A single forward pass avoids samples × events work on long runs.
        int phaseIndex = 0;
        var byPhase = new Dictionary<(int Gear, string Phase), List<Frame>>();
        foreach (var f in frames)
        {
            while (phaseIndex < phaseWindows.Length && phaseWindows[phaseIndex].EndSeconds < f.Sample.TimeSeconds) phaseIndex++;
            var phase = phaseIndex < phaseWindows.Length && phaseWindows[phaseIndex].StartSeconds <= f.Sample.TimeSeconds ? phaseWindows[phaseIndex].Phase : "Between entries / transitions";
            var key = (f.Sample.Gear - 1, phase); // Shared memory: reverse=0, neutral=1, first=2.
            if (!byPhase.TryGetValue(key, out var values)) byPhase[key] = values = [];
            values.Add(f);
        }
        foreach (var group in frames.GroupBy(f => f.Sample.Gear - 1).OrderBy(g => g.Key))
        {
            var values = group.ToList(); var time = values.Sum(f => f.Dt);
            bool hasTarget = target?.Gear == group.Key;
            result.Gears.Add(new() { Gear = group.Key, Seconds = time, LowRpm = Quantile(values, s => s.Rpm, .1),
                MedianRpm = Quantile(values, s => s.Rpm, .5), HighRpm = Quantile(values, s => s.Rpm, .9),
                LowSpeedKmh = Quantile(values, s => s.SpeedKmh, .1), HighSpeedKmh = Quantile(values, s => s.SpeedKmh, .9),
                HighThrottleSeconds = values.Where(f => f.Sample.Throttle >= .7).Sum(f => f.Dt),
                TargetSpeedSeconds = hasTarget ? values.Where(InTargetSpeed).Sum(f => f.Dt) : null,
                BelowTargetSeconds = hasTarget ? values.Where(f => InTargetSpeed(f) && f.Sample.Rpm < target!.MinimumRpm).Sum(f => f.Dt) : null,
                AboveTargetSeconds = hasTarget ? values.Where(f => InTargetSpeed(f) && f.Sample.Rpm > target!.MaximumRpm).Sum(f => f.Dt) : null,
                NearBaseLimiterSeconds = limiter is double limit ? values.Where(f => f.Sample.Rpm >= limit * .97).Sum(f => f.Dt) : null,
                RecordedRatio = recorded?.Gears.SingleOrDefault(g => g.Gear == group.Key)?.Ratio,
                Confidence = reliable && completeSignals && time >= 10 ? "MEDIUM" : "LOW" });
        }
        foreach (var (key, values) in byPhase.OrderBy(kv => kv.Key.Gear).ThenBy(kv => kv.Key.Phase, StringComparer.Ordinal))
            result.Phases.Add(new(key.Gear, key.Phase, values.Sum(f => f.Dt), Quantile(values, s => s.Rpm, .5),
                Quantile(values, s => s.Rpm, .1), Quantile(values, s => s.Rpm, .9)));

        if (target is not null)
        {
            result.TargetContext = $"Recorded target: gear {target.Gear}, {target.MinimumSpeedKmh:0.#}–{target.MaximumSpeedKmh:0.#} km/h, {target.MinimumRpm:0}–{target.MaximumRpm:0} RPM. Target exposure is counted only within that gear and speed range. This is your chosen band, not measured engine power. Later target edits do not reinterpret this run.";
            Episodes("High throttle below target", f => f.Sample.Gear - 1 == target.Gear && InTargetSpeed(f) && f.Sample.Rpm < target.MinimumRpm && f.Sample.Throttle >= .7 && f.Sample.Brake <= .05);
            Episodes("Above chosen RPM target", f => f.Sample.Gear - 1 == target.Gear && InTargetSpeed(f) && f.Sample.Rpm > target.MaximumRpm);
        }
        if (limiter is double limitRpm)
            Episodes("Near recorded base limiter", f => f.Sample.Rpm >= limitRpm * .97 && f.Sample.Throttle >= .7 && f.Sample.Brake <= .05);
        result.Events = result.Events.OrderBy(e => e.StartSeconds).Take(500).ToList();
        if (recorded is not null)
            result.SetupContext = (recorded.FinalDrive is double ratio ? $"Recorded final drive: {ratio:0.####}:1. {recorded.FinalDriveSource}. " : "Final-drive mapping is unverified. ") +
                (recorded.BaseLimiterRpm is double baseRpm ? $"Base limiter definition: {baseRpm:0} RPM. " : "Base limiter unavailable. ") +
                (recorded.AdjustableLimiter ? "Adjustable limiter: proximity measurement unavailable. " : "Limiter proximity uses this base reference, not verified limiter activation. ") +
                (tune!.SetupSource == LiveSetupCaptureService.CaptureSource ? "Setup selections were captured by the companion. " : "Setup selections describe the attached saved file. ") +
                (context!.TuneConfirmedInUse ? "Driver confirmed the setup in use." : "The setup was not confirmed in use.");
        AddEcu();
        result.Summary = frames.Count == 0 ? "No usable forward-gear RPM evidence. Record a clean run with RPM and gear signals." :
            $"{seconds:0.0}s of forward drift across {result.Gears.Count} gear(s). Typical RPM and speed ranges show the middle 80% of recorded time; peaks alone do not establish a gearing issue.";
        result.Limitations = "Road speed and engine RPM do not isolate tyre spin, clutch slip, a power deficit or the active ECU. Clutch values are raw signals: pedal polarity, assists and clutch engagement are not inferred. " +
            "Near-base-limiter time (at least 97% of the recorded base RPM limit) is proximity, not confirmed limiter contact. Event thresholds are provisional: at least 0.5s continuous evidence in one gear, with no large clutch-signal change. " +
            "Phase rows cover only their drifting portions. The event list shows up to 500 examples; test guidance counts all qualifying episodes. These measurements do not score improvement or alter setup/FFB recommendations. " +
            (recorded is null ? "This older or incomplete run has no powertrain definition snapshot; today's car files are not applied retrospectively. " : string.Join(" ", recorded.Limitations)) +
            (reliable ? "" : " Telemetry/context quality is insufficient for a gearing test suggestion.");
        if (frames.Any(f => !f.Sample.HasExtendedSignals || f.Sample.LongitudinalVelocityMs is null))
            result.Limitations += " Some extended or travel-direction signals were not recorded; those limitations remain unknown.";
        result.NextTest = target is null ? "Save your preferred gear, speed range and RPM range in AC Setup → Gearing → Save target for this car, then record a fresh baseline. The example RPM band is not a detected power band." :
            "Keep this setup and repeat the same section. A target mismatch alone does not show whether gearing, wheelspin, clutch use, line or engine behavior caused it.";
        if (reliable && completeSignals && seconds >= 20 && context is { TuneConfirmedInUse: true, CarIdentityVerified: true } && recorded?.FinalDrive is not null && target is not null &&
            recorded.Gears.Any(g => g.Gear == target.Gear) && result.Gears.Any(g => g.Gear == target.Gear && g.Seconds >= 10))
        {
            var low = episodeTotals.GetValueOrDefault(("High throttle below target", target.Gear));
            var high = episodeTotals.GetValueOrDefault(("Near recorded base limiter", target.Gear));
            if (low.Count >= 3 && low.Seconds >= 3 && high.Count >= 3 && high.Seconds >= 3)
                result.NextTest = "This gear showed both low-RPM and near-base-limiter periods. Review the sections and pedal use before changing final drive; one ratio may not suit the entire speed range.";
            else if (high.Count >= 3 && high.Seconds >= 3)
                result.NextTest = $"Gear {target.Gear} repeatedly approached the recorded base limiter. Confirm actual limiter contact in game; if it recurs, compare one taller final-drive option in the gearing planner. Keep ECU and other settings fixed, then repeat the same section.";
            else if (low.Count >= 3 && low.Seconds >= 3)
                result.NextTest = $"Gear {target.Gear} repeatedly stayed below your chosen RPM band at high throttle without a large recorded clutch change. Review the input events; if the pattern repeats, compare one shorter final-drive option in the planner. Keep ECU fixed. This is a test hypothesis, not proof of insufficient engine power.";
        }
        return result;

        void Episodes(string kind, Func<Frame, bool> eligible)
        {
            foreach (var block in blocks)
            {
                var segment = new List<Frame>(); double clutchMin = 0, clutchMax = 0;
                void Flush()
                {
                    if (segment.Count >= 3 && segment.Sum(f => f.Dt) >= .5 && segment[^1].Sample.TimeSeconds - segment[0].Sample.TimeSeconds >= .5)
                    {
                        var first = segment[0].Sample; var last = segment[^1].Sample;
                        var key = (kind, first.Gear - 1);
                        var count = episodeTotals.GetValueOrDefault(key);
                        episodeTotals[key] = (count.Count + 1, count.Seconds + last.TimeSeconds - first.TimeSeconds);
                        if (result.Events.Count >= 1500) { segment.Clear(); return; }
                        var phase = phases.FirstOrDefault(p => p.Phase is "Initiation" or "Transition" && p.StartSeconds <= first.TimeSeconds && p.EndSeconds >= first.TimeSeconds)?.Phase ?? "Between entries / transitions";
                        result.Events.Add(new() { Kind = kind, Gear = first.Gear - 1, Phase = phase, StartSeconds = first.TimeSeconds, EndSeconds = last.TimeSeconds,
                            StartRpm = first.Rpm, EndRpm = last.Rpm,
                            Evidence = $"RPM {first.Rpm} → {last.Rpm}; speed {first.SpeedKmh:0.0} → {last.SpeedKmh:0.0} km/h; throttle {segment.Min(f => f.Sample.Throttle):P0}–{segment.Max(f => f.Sample.Throttle):P0}; brake {segment.Max(f => f.Sample.Brake):P0} maximum; raw clutch {clutchMin:P0}–{clutchMax:P0}. Gear stayed constant. Input/response association only; clutch engagement and delivered torque are not measured." });
                    }
                    segment.Clear();
                }
                foreach (var f in block)
                {
                    if (!Useful(f) || f.Dt > .1 || !eligible(f)) { Flush(); continue; }
                    if (segment.Count > 0 && (segment[^1].Sample.Gear != f.Sample.Gear || Math.Max(clutchMax, f.Sample.Clutch) - Math.Min(clutchMin, f.Sample.Clutch) > .15)) Flush();
                    if (segment.Count == 0) clutchMin = clutchMax = f.Sample.Clutch;
                    clutchMin = Math.Min(clutchMin, f.Sample.Clutch); clutchMax = Math.Max(clutchMax, f.Sample.Clutch); segment.Add(f);
                }
                Flush();
            }
        }

        void AddEcu()
        {
            var mappings = tune?.DecodedSetup.Where(s => s.Section.Equals("ENGINE_MAPS", StringComparison.OrdinalIgnoreCase)).ToList();
            if (tune?.HasUnassignedSetupValues == true)
            {
                result.EcuContext = "The setup contains an unidentified VALUE. It cannot be identified as the ECU; no engine-map effect is inferred. Capture a complete named setup before testing ECU changes.";
                return;
            }
            if (mappings?.Count != 1) return;
            var setting = mappings[0]; result.EcuContext = setting.Display;
            if (setting.Status != DecodedSetupSetting.Verified || setting.EngineMap is not { } map) return;
            result.MapPoints = map.Points.ToList();
            var covered = frames.Select(f => (Frame: f, Multiplier: Interpolate(map.Points, f.Sample.Rpm))).Where(p => p.Multiplier is not null).ToList();
            if (covered.Count == 0)
            { result.EcuContext += " Recorded drift RPM does not overlap this curve's defined range. No extrapolation was used."; return; }
            result.EcuContext += $" Over {covered.Sum(p => p.Frame.Dt):0.0}s of recorded drift RPM inside this curve, its configured multiplier spans {covered.Min(p => p.Multiplier):0.###}–{covered.Max(p => p.Multiplier):0.###}× (linear lookup context). This does not measure horsepower or prove the map was active. No extrapolation beyond curve points.";
        }
    }

    private static double Quantile(List<Frame> frames, Func<TelemetrySample, double> value, double quantile)
    {
        var sorted = frames.OrderBy(f => value(f.Sample)).ToList();
        double target = sorted.Sum(f => f.Dt) * quantile, sum = 0;
        foreach (var f in sorted) { sum += f.Dt; if (sum >= target) return value(f.Sample); }
        return sorted.Count == 0 ? 0 : value(sorted[^1].Sample);
    }

    private static double? Interpolate(IReadOnlyList<EngineMapPoint> points, double rpm)
    {
        if (points.Count == 0 || rpm < points[0].Rpm || rpm > points[^1].Rpm) return null;
        int low = 0, high = points.Count - 1;
        while (low < high) { int middle = (low + high) / 2; if (points[middle].Rpm < rpm) low = middle + 1; else high = middle; }
        if (points[low].Rpm == rpm) return points[low].Multiplier;
        var left = points[low - 1]; var right = points[low];
        return left.Multiplier + (right.Multiplier - left.Multiplier) * (rpm - left.Rpm) / (right.Rpm - left.Rpm);
    }
}
