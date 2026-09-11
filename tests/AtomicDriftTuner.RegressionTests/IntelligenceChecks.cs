using System.IO;
using System.Reflection;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class IntelligenceChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("intelligence detects complete entries and both transition directions", () =>
        {
            var session = Session();
            var a = new TelemetryAnalyzer().Analyze(session);
            Check(a.DriftEntries == 4, $"Expected 4 entries, got {a.DriftEntries}");
            Check(a.TransitionCount == 8, $"Expected 8 transitions, got {a.TransitionCount}");
            Check(a.Diagnosis.Metric("initiation")?.Value > 0 && a.Diagnosis.Metric("transition")?.Value > 0, "Phase metrics missing");
            Check(a.OscillationEvents == 0, "Ordinary transitions counted as oscillation");
        });
        test("intelligence does not join transitions across missing data", () =>
        {
            var s = Session();
            var before = new TelemetryAnalyzer().Analyze(s);
            foreach (var f in s.Samples.Where(f => f.SlipAngleDeg < 0)) f.TimeSeconds += 1000;
            var after = new TelemetryAnalyzer().Analyze(s);
            Check(after.TransitionCount < before.TransitionCount && after.Diagnosis.Discontinuities > 0, "A transition crossed a gap");
        });
        test("intelligence rejects frozen packets and nonfinite motion", () =>
        {
            var s = Session();
            foreach (var f in s.Samples) f.PacketId = 7;
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.DriftTimeSeconds == 0 && a.TransitionCount == 0, "Frozen packets produced evidence");
            foreach (var f in s.Samples) f.SlipAngleDeg = double.NaN;
            a = new TelemetryAnalyzer().Analyze(s);
            Check(a.SampleCount == 0 && a.CalibrationSuggestion.IsNeutral, "Invalid motion produced a correction");
        });
        test("intelligence excludes pit reverse AI and off-track samples", () =>
        {
            foreach (var kind in new[] { "pit", "reverse", "ai", "off-track" })
            {
                var s = Session();
                foreach (var f in s.Samples)
                { f.HasExtendedSignals = true; f.Gear = kind == "reverse" ? 0 : 2; f.PitLimiterOn = kind == "pit"; f.IsAiControlled = kind == "ai"; f.WheelsOutsideTrack = kind == "off-track" ? 4 : 0; }
                var a = new TelemetryAnalyzer().Analyze(s);
                Check(a.DriftTimeSeconds == 0 && a.CalibrationSuggestion.IsNeutral, kind + " contributed usable evidence");
            }
        });
        test("intelligence separates oscillation from phase steering", () =>
        {
            var s = Session(oscillate: true);
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.OscillationEvents > 0, "Sustained repeated steering reversals were missed");
            Check(a.CalibrationSuggestion.WheelSpeedDelta == 0 && a.CalibrationSuggestion.DampingDelta == 0, "Driver-influenced motion became automatic hardware correction");
        });
        test("intelligence is stable across 25Hz and 50Hz sampling", () =>
        {
            var a = new TelemetryAnalyzer().Analyze(Session(hz: 25));
            var b = new TelemetryAnalyzer().Analyze(Session(hz: 50));
            Check(a.TransitionCount == b.TransitionCount && a.DriftEntries == b.DriftEntries, "Phase counts depend on sample rate");
            Check(Math.Abs(a.AverageTransitionSeconds - b.AverageTransitionSeconds) < .06, "Transition timing drifted across rates");
            Check(Math.Abs(a.DriftTimeSeconds - b.DriftTimeSeconds) < .5, "Time weighting differs across rates");
        });
        test("intelligence keeps missing axle-slip evidence unknown", () =>
        {
            var s = Session(); foreach (var f in s.Samples) f.FrontWheelSlipAvg = f.RearWheelSlipAvg = 0;
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.Diagnosis.Metric("front-slip-share")?.Value is null && a.Diagnosis.Metric("rear-slip-share")?.Value is null, "Missing slip was treated as perfect grip");
        });
        test("comparison rejects mismatched and missing run context", () =>
        {
            foreach (var kind in new[] { "driver", "car", "track", "conditions", "hardware", "goals", "legacy", "interrupted", "same-run" })
            {
                var (a, b) = Pair();
                switch (kind)
                {
                    case "driver": b.Session.Context!.DriverId = Guid.NewGuid().ToString("N"); break;
                    case "car": b.Session.CarFolder = "other-car"; break;
                    case "track": b.Session.Context!.TrackId = "other-track"; break;
                    case "conditions": b.Session.Context!.Conditions = "wet tandem"; break;
                    case "hardware": b.Session.Wheelbase = "other-wheelbase"; break;
                    case "goals": b.Session.Context!.Tune!.DesiredBehavior.TransitionSpeed = -2; break;
                    case "legacy": b.Session.Context = null; break;
                    case "interrupted": b.Session.Context!.Interrupted = true; break;
                    case "same-run": b.Session.Id = a.Session.Id; break;
                }
                var c = new RunComparisonEngine().Compare(a, b);
                Check(!c.Comparable && c.Verdict == "Inconclusive", kind + " received a verdict");
            }
        });
        test("comparison scores closer goals and detects control tradeoffs", () =>
        {
            var (a, b) = Pair();
            Set(a, "transition", 1.6); Set(b, "transition", .55);
            var c = new RunComparisonEngine().Compare(a, b);
            Check(c.Comparable && c.Verdict == "Closer to goals", c.Summary + " " + string.Join("; ", c.Limitations));
            Set(b, "oscillation", 5);
            c = new RunComparisonEngine().Compare(a, b);
            Check(c.Verdict == "Tradeoff", "Faster but more oscillatory was reported as simple improvement");
        });
        test("comparison honors a slower desired transition rather than rewarding speed", () =>
        {
            var (a, b) = Pair();
            a.Session.Context!.Tune!.DesiredBehavior.TransitionSpeed = b.Session.Context!.Tune!.DesiredBehavior.TransitionSpeed = -2;
            Set(a, "transition", 1.3); Set(b, "transition", .5);
            Check(new RunComparisonEngine().Compare(a, b).Verdict == "Farther from goals", "Faster was rewarded against a slower target");
        });
        test("comparison does not attribute an unconfirmed or untracked tune", () =>
        {
            var (a, b) = Pair();
            b.Session.Context!.TuneConfirmedInUse = false;
            var c = new RunComparisonEngine().Compare(a, b);
            Check(!c.RecommendationTestTracked, "An unconfirmed generated tune was treated as applied");
            b.Session.Context.TuneConfirmedInUse = true; b.Session.Context.RecommendationSessionId = "";
            Check(!new RunComparisonEngine().Compare(a, b).RecommendationTestTracked, "Untracked recommendation attributed");
        });
        test("driver feedback can disagree with telemetry without being overwritten", () =>
        {
            var review = new RunReview { DriverRating = "Worse", Comparison = new RunComparison { Comparable = true, Verdict = "Closer to goals", RecommendationTestTracked = true } };
            Check(review.Conclusion.Contains("disagree"), "Driver disagreement disappeared");
        });
        test("report preserves historical desired behavior and caller objects", () =>
        {
            var (a, _) = Pair(); var current = new CarBehaviorTarget { TransitionSpeed = -2 };
            var original = JsonSerializer.Serialize(a.Session.Context!.Tune!.DesiredBehavior);
            var report = new TelemetryTuningAssistantEngine().Build(Input(), current, a);
            Check(report.Assessments.First(x => x.Behavior == "Transition crossover time").Desired.Contains("Quicker"), "Report used today's goals for a historical run");
            Check(current.TransitionSpeed == -2 && JsonSerializer.Serialize(a.Session.Context.Tune.DesiredBehavior) == original, "Analysis changed saved goals");
        });
        test("history versions are immutable and isolated by driver and car", () =>
        {
            var store = new RunHistoryStore(Path.Combine(root, "history")); var input = Input();
            var driver = store.GetOrCreateDriver("Driver one");
            Check(store.GetOrCreateDriver("  DRIVER ONE ").Id == driver.Id, "Driver identity did not persist");
            var goal = new CarBehaviorTarget { RearGrip = 1 };
            var tune = store.CaptureTune(input, driver, "Baseline", goal, null);
            goal.RearGrip = -2;
            Check(store.ListTunes(input, driver.Id).Single().DesiredBehavior.RearGrip == 1, "Snapshot changed with caller");
            var refused = false; try { store.SaveTune(tune); } catch (IOException) { refused = true; }
            Check(refused, "Existing tune version was overwritten");
            Check(store.ListTunes(input, Guid.NewGuid().ToString("N")).Count == 0, "Driver histories leaked");
            input.Car.Id = "different-car"; input.Car.SourceFolderName = "different-car";
            Check(store.ListTunes(input, driver.Id).Count == 0, "Car histories leaked");
        });
        test("history preserves corruption and loads unrelated good versions", () =>
        {
            var dir = Path.Combine(root, "history-corrupt"); var store = new RunHistoryStore(dir); var driver = store.GetOrCreateDriver("Driver");
            store.CaptureTune(Input(), driver, "Good", new(), null);
            var broken = Path.Combine(dir, "tunes", "broken.json"); File.WriteAllText(broken, "{");
            File.WriteAllText(Path.Combine(dir, "tunes", "null.json"), "{\"Schema\":\"adt/tune-version/1\",\"Settings\":null}");
            Check(store.ListTunes(Input(), driver.Id).Count == 1 && File.ReadAllText(broken) == "{", "Corruption blocked or replaced good history");
        });
        test("run context survives session save reload and old sessions still load", () =>
        {
            var store = new TelemetrySessionStore();
            typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, Path.Combine(root, "intelligence-sessions"));
            var s = Session(); var a = new TelemetryAnalyzer().Analyze(s);
            var path = store.Save(s, a).JsonPath;
            var loaded = store.TryLoad(path)!;
            Check(loaded.Session.Context?.DriverId == s.Context?.DriverId && loaded.Analysis.Diagnosis.Events.Count > 0, "New metadata was lost");
            s.Context = null; path = store.Save(s, a).JsonPath;
            Check(store.TryLoad(path)?.Session.Context is null, "Legacy context was invented");
        });
        test("impact cooldown and recording resets cannot authorize a correction", () =>
        {
            var s = Session();
            foreach (var f in s.Samples.Where(f => f.TimeSeconds >= 8)) f.DamageTotal = 1;
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.Diagnosis.ExcludedSeconds >= .99, "Impact cooldown was not excluded");
            foreach (var f in s.Samples) f.FinalFfb = 1;
            foreach (var f in s.Samples.Where(f => f.TimeSeconds >= 50)) f.TimeSeconds -= 50;
            a = new TelemetryAnalyzer().Analyze(s);
            Check(a.Diagnosis.TimelineReset && a.CalibrationSuggestion.IsNeutral, "Restarted data produced a correction");
        });
        test("sanitized source errors and sparse evidence stay untrusted", () =>
        {
            var s = Session(); foreach (var f in s.Samples) f.InvalidSourceSignals = true;
            Check(new TelemetryAnalyzer().Analyze(s).DriftTimeSeconds == 0, "Sanitized invalid source values became evidence");
            s = Session(hz: 5); foreach (var f in s.Samples) f.FinalFfb = 1;
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.CalibrationSuggestion.IsNeutral, "Sparse source data produced a correction");
        });
        test("low-confidence changes remain descriptive and unstable runs do not request faster response", () =>
        {
            var (a, b) = Pair(); Set(a, "transition", 1.6); Set(b, "transition", .55);
            b.Analysis.Diagnosis.Metric("transition")!.Confidence = "LOW";
            Check(new RunComparisonEngine().Compare(a, b).Verdict == "No clear change", "Weak phase evidence was scored");
            Set(a, "transition", 2); a.Analysis.OscillationEvents = 2;
            a.Session.Context!.Tune!.DesiredBehavior.TransitionSpeed = 0;
            var report = new TelemetryTuningAssistantEngine().Build(Input(), new(), a);
            Check(report.SuggestedBehaviorTarget.TransitionSpeed <= 0 && !report.Recommendations.Any(r => r.Change.Contains("response increase")), "Unstable run recommended more speed");
        });
        test("damaged run context preserves raw evidence without claiming known identity", () =>
        {
            var store = new TelemetrySessionStore();
            typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(store, Path.Combine(root, "damaged-context"));
            var s = Session(); s.Context!.Tune!.Settings = null!;
            var path = store.Save(s, new TelemetryAnalyzer().Analyze(s)).JsonPath;
            var loaded = store.TryLoad(path);
            Check(loaded is not null && loaded.Session.Context is null && loaded.Analysis.DriftEntries == 4, "Bad context hid valid raw telemetry");
            Check(loaded!.Analysis.Diagnosis.QualityNotes.Any(n => n.Contains("damaged")), "Bad context was silently discarded");
        });
        test("setup snapshots capture actual file differences without modifying source files", () =>
        {
            var dir = Path.Combine(root, "setup-snapshots"); Directory.CreateDirectory(dir);
            var setup = Path.Combine(dir, "baseline.ini"); File.WriteAllText(setup, "[PRESSURE_LF]\nVALUE=28\n[PRESSURE_RF]\nVALUE=28\n");
            var store = new RunHistoryStore(Path.Combine(dir, "history")); var driver = store.GetOrCreateDriver("Tester");
            var input = Input(); var original = File.ReadAllText(setup);
            var before = store.CaptureTune(input, driver, "Before", new(), null, setup);
            Check(File.ReadAllText(setup) == original && before.Settings["ACSetup.PRESSURE_LF"] == 28, "Source changed or actual value missing");
            File.WriteAllText(setup, original.Replace("VALUE=28", "VALUE=29"));
            var after = store.CaptureTune(input, driver, "After", new(), null, setup);
            Check(before.SetupSha256 != after.SetupSha256 && store.ListTunes(input, driver.Id).Single(t => t.Id == before.Id).Settings["ACSetup.PRESSURE_LF"] == 28, "Historical values were replaced");
        });
        test("reviews survive reopening and preserve conflicting revisions", () =>
        {
            var dir = Path.Combine(root, "review-history"); var store = new RunHistoryStore(dir); var driver = store.GetOrCreateDriver("Tester");
            var input = Input(); var (a, b) = Pair(); Set(a, "transition", 1.6); Set(b, "transition", .55);
            var review = new RunReview { SessionId = b.Session.Id, BaselineSessionId = a.Session.Id, DriverId = driver.Id,
                ContextKey = RunHistoryStore.ContextKey(input), DriverRating = "Better", Notes = "Easier to catch", Comparison = new RunComparisonEngine().Compare(a, b) };
            store.SaveReview(review); review.Id = Guid.NewGuid().ToString("N"); review.DriverRating = "Worse"; review.Notes = "Less predictable on another lap"; store.SaveReview(review);
            var reopened = new RunHistoryStore(dir).ListReviews(input, driver.Id);
            Check(reopened.Count == 2 && reopened.Any(r => r.Conclusion.Contains("support improvement")) && reopened.Any(r => r.Conclusion.Contains("disagree")), "History erased either supporting evidence or later disagreement");
        });
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Set(SavedTelemetrySession s, string key, double value) => s.Analysis.Diagnosis.Metrics.Single(x => x.Key == key).Value = value;
    private static TuneInput Input() => new() { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
    private static (SavedTelemetrySession, SavedTelemetrySession) Pair()
    {
        var first = Session(); var second = RunHistoryStore.Clone(first); second.Id = Guid.NewGuid().ToString("N"); second.StartedUtc = first.StartedUtc.AddMinutes(5);
        second.Context!.RecommendationSessionId = first.Id; second.Context.TestedRecommendations = ["Test a quicker transition"];
        second.Context.Tune!.Id = Guid.NewGuid().ToString("N"); second.Context.Tune.Settings["ACSetup.DAMP_REBOUND_LR"] = 8;
        return (new SavedTelemetrySession { Session = first, Analysis = new TelemetryAnalyzer().Analyze(first) }, new SavedTelemetrySession { Session = second, Analysis = new TelemetryAnalyzer().Analyze(second) });
    }
    internal static TelemetrySession Session(int hz = 50, bool oscillate = false)
    {
        var driver = Guid.NewGuid().ToString("N");
        var s = new TelemetrySession { CarFolder = "fixture-car", CarName = "Fixture Car", DriftPack = "Fixture Pack", Wheelbase = "Fixture Base", SteeringWheel = "Fixture Rim", DriftTarget = "Tandem",
            Context = new RunContext { DriverId = driver, DriverName = "Fixture driver", TrackId = "fixture-track", Conditions = "dry solo", CarIdentityVerified = true, TuneConfirmedInUse = true,
                Tune = new TuneVersion { DriverId = driver, ContextKey = "fixture-key", DesiredBehavior = new CarBehaviorTarget { TransitionSpeed = 2 }, SetupSha256 = "fixture-hash", Settings = new() { ["ACSetup.DAMP_REBOUND_LR"] = 7 } } } };
        double previousSteer = 0;
        for (int i = 0; i < 100 * hz; i++)
        {
            double t = (double)i / hz, p = t % 25;
            double angle = p < 5 ? 0 : p < 6 ? (p - 5) * 30 : p < 11 ? 30 : p < 12 ? 30 - (p - 11) * 60 : p < 17 ? -30 : p < 18 ? -30 + (p - 17) * 60 : p < 22 ? 30 : 0;
            double steer = -angle * 2 + (oscillate && Math.Abs(angle) == 30 ? Math.Sin(t * Math.PI * 8) * 12 : 0);
            s.Samples.Add(new TelemetrySample { TimeSeconds = t, PacketId = i + 1, SpeedKmh = 60, SlipAngleDeg = angle, SteeringAngleDeg = steer,
                SteeringRateDegPerSec = i == 0 ? 0 : (steer - previousSteer) * hz, YawRateDegPerSec = angle * .7, Throttle = .75, Clutch = 1, Gear = 2,
                FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, FinalFfb = .45, HasExtendedSignals = true });
            previousSteer = steer;
        }
        return s;
    }
}
