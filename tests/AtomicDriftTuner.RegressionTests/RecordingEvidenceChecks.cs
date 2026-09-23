using System.Diagnostics;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class RecordingEvidenceChecks
{
    internal static void Run(Action<string, Action> test)
    {
        test("front guidance separates normal cornering from axle slip and shows measurable progress", () =>
        {
            var session = Continuous(40, goal: new() { FrontEndBite = 1 });
            foreach (var sample in session.Samples.Where(s => s.TimeSeconds >= 37)) sample.SlipAngleDeg = 5;
            var analysis = new DriftDiagnosisEngine().Analyze(session);
            var progress = RecordingEvidenceService.FromAnalysis(session, analysis);
            Check(!progress.ReadyToReview && progress.NeededEvidence.Count == 1 && progress.Message.Contains("/ 10 s") &&
                progress.Message.Contains("normal corners") && progress.Message.Contains("30"), "Front requirement hid its progress or driving task");
            Check(progress.Details.Contains("12–120") && progress.Details.Contains("8°"), "Front acceptance conditions are missing");
            foreach (var sample in session.Samples.Where(s => s.TimeSeconds >= 28)) sample.SlipAngleDeg = 5;
            Check(new RecordingEvidenceService().Update(session, 40).ReadyToReview, "Normal cornering did not complete the front requirement");
            foreach (var sample in session.Samples) sample.InvalidWheelSlipSignals = true;
            progress = new RecordingEvidenceService().Update(session, 40);
            Check(!progress.ReadyToReview && progress.NeededEvidence.Count == 1 && progress.Message.Contains("Axle-slip") &&
                !progress.Message.Contains("normal corners"), "Missing wheel-slip evidence was mislabeled as missing normal cornering");
        });
        test("a long useful run allows partial review without inventing front-response evidence", () =>
        {
            var session = Continuous(121, goal: new() { FrontEndBite = 1 });
            var original = JsonSerializer.Serialize(session);
            var analysis = new DriftDiagnosisEngine().Analyze(session);
            var originalAnalysis = JsonSerializer.Serialize(analysis);
            var progress = RecordingEvidenceService.FromAnalysis(session, analysis);
            Check(progress.ReadyToReview && progress.State == "ready" && progress.Heading == "READY FOR PARTIAL REVIEW" && progress.NeededEvidence.Count == 1,
                "Useful drift remained stuck behind missing normal cornering");
            Check(progress.Message.Contains("Stop and save") && progress.Message.Contains(progress.NeededEvidence[0]) &&
                progress.Details.Contains("remain insufficient"), "Partial review hid its limitation or next step");
            Check(analysis.Diagnosis.Metric("front-response") is { Value: null, Confidence: "LOW" } &&
                originalAnalysis == JsonSerializer.Serialize(analysis) && original == JsonSerializer.Serialize(session),
                "Partial readiness altered measured evidence or the saved goal");
        });
        test("front and drift evidence accumulates across several short corners", () =>
        {
            var session = Continuous(60, goal: new() { FrontEndBite = 1 });
            foreach (var sample in session.Samples)
                sample.SlipAngleDeg = sample.TimeSeconds % 6 < 3 ? 5 : 30;
            var analysis = new DriftDiagnosisEngine().Analyze(session);
            var progress = RecordingEvidenceService.FromAnalysis(session, analysis);
            Check(analysis.Diagnosis.Metric("front-response")?.EvidenceSeconds > 29 && analysis.DriftTimeSeconds > 29,
                "Short sections did not accumulate useful time");
            Check(progress.ReadyToReview && !progress.HasGoalLimitations && progress.NeededEvidence.Count == 0 &&
                progress.Details.Contains("shorter sections"), "Readiness required an uninterrupted ten-second section");
        });
        test("front response does not count straight slow or high-slip driving as normal cornering", () =>
        {
            foreach (var kind in new[] { "slow", "straight", "large-steer", "sliding" })
            {
                var session = Continuous(40, goal: new() { FrontEndBite = 1 });
                foreach (var sample in session.Samples.Where(s => s.TimeSeconds >= 25))
                {
                    sample.SlipAngleDeg = kind == "sliding" ? 9 : 5;
                    sample.SpeedKmh = kind == "slow" ? 29 : 40;
                    sample.SteeringAngleDeg = kind == "straight" ? 11 : kind == "large-steer" ? 121 : 30;
                }
                var progress = new RecordingEvidenceService().Update(session, 40);
                Check(!progress.ReadyToReview && progress.Message.Contains("0.0 / 10 s total"), "Wrong normal-cornering eligibility: " + kind);
            }
        });
        test("partial review requires sixty usable drift seconds rather than wall-clock time", () =>
        {
            var session = Continuous(60, goal: new() { FrontEndBite = 1 });
            var service = new RecordingEvidenceService();
            Check(!service.Update(session, 500).ReadyToReview, "Elapsed time bypassed the useful drift threshold");
            Add(session, 60, 60.04);
            Check(service.Update(session, 501, force: true).Heading == "READY FOR PARTIAL REVIEW", "Enough usable drift did not permit limited review");
            var mostlyIdle = Continuous(120, goal: new() { FrontEndBite = 1 });
            foreach (var sample in mostlyIdle.Samples.Where(s => s.TimeSeconds >= 30)) sample.SpeedKmh = 0;
            Check(!new RecordingEvidenceService().Update(mostlyIdle, 120).ReadyToReview, "Idle time counted toward partial review");
        });
        test("partial review never bypasses poor telemetry or interruption", () =>
        {
            var sparse = Continuous(120, hz: 5, goal: new() { FrontEndBite = 1 });
            Check(!new RecordingEvidenceService().Update(sparse, 120).ReadyToReview, "Low sample rate became ready for partial review");
            var interrupted = Continuous(120, goal: new() { FrontEndBite = 1 });
            interrupted.Context!.Interrupted = true;
            Check(!new RecordingEvidenceService().Update(interrupted, 120).ReadyToReview, "Interrupted run became ready");
            var reset = Continuous(120, goal: new() { FrontEndBite = 1 });
            reset.Samples.Add(new TelemetrySample { TimeSeconds = 0, PacketId = 1, SpeedKmh = 60, SlipAngleDeg = 30 });
            Check(!new RecordingEvidenceService().Update(reset, 121).ReadyToReview, "Reset timeline became ready");
            var changedSetup = Continuous(120, goal: new() { FrontEndBite = 1 });
            changedSetup.Context!.SetupCaptureIssue = "Setup changed";
            Check(!new RecordingEvidenceService().Update(changedSetup, 120).ReadyToReview, "Lost setup evidence became ready");
        });
        test("partial review keeps every missing goal and notifies once before full coverage", () =>
        {
            var session = Continuous(70, goal: new() { FrontEndBite = 1, InitiationSharpness = 1, TransitionSpeed = 1 });
            var limited = new RecordingEvidenceService().Update(session, 70);
            Check(limited.ReadyToReview && limited.Heading == "READY FOR PARTIAL REVIEW" && limited.NeededEvidence.Count == 3,
                "Partial review dropped a missing goal");
            var gate = new RecordingReadyNotification();
            Check(gate.Observe(session.Id, true, limited, true), "Partial review did not notify");
            var full = new RecordingEvidenceProgress { State = "ready", ReadyToReview = true };
            Check(!gate.Observe(session.Id, true, full, true), "Full coverage replayed this run's notification");
            Check(gate.Observe("next", true, full, true), "Next run notification was lost");
        });
        test("ready notification sounds once per recording, never for stale stopped or muted evidence", () =>
        {
            var gate = new RecordingReadyNotification();
            var ready = new RecordingEvidenceProgress { State = "ready", ReadyToReview = true };
            Check(!gate.Observe("one", false, ready, true), "Stopped recording sounded ready");
            Check(!gate.Observe("one", true, ready with { State = "waiting" }, true), "Stale evidence sounded ready");
            Check(gate.Observe("one", true, ready, true), "First readiness did not notify");
            Check(!gate.Observe("one", true, ready, true), "Repeated status replayed sound");
            gate.Observe("one", true, ready with { State = "more-evidence", ReadyToReview = false }, true);
            Check(!gate.Observe("one", true, ready, true), "Readiness fluctuation replayed sound");
            Check(!gate.Observe("two", true, ready, false) && !gate.Observe("two", true, ready, true), "Muted event replayed after enabling sound");
            Check(gate.Observe("three", true, ready, true), "New recording did not reset notification");
        });
        test("live guidance exposes every missing goal without changing readiness thresholds", () =>
        {
            var session = Continuous(30, goal: new() { InitiationSharpness = 1, TransitionSpeed = 1, SelfSteerSpeed = 1 });
            var result = new RecordingEvidenceService().Update(session, 30);
            Check(!result.ReadyToReview && result.Heading == "KEEP COLLECTING" && result.NeededEvidence.Count == 3,
                "Goal checklist lost missing measurements or falsely declared readiness");
            Check(result.NeededEvidence[0] == result.Message && result.NeededEvidence.Any(x => x.Contains("direction changes")), "Goal checklist disagrees with guidance");
        });
        test("live evidence uses saved-analysis drift and phase counts without changing the run", () =>
        {
            var session = IntelligenceChecks.Session();
            var original = JsonSerializer.Serialize(session);
            var expected = new DriftDiagnosisEngine().Analyze(session);
            var progress = new RecordingEvidenceService().Update(session, 100);
            Near(progress.UsableDriftSeconds, expected.DriftTimeSeconds);
            Check(progress.Entries == expected.DriftEntries && progress.Transitions == expected.TransitionCount,
                "Live phase counts differ from saved analysis");
            Check(progress.ReadyToReview && progress.IsProvisional && progress.Details.Contains("not that the tune is better"),
                "Enough evidence claimed a tuning outcome or lost provisional status");
            Check(original == JsonSerializer.Serialize(session), "Live guidance modified the recorded context or samples");
        });
        test("live evidence does not require transitions for neutral or legacy FFB-only goals", () =>
        {
            var neutral = Continuous(25);
            var progress = new RecordingEvidenceService().Update(neutral, 25);
            Check(progress.ReadyToReview && progress.Transitions == 0 && progress.Entries == 0,
                "Neutral evidence unnecessarily required phase events");
            var transition = Continuous(25, goal: new() { TransitionSpeed = 1 });
            progress = new RecordingEvidenceService().Update(transition, 25);
            Check(!progress.ReadyToReview && progress.Message.Contains("direction changes"), "Transition goal ignored missing transitions");
            transition.Context!.Focus = TuningFocus.FfbOnly; transition.Context.Tune!.Focus = TuningFocus.FfbOnly;
            Check(new RecordingEvidenceService().Update(transition, 25).ReadyToReview, "Legacy FFB-only recording required car handling events");
            var initiation = Continuous(25, goal: new() { InitiationSharpness = -1 });
            Check(new RecordingEvidenceService().Update(initiation, 25).Message.Contains("clean entries"), "Initiation goal ignored missing entries");
        });
        test("live evidence excludes invalid, off-track, impact and missing intervals exactly as diagnosis", () =>
        {
            var session = Continuous(40);
            foreach (var sample in session.Samples)
            {
                if (sample.TimeSeconds < 2) sample.PitLimiterOn = true;
                if (sample.TimeSeconds is >= 3 and < 5) sample.WheelsOutsideTrack = 4;
                if (sample.TimeSeconds >= 8) sample.DamageTotal = 1;
            }
            session.Samples[600].InvalidSourceSignals = true;
            session.Samples.RemoveAll(s => s.TimeSeconds is >= 15 and < 20);
            var expected = new DriftDiagnosisEngine().Analyze(session);
            var progress = new RecordingEvidenceService().Update(session, 400);
            Near(progress.UsableDriftSeconds, expected.DriftTimeSeconds);
            Check(progress.UsableDriftSeconds < 31 && progress.UsableDriftSeconds > 20,
                "Wall-clock waiting or excluded driving counted as useful drift");
            var sparse = Continuous(30, hz: 5);
            Check(!new RecordingEvidenceService().Update(sparse, 30).ReadyToReview, "Sparse telemetry was called ready");
        });
        test("live guide throttles refreshes, forces a final update and resets for each recording", () =>
        {
            var service = new RecordingEvidenceService(); var session = Continuous(10);
            var first = service.Update(session, 10);
            Add(session, 10, 25);
            Check(ReferenceEquals(first, service.Update(session, 11)), "Live diagnosis ignored its two-second throttle");
            Check(service.Update(session, 11, force: true).ReadyToReview, "Stopped recording kept outdated evidence");
            var shortRun = Continuous(1); var fresh = service.Update(shortRun, 0);
            Check(!fresh.ReadyToReview && fresh.UsableDriftSeconds < 1, "A new recording retained prior evidence");
            session.Samples.Clear(); Check(!service.Update(session, 0).ReadyToReview, "Cleared run kept ready state");
        });
        test("live guide keeps the initial desired behavior snapshot until the next recording", () =>
        {
            var service = new RecordingEvidenceService(); var session = Continuous(10);
            service.Update(session, 10);
            session.Context!.Tune!.DesiredBehavior.TransitionSpeed = 2;
            Add(session, 10, 25);
            Check(service.Update(session, 25).ReadyToReview, "Mid-run goal edit reinterpreted captured evidence");
            session.Id = Guid.NewGuid().ToString("N");
            Check(!service.Update(session, 25).ReadyToReview, "New run did not capture its new transition goal");
        });
        test("live guide immediately withdraws readiness after interruption or a timeline reset", () =>
        {
            var service = new RecordingEvidenceService(); var session = Continuous(30);
            Check(service.Update(session, 30).ReadyToReview, "Fixture was not ready");
            session.Context!.Interrupted = true;
            var interrupted = service.Update(session, 30.1);
            Check(!interrupted.ReadyToReview && interrupted.State == "interrupted" && interrupted.UsableDriftSeconds > 20,
                "Interrupted run kept readiness or discarded available evidence");
            var restarted = Continuous(30);
            restarted.Samples.Add(new TelemetrySample { TimeSeconds = 0, PacketId = 1, SpeedKmh = 60, SlipAngleDeg = 30 });
            Check(new RecordingEvidenceService().Update(restarted, 31).State == "interrupted", "Restarted timeline was treated as comparable evidence");
        });
        test("live angle guidance requires complete attempts rather than brief peaks or missing recovery data", () =>
        {
            var angle = Continuous(60, goal: new() { SustainedAngle = SustainedAnglePreference.ExtremeAngle });
            foreach (var s in angle.Samples)
            {
                s.SlipAngleDeg = s.TimeSeconds % 10 is >= 2 and < 6 ? 75 : 30;
                s.LongitudinalVelocityMs = 10;
            }
            var ready = new RecordingEvidenceService().Update(angle, 60);
            Check(ready.ReadyToReview && ready.CompletedAngleAttempts == 6, "Complete angle evidence was not recognized");
            foreach (var s in angle.Samples) s.LongitudinalVelocityMs = null;
            var missing = new RecordingEvidenceService().Update(angle, 60);
            Check(!missing.ReadyToReview && missing.CompletedAngleAttempts == 0 && missing.Message.Contains("complete holds"),
                "Missing recovery evidence was called ready before the partial-review threshold");
            Add(angle, 60, 61);
            missing = new RecordingEvidenceService().Update(angle, 61);
            Check(missing.ReadyToReview && missing.HasGoalLimitations && missing.CompletedAngleAttempts == 0 && missing.Message.Contains("complete holds"),
                "Partial review invented completed attempts or hid missing angle evidence");
        });
        test("long-recording live guidance has a bounded workload and preserves raw samples", () =>
        {
            var longRun = Continuous(1800); var service = new RecordingEvidenceService();
            var clock = Stopwatch.StartNew(); var first = service.Update(longRun, 1800); clock.Stop();
            Console.WriteLine($"Live evidence benchmark: {longRun.Samples.Count:N0} samples in {clock.Elapsed.TotalMilliseconds:0} ms.");
            Check(first.ReadyToReview && first.UsableDriftSeconds > 1799, "Long clean run lost useful evidence");
            Add(longRun, 1800, 1800.1);
            Check(ReferenceEquals(first, service.Update(longRun, 1802)), "Long run ignored its five-second refresh interval");
            longRun.Samples.AddRange(Enumerable.Repeat(new TelemetrySample(), RecordingEvidenceService.MaximumLiveSamples + 1 - longRun.Samples.Count));
            var limited = service.Update(longRun, 1803, force: true);
            Check(!limited.ReadyToReview && limited.State == "review-on-save" && longRun.Samples.Count == RecordingEvidenceService.MaximumLiveSamples + 1,
                "Live workload limit discarded data or claimed current evidence readiness");
        });
    }

    private static TelemetrySession Continuous(double seconds, int hz = 50, CarBehaviorTarget? goal = null)
    {
        var driver = Guid.NewGuid().ToString("N");
        var session = new TelemetrySession { Context = new RunContext { DriverId = driver, DriverName = "Evidence fixture", TrackId = "fixture", Conditions = "dry",
            Tune = new TuneVersion { DriverId = driver, ContextKey = "fixture", DesiredBehavior = goal ?? new() } } };
        Add(session, 0, seconds, hz); return session;
    }

    private static void Add(TelemetrySession session, double from, double to, int hz = 50)
    {
        for (var index = (int)Math.Round(from * hz); index < (int)Math.Round(to * hz); index++)
            session.Samples.Add(new TelemetrySample { PacketId = index + 1, TimeSeconds = (double)index / hz,
                SpeedKmh = 60, SlipAngleDeg = 30, SteeringAngleDeg = -60, YawRateDegPerSec = 20,
                Throttle = .75, Clutch = 1, Gear = 3, HasExtendedSignals = true, LongitudinalVelocityMs = 14,
                FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, FinalFfb = .4 });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .000001, $"Expected {expected}, found {actual}");
}
