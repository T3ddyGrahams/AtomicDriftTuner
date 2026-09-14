using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using System.IO;
using System.Reflection;
using System.Text.Json;

internal static class PedalChecks
{
    internal static void Run(Action<string, Action> test, string root)
    {
        test("pedal events retain throttle brake overlap and possible clutch context with timed car response", () =>
        {
            var s = Events();
            var p = Analyze(s);
            var application = p.Events.First(e => e.Kind == "Throttle application");
            Check(application.ResponseComplete && application.AngleChangeDeg > 5 && application.YawChangeDegPerSec > 3, "Timed rotation response missing");
            Check(p.Events.Count(e => e.Kind == "Brake application") == 1 && p.Events.Count(e => e.Kind == "Brake release") == 1, "Brake edges missing");
            Check(p.Events.Single(e => e.Kind == "Throttle / brake overlap").DurationSeconds >= 1.9, "Overlap duration missing");
            var clutch = p.Events.Single(e => e.Kind == "Clutch signal cycle");
            Check(clutch.ResponseComplete && !clutch.GearChanged && clutch.RpmPeakChange >= 1000 && clutch.Evidence.Contains("Possible clutch-kick pattern"), "Clutch context missing");
            Check(p.Events.All(e => e.Confidence != "HIGH") && clutch.Evidence.Contains("not confirmed"), "Technique overclaimed");
        });
        test("pedal detection and time-weighted exposure are stable from 15 to 100 Hz", () =>
        {
            var reference = Analyze(Events(100));
            foreach (var hz in new[] { 15, 25, 50 })
            {
                var actual = Analyze(Events(hz));
                Check(actual.Events.Count == reference.Events.Count, $"Event count differs at {hz} Hz");
                for (var i = 0; i < actual.Events.Count; i++)
                    Check(actual.Events[i].Kind == reference.Events[i].Kind && Math.Abs(actual.Events[i].StartSeconds - reference.Events[i].StartSeconds) < .1, $"Event timing differs at {hz} Hz");
                foreach (var metric in reference.ContextMetrics)
                    Check(Math.Abs(actual.Metric(metric.Key)!.Value!.Value - metric.Value!.Value) < 1, $"Exposure differs at {hz} Hz: {metric.Key}");
            }
        });
        test("isolated pedal spikes and small noise do not become technique events", () =>
        {
            var s = Flat();
            foreach (var sample in s.Samples)
            {
                sample.Throttle += Math.Sin(sample.TimeSeconds * 10) * .02;
                if (Math.Abs(sample.TimeSeconds - 6) < .001) { sample.Throttle = .95; sample.Brake = .8; sample.Clutch = 0; }
            }
            Check(Analyze(s).Events.Count == 0, "A single spike became a sustained event");
        });
        test("pedal timing connects to detected initiation and transition without replacing phase measurements", () =>
        {
            var s = IntelligenceChecks.Session();
            var original = new TelemetryAnalyzer().Analyze(s);
            foreach (var f in s.Samples)
            {
                var phase = f.TimeSeconds % 25;
                f.Throttle = phase is >= 5 and < 8 ? .9 : .15;
                f.Brake = phase is >= 11.3 and < 12.3 ? .4 : 0;
            }
            var measured = new TelemetryAnalyzer().Analyze(s);
            Check(measured.Diagnosis.Pedals.Events.Any(e => e.Kind == "Throttle application" && e.Phase == "Initiation"), "Initiation not linked");
            Check(measured.Diagnosis.Pedals.Events.Any(e => e.Kind == "Brake application" && e.Phase == "Transition"), "Transition not linked");
            Check(measured.DriftEntries == original.DriftEntries && measured.TransitionCount == original.TransitionCount &&
                measured.Diagnosis.Metric("transition")!.Value == original.Diagnosis.Metric("transition")!.Value, "Pedals replaced phase measurements");
        });
        test("pedal events do not cross excluded invalid frozen reset or missing telemetry", () =>
        {
            foreach (var mode in new[] { "gap", "invalid", "frozen", "pit", "ai", "reverse", "offtrack", "impact", "reset" })
            {
                var s = Flat();
                foreach (var f in s.Samples)
                {
                    if (f.TimeSeconds >= 6) f.Throttle = .9;
                    if (mode == "impact" && f.TimeSeconds >= 5.9) f.DamageTotal = 1;
                    if (mode == "reset" && f.TimeSeconds >= 5.9) { f.TimeSeconds -= 5.9; continue; }
                    if (f.TimeSeconds < 5.9 || f.TimeSeconds > 6.3) continue;
                    switch (mode)
                    {
                        case "invalid": f.InvalidSourceSignals = true; break;
                        case "frozen": f.PacketId = 295; break;
                        case "pit": f.PitLimiterOn = true; break;
                        case "ai": f.IsAiControlled = true; break;
                        case "reverse": f.Gear = 0; break;
                        case "offtrack": f.WheelsOutsideTrack = 4; break;
                    }
                }
                if (mode == "gap") s.Samples.RemoveAll(f => f.TimeSeconds >= 5.9 && f.TimeSeconds <= 6.3);
                Check(Analyze(s).Events.Count == 0, $"Pedal event crossed {mode}");
            }
        });
        test("truncated response windows and sparse recording do not produce confident pedal response", () =>
        {
            var s = Flat();
            foreach (var f in s.Samples.Where(f => f.TimeSeconds >= 5.8)) f.Throttle = .9;
            s.Samples.RemoveAll(f => f.TimeSeconds >= 6.1 && f.TimeSeconds <= 6.7);
            var e = Analyze(s).Events.Single();
            Check(!e.ResponseComplete && e.AngleChangeDeg is null && e.Confidence == "LOW", "Response bridged a gap");
            Check(Analyze(Events(5)).Events.All(e => !e.ResponseComplete && e.Confidence == "LOW"), "Sparse recording produced response certainty");
        });
        test("clutch shifts missing RPM incomplete cycles and raw polarity remain qualified", () =>
        {
            foreach (var mode in new[] { "shift", "rpm", "incomplete", "inverted" })
            {
                var s = Flat();
                foreach (var f in s.Samples)
                {
                    f.Clutch = f.TimeSeconds >= 5 && (mode == "incomplete" || f.TimeSeconds < 5.35) ? 0 : 1;
                    f.Throttle = .8;
                    f.Rpm = mode == "rpm" ? 0 : f.TimeSeconds >= 5 && f.TimeSeconds < 5.5 ? 6500 : 5000;
                    if (mode == "shift" && f.TimeSeconds >= 5.2) f.Gear = 4;
                    if (mode == "inverted") f.Clutch = 1 - f.Clutch;
                }
                var events = Analyze(s).Events.Where(e => e.Kind == "Clutch signal cycle").ToList();
                if (mode == "incomplete") Check(events.Count == 0, "Incomplete clutch cycle counted");
                else
                {
                    var e = events.Single();
                    if (mode == "shift") Check(e.GearChanged && !e.Evidence.Contains("Possible clutch-kick"), "Shift called a kick");
                    if (mode == "rpm") Check(e.RpmPeakChange is null && !e.Evidence.Contains("Possible clutch-kick"), "Missing RPM called a kick");
                    if (mode == "inverted") Check(e.Input.Contains("raw direction") && e.Evidence.Contains("not confirmed"), "Raw polarity interpreted as physical pedal state");
                }
            }
        });
        test("pedal exposure ignores idle time and missing wheel-slip signals stay unknown", () =>
        {
            var s = Events();
            foreach (var f in s.Samples) { f.FrontWheelSlipAvg = f.RearWheelSlipAvg = 0; }
            var baseline = Analyze(s);
            for (int i = 2000; i < 3000; i++) s.Samples.Add(new TelemetrySample { TimeSeconds = i / 50.0, PacketId = i + 1, SpeedKmh = 0, Throttle = 1, Brake = 1, Clutch = 0, HasExtendedSignals = true, Gear = 3 });
            var addedIdle = Analyze(s);
            Check(baseline.Metric("brake-active")!.Value == addedIdle.Metric("brake-active")!.Value, "Idle changed drift exposure");
            Check(baseline.Events.Where(e => e.ResponseComplete).All(e => e.RearSlipChange is null), "Missing slip invented");
        });
        test("run comparison rejects different throttle patterns even at the same average input", () =>
        {
            var (a, b) = Pair();
            foreach (var f in a.Session.Samples) f.Throttle = .5;
            foreach (var f in b.Session.Samples) f.Throttle = f.TimeSeconds % 2 < 1 ? .1 : .9;
            Reanalyze(a, b);
            Check(Math.Abs(a.Analysis.Diagnosis.Metric("throttle")!.Value!.Value - b.Analysis.Diagnosis.Metric("throttle")!.Value!.Value) < .01, "Fixture means not equal");
            var comparison = new RunComparisonEngine().Compare(a, b);
            Check(!comparison.Comparable && comparison.Verdict == "Inconclusive" && !comparison.RecommendationTestTracked, "Different technique credited as tune improvement");
            Check(comparison.Limitations.Any(x => x.Contains("throttle", StringComparison.OrdinalIgnoreCase)) && comparison.Metrics.Any(m => m.Metric.StartsWith("Pedals:")), "Missing visible reason/input differences");
        });
        test("run comparison catches changed braking overlap and clutch exposure", () =>
        {
            foreach (var mode in new[] { "brake", "overlap", "clutch" })
            {
                var (a, b) = Pair();
                foreach (var f in a.Session.Samples.Concat(b.Session.Samples)) f.Throttle = .75;
                foreach (var f in b.Session.Samples)
                    if (mode == "clutch") f.Clutch = .4;
                    else f.Brake = mode == "brake" ? .5 : .18;
                Reanalyze(a, b);
                var c = new RunComparisonEngine().Compare(a, b);
                Check(!c.Comparable && c.Limitations.Any(x => x.Contains(mode == "clutch" ? "clutch" : mode == "overlap" ? "overlap" : "Brak", StringComparison.OrdinalIgnoreCase)), $"Changed {mode} passed comparison");
            }
        });
        test("clutch cycle frequency catches changed technique with similar average clutch exposure", () =>
        {
            var (a, b) = Pair();
            foreach (var f in a.Session.Samples) f.Clutch = f.TimeSeconds % 4 < .6 ? 0 : 1;
            foreach (var f in b.Session.Samples) f.Clutch = f.TimeSeconds % 1.2 < .18 ? 0 : 1;
            Reanalyze(a, b);
            Check(Math.Abs(a.Analysis.Diagnosis.Pedals.Metric("clutch-mean")!.Value!.Value - b.Analysis.Diagnosis.Pedals.Metric("clutch-mean")!.Value!.Value) < 2, "Fixture average clutch signals differ");
            var c = new RunComparisonEngine().Compare(a, b);
            Check(!c.Comparable && c.Limitations.Any(x => x.StartsWith("Clutch signal cycles")), "Different clutch timing credited to setup");
        });
        test("matched pedal use preserves comparison scoring and pedal rows never become goal scores", () =>
        {
            var s = IntelligenceChecks.Session();
            var a = new SavedTelemetrySession { Session = s };
            var b = new SavedTelemetrySession { Session = RunHistoryStore.Clone(s) };
            b.Session.Id = Guid.NewGuid().ToString("N"); b.Session.StartedUtc = s.StartedUtc.AddMinutes(5);
            Reanalyze(a, b);
            var c = new RunComparisonEngine().Compare(a, b);
            Check(c.Comparable && c.Verdict == "No clear change", "Matched inputs broke comparison");
            Check(c.Metrics.Where(m => m.Metric.StartsWith("Pedals:")).All(m => m.Interpretation.Contains("not an improvement score")), "Pedals scored as tuning goals");
            Check(a.Analysis.Diagnosis.Metrics.All(m => !a.Analysis.Diagnosis.Pedals.ContextMetrics.Any(p => p.Key == m.Key)), "Pedal context leaked into goal metrics");
        });
        test("pedal guidance uses recorded car goals and preserves calibration response guidance and raw telemetry", () =>
        {
            var s = Events();
            s.Context!.Tune!.DesiredBehavior = new CarBehaviorTarget { RearGrip = 2, InitiationSharpness = -2 };
            var saved = new SavedTelemetrySession { Session = s, Analysis = new TelemetryAnalyzer().Analyze(s) };
            var json = JsonSerializer.Serialize(s);
            var calibration = JsonSerializer.Serialize(saved.Analysis.CalibrationSuggestion);
            var input = new TuneInput { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
            var report = new DriftAssistantReportBuilder().Build(input, new CarBehaviorTarget { RearGrip = -2, InitiationSharpness = 2 }, saved, null);
            Check(report.Assessments.Any(a => a.Behavior == "Throttle and rear response" && a.Desired == "More planted rear"), "Current goals replaced saved rear goal");
            Check(report.Assessments.Any(a => a.Behavior == "Clutch and initiation" && a.Desired == "Progressive initiation"), "Wrong initiation goal");
            Check(JsonSerializer.Serialize(report.ProposedCalibration) == calibration && JsonSerializer.Serialize(s) == json, "Pedals changed calibration or raw session");
            Check(report.Recommendations.Any(r => r.Area == RecommendationArea.General && r.Change.Contains("planted-rear")), "Missing user guidance");
            Check(report.Assessments.Single(a => a.Behavior == "Braking and balance").Confidence == "LOW", "One braking episode counted as repeated applications");
        });
        test("older recordings reanalyze without file changes and unknown pedal coverage blocks attribution", () =>
        {
            var (a, b) = Pair();
            foreach (var f in a.Session.Samples) f.HasExtendedSignals = false;
            Reanalyze(a, b);
            var dir = Path.Combine(root, "pedal-legacy"); Directory.CreateDirectory(dir);
            var store = new TelemetrySessionStore();
            typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, dir);
            var (path, _) = store.Save(a.Session, a.Analysis);
            var bytes = File.ReadAllBytes(path);
            var reopened = store.TryLoad(path)!;
            Check(reopened.Analysis.Diagnosis.Pedals.ContextMetrics.Count == 11 && File.ReadAllBytes(path).SequenceEqual(bytes), "Reanalysis lost context or rewrote raw data");
            var c = new RunComparisonEngine().Compare(a, b);
            Check(!c.Comparable && c.Limitations.Any(x => x.Contains("coverage")), "Unknown coverage accepted");
            Check(a.Analysis.Diagnosis.Pedals.Limitations.Contains("Legacy"), "Unknown coverage not explained");
        });
    }

    private static PedalDiagnosis Analyze(TelemetrySession s) => new TelemetryAnalyzer().Analyze(s).Diagnosis.Pedals;
    private static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static void Reanalyze(params SavedTelemetrySession[] runs) { foreach (var run in runs) run.Analysis = new TelemetryAnalyzer().Analyze(run.Session); }
    private static (SavedTelemetrySession, SavedTelemetrySession) Pair()
    {
        var s = Flat();
        var next = RunHistoryStore.Clone(s); next.Id = Guid.NewGuid().ToString("N"); next.StartedUtc = s.StartedUtc.AddMinutes(5);
        return (new SavedTelemetrySession { Session = s }, new SavedTelemetrySession { Session = next });
    }
    private static TelemetrySession Flat(int hz = 50)
    {
        var s = IntelligenceChecks.Session(hz);
        s.Samples.RemoveAll(f => f.TimeSeconds >= 40);
        foreach (var f in s.Samples)
        {
            f.SlipAngleDeg = 30; f.YawRateDegPerSec = 21; f.SteeringAngleDeg = -60; f.SteeringRateDegPerSec = 0;
            f.Throttle = .15; f.Brake = 0; f.Clutch = 1; f.Gear = 3; f.Rpm = 5000;
        }
        return s;
    }
    private static TelemetrySession Events(int hz = 50)
    {
        var s = Flat(hz);
        foreach (var f in s.Samples)
        {
            var t = f.TimeSeconds;
            if (t >= 5 && t < 8 || t >= 12 && t < 14) f.Throttle = .9;
            if (t >= 5.15 && t < 8.3) { f.SlipAngleDeg = 40; f.YawRateDegPerSec = 28; f.RearWheelSlipAvg = 5; }
            if (t >= 12 && t < 14) f.Brake = .5;
            if (t >= 20 && t < 20.35) f.Clutch = 0;
            if (t >= 20 && t < 20.5) { f.Rpm = 6500; f.Throttle = .5; }
        }
        return s;
    }
}
