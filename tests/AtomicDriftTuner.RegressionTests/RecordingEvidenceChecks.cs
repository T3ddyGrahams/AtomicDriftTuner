using System.Diagnostics;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class RecordingEvidenceChecks
{
    internal static void Run(Action<string, Action> test)
    {
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
                "Missing recovery evidence was called ready");
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
