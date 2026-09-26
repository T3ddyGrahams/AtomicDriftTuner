using System.IO;
using System.Reflection;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class DrivingContextChecks
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    public static void Run(Action<string, Action> test, string root)
    {
        test("driving context partitions complete phases and cumulative short drift sections", () =>
        {
            var s = Fixture(); var a = new TelemetryAnalyzer().Analyze(s); var rows = a.Diagnosis.DrivingContext.Observations;
            Check(rows.Count is > 10 and < 100 && rows.All(r => r.EvidenceSeconds > 0 && (r.Value is null || double.IsFinite(r.Value.Value))), "Missing or unbounded observations");
            foreach (var key in new[] { "initiation", "transition" })
                Check(rows.Where(r => r.MetricKey == key).Sum(r => r.Events) == a.Diagnosis.Metric(key)!.Events, "Event evidence duplicated or lost");
            Check(rows.Count(r => r.MetricKey == "rear-slip-share" && r.Confidence == "MEDIUM") == 2, "Short sections did not accumulate by direction");
            Check(rows.Where(r => r.MetricKey == "transition").Select(r => r.Direction).Distinct().Count() == 2, "Transition direction collapsed");
            Check(rows.All(r => r.Confidence != "HIGH"), "Context proxies overstate confidence");
        });
        test("driving context separates speed bands without relabeling cross-band entries", () =>
        {
            var s = Fixture(); foreach (var f in s.Samples) f.SpeedKmh = f.SlipAngleDeg < 0 ? 40 : 100;
            var rows = new TelemetryAnalyzer().Analyze(s).Diagnosis.DrivingContext.Observations;
            Check(rows.Any(r => r.MetricKey == "rear-slip-share" && r.SpeedBand == 0 && r.Direction == "Left") &&
                rows.Any(r => r.MetricKey == "rear-slip-share" && r.SpeedBand == 2 && r.Direction == "Right"), "Speed/direction grouping incorrect");
            Check(rows.Where(r => r.MetricKey == "transition").All(r => r.SpeedBand == 3), "Multi-speed transition presented as one speed band");
        });
        test("driving context has stable time weighting across sample rates", () =>
        {
            var left = new TelemetryAnalyzer().Analyze(Fixture(25)).Diagnosis.DrivingContext.Observations;
            var right = new TelemetryAnalyzer().Analyze(Fixture(100)).Diagnosis.DrivingContext.Observations;
            foreach (var row in left.Where(r => r.Value is not null && r.MetricKey is "rear-slip-share" or "clipping" or "stability"))
            {
                var other = right.Single(r => r.MetricKey == row.MetricKey && r.Context == row.Context);
                Check(Math.Abs(row.Value!.Value - other.Value!.Value) < .001, "Sample count changed a time-weighted mean");
                Check(Math.Abs(row.EvidenceSeconds - other.EvidenceSeconds) < 1, "Evidence duration scales with sample count");
            }
        });
        test("driving context excludes bad slip and backward evidence but retains other motion", () =>
        {
            var s = Fixture(); foreach (var f in s.Samples) { f.InvalidWheelSlipSignals = true; f.FrontWheelSlipAvg = double.NaN; }
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(!a.Diagnosis.DrivingContext.Observations.Any(r => r.MetricKey.Contains("slip-share")) &&
                a.Diagnosis.DrivingContext.Observations.Any(r => r.MetricKey == "transition" && r.Value is not null), "Invalid slip contaminated independent motion evidence");
            foreach (var f in s.Samples) f.LongitudinalVelocityMs = -1;
            Check(new TelemetryAnalyzer().Analyze(s).Diagnosis.DrivingContext.Observations.Count == 0, "Backward motion entered contextual tuning evidence");
        });
        test("driving context never differentiates across gaps or speed/direction boundaries", () =>
        {
            var s = Fixture(); s.Samples.Clear();
            for (int i = 0; i < 2000; i++)
            {
                int block = i / 250; double angle = block % 2 == 0 ? 25 : 55;
                s.Samples.Add(new() { TimeSeconds = i / 50d + block, PacketId = i + 1, SpeedKmh = 60, SlipAngleDeg = angle,
                    SteeringAngleDeg = -50, YawRateDegPerSec = 20, Throttle = .7, Clutch = 1, Gear = 2, HasExtendedSignals = true,
                    FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, LongitudinalVelocityMs = 5 });
            }
            var rows = new TelemetryAnalyzer().Analyze(s).Diagnosis.DrivingContext.Observations;
            Check(rows.Single(r => r.MetricKey == "stability").Value == 0, "Disjoint constant-angle blocks produced false variation");
            Check(rows.Single(r => r.MetricKey == "rear-slip-share").Confidence == "MEDIUM", "Repeated short sections require continuous ten-second drift");
        });
        test("driving context distinguishes missing pedal signals and travel direction from zero", () =>
        {
            var s = Fixture(); foreach (var f in s.Samples) { f.HasExtendedSignals = false; f.LongitudinalVelocityMs = null; }
            var rows = new TelemetryAnalyzer().Analyze(s).Diagnosis.DrivingContext.Observations;
            Check(rows.Count > 0 && rows.All(r => r.ThrottlePct is null && r.BrakePct is null && r.ClutchSignalPct is null && r.UnknownTravelSeconds > 0), "Missing channels became verified zero inputs or forward travel");
        });
        test("opposing transition conditions request a matching repeat instead of whole-car acceleration", () =>
        {
            var s = Fixture(asymmetric: true); var original = JsonSerializer.Serialize(s.Context); var run = Saved(s);
            var report = new DriftAssistantReportBuilder().Build(Input(), s.Context!.Tune!.DesiredBehavior, run, null);
            Check(report.Recommendations.Any(r => r.MetricKey == "transition" && r.Priority == "Repeat inputs" && r.Area == RecommendationArea.General), "Conflicting timing conditions did not block blanket tuning");
            Check(!report.Recommendations.Any(r => r.MetricKey == "transition" && r.Area == RecommendationArea.CarSetup) &&
                report.SuggestedBehaviorTarget.TransitionSpeed == s.Context.Tune.DesiredBehavior.TransitionSpeed, "Mixed direction timing sped up all transitions");
            Check(report.ContextAssessments.Any(r => r.Status.StartsWith("FASTER")) && report.ContextAssessments.Any(r => r.Status.StartsWith("SLOWER")), "Opposing goal results hidden by average");
            Check(JsonSerializer.Serialize(s.Context) == original, "Diagnosis rewrote driver goals/history");
        });
        test("contextual setup guidance names the repeat condition and keeps saved goals", () =>
        {
            var s = Fixture(); s.Context!.Tune!.DesiredBehavior.RearGrip = 2;
            var report = new DriftAssistantReportBuilder().Build(Input(), new() { RearGrip = -2 }, Saved(s), null);
            var rear = report.Recommendations.Single(r => r.MetricKey == "rear-slip-share" && r.Area == RecommendationArea.CarSetup);
            Check(rear.DrivingContext.Contains("Sustained", StringComparison.OrdinalIgnoreCase) && rear.Change.Contains("km/h") && rear.Why.Contains("Condition evidence"), "Guidance lacks repeatable condition");
            Check(report.ContextAssessments.Where(r => r.Behavior.StartsWith("Rear slip")).All(r => r.Desired.Contains("planted")), "Current goal replaced recorded goal");
            Check(report.NextStep.Confidence == "New baseline needed", "Changed-goal workflow bypassed");
        });
        test("supported timing cannot authorize the opposite of a sparse whole-run finding", () =>
        {
            var s = Fixture(asymmetric: true);
            foreach (var f in s.Samples)
                if (f.TimeSeconds % 25 is >= 17 and < 20) f.SpeedKmh = f.TimeSeconds < 50 ? 40 : 100;
            var run = Saved(s);
            var buckets = run.Analysis.Diagnosis.DrivingContext.Observations.Where(o => o.MetricKey == "transition").ToArray();
            Check(buckets.Count(o => o.Confidence == "MEDIUM") == 1 && buckets.Count(o => o.Confidence == "LOW") == 2, "Fixture does not isolate one supported condition");
            var report = new DriftAssistantReportBuilder().Build(Input(), s.Context!.Tune!.DesiredBehavior, run, null);
            Check(!report.Recommendations.Any(r => r.MetricKey == "transition" && r.Area == RecommendationArea.CarSetup) &&
                report.Recommendations.Any(r => r.MetricKey == "transition" && r.Priority == "Repeat inputs"), "Sparse average overruled the matching supported condition");
        });
        test("context guidance caps poor-quality confidence and preserves legacy fallbacks", () =>
        {
            var s = Fixture(); s.Context!.Tune!.DesiredBehavior.RearGrip = 2; s.Context.Interrupted = true;
            var run = Saved(s); var builder = new DriftAssistantReportBuilder();
            var report = builder.Build(Input(), s.Context.Tune.DesiredBehavior, run, null);
            Check(report.ContextAssessments.Count > 0 && report.ContextAssessments.All(a => a.Confidence == "LOW") &&
                report.DrivingContextSummary.Contains("inspection only") && report.NextStep.Confidence == "Another clean run needed", "Poor-quality run received contextual tuning confidence");
            Check(!report.Recommendations.Any(r => r.Area == RecommendationArea.CarSetup && r.Confidence != "LOW"), "Context promoted unreliable evidence into a setup test");
            run.Analysis.Diagnosis.DrivingContext = new();
            Check(builder.Build(Input(), s.Context.Tune.DesiredBehavior, run, null).ContextAssessments.Count == 0, "Legacy empty context produced invented measurements");
        });
        test("mph display does not change context grouping or tuning decisions", () =>
        {
            var s = Fixture(); s.Context!.Tune!.DesiredBehavior.RearGrip = 2; var run = Saved(s); var builder = new DriftAssistantReportBuilder();
            var before = JsonSerializer.Serialize(run);
            var kmh = builder.Build(Input(), s.Context.Tune.DesiredBehavior, run, null);
            var mph = builder.Build(Input(), s.Context.Tune.DesiredBehavior, run, null, displayMph: true);
            Check(mph.ContextAssessments.All(a => a.Behavior.Contains("mph")) && mph.Recommendations.Any(r => r.DrivingContext.Contains("mph")), "MPH preference lost from guidance");
            Check(mph.ContextAssessments.Select(a => a.Observed).SequenceEqual(kmh.ContextAssessments.Select(a => a.Observed)) &&
                RunHistoryStore.SameBehavior(kmh.SuggestedBehaviorTarget, mph.SuggestedBehaviorTarget) && before == JsonSerializer.Serialize(run), "Display units changed analysis or saved history");
        });
        test("large pedal differences prevent a grip setup conclusion across unmatched conditions", () =>
        {
            var s = Fixture(); s.Context!.Tune!.DesiredBehavior.RearGrip = 2;
            foreach (var f in s.Samples) f.Throttle = f.SlipAngleDeg < 0 ? .95 : .3;
            var report = new DriftAssistantReportBuilder().Build(Input(), s.Context.Tune.DesiredBehavior, Saved(s), null);
            Check(report.Recommendations.Any(r => r.MetricKey == "rear-slip-share" && r.Area == RecommendationArea.General && r.Change.Contains("Pedal use differs")), "Pedal difference became a grip diagnosis");
            Check(!report.Recommendations.Any(r => r.MetricKey == "rear-slip-share" && r.Area == RecommendationArea.CarSetup), "Grip test was not held");
        });
        test("sparse contextual buckets do not borrow confidence from the entire run", () =>
        {
            var s = Fixture(); s.Samples.RemoveAll(f => f.TimeSeconds >= 25); var rows = new TelemetryAnalyzer().Analyze(s).Diagnosis.DrivingContext.Observations;
            Check(rows.Where(r => r.MetricKey is "transition" or "initiation").All(r => r.Value is null && r.Confidence == "LOW"), "Single event became repeatable context");
            Check(rows.Any(r => r.MetricKey == "rear-slip-share" && r.Confidence == "LOW"), "Sparse direction borrowed all-run exposure");
        });
        test("context reanalysis persists its version and leaves whole-run metrics and calibration unchanged", () =>
        {
            var s = Fixture(); var full = new DriftDiagnosisEngine().Analyze(s); var live = new DriftDiagnosisEngine().Analyze(s, includePowertrain: false);
            Check(JsonSerializer.Serialize(full.Diagnosis.Metrics) == JsonSerializer.Serialize(live.Diagnosis.Metrics) &&
                JsonSerializer.Serialize(full.CalibrationSuggestion) == JsonSerializer.Serialize(live.CalibrationSuggestion), "Context altered aggregate measurements or FFB");
            Check(live.Diagnosis.DrivingContext.Observations.Count == 0, "Expensive context work entered live readiness path");
            var store = new TelemetrySessionStore();
            typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, Path.Combine(root, "context-history"));
            var saved = store.Save(s, full); var reread = store.TryLoad(saved.JsonPath)!;
            Check(reread.Analysis.Diagnosis.AnalyzerVersion == "drift-diagnosis/5" &&
                reread.Analysis.Diagnosis.DrivingContext.Version == "driving-context/1" && reread.Analysis.Diagnosis.DrivingContext.Observations.Count == full.Diagnosis.DrivingContext.Observations.Count, "Saved run did not reanalyze context");
        });
        test("dense recordings produce bounded condition tables", () =>
        {
            var s = Fixture(500); var timer = System.Diagnostics.Stopwatch.StartNew();
            var a = new TelemetryAnalyzer().Analyze(s); timer.Stop();
            Check(s.Samples.Count == 50000 && a.Diagnosis.DrivingContext.Observations.Count is > 10 and < 100, "Context rows grow with frame count");
            Console.WriteLine($"CONTEXT BENCHMARK: {s.Samples.Count} frames analyzed in {timer.Elapsed.TotalSeconds:0.000}s; {a.Diagnosis.DrivingContext.Observations.Count} condition rows.");
        });
    }
    internal static TelemetrySession Fixture(int hz = 50, bool asymmetric = false)
    {
        var s = IntelligenceChecks.Session(hz);
        foreach (var f in s.Samples) f.LongitudinalVelocityMs = 8;
        if (asymmetric)
        {
            double previous = 0;
            foreach (var f in s.Samples)
            {
                var p = f.TimeSeconds % 25;
                f.SlipAngleDeg = p < 5 ? 0 : p < 6 ? (p - 5) * 30 : p < 11 ? 30 : p < 11.3 ? 30 - (p - 11) * 200 : p < 17 ? -30 : p < 20 ? -30 + (p - 17) * 20 : p < 22 ? 30 : 0;
                f.SteeringAngleDeg = -2 * f.SlipAngleDeg; f.SteeringRateDegPerSec = (f.SteeringAngleDeg - previous) * hz;
                f.YawRateDegPerSec = f.SlipAngleDeg * .7; previous = f.SteeringAngleDeg;
            }
        }
        return s;
    }
    internal static SavedTelemetrySession Saved(TelemetrySession s) => new() { Session = s, Analysis = new TelemetryAnalyzer().Analyze(s) };
    private static TuneInput Input() => new() { Car = BuiltInProfiles.Cars()[0], Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Intent = BuiltInProfiles.Intents()[1] };
}
