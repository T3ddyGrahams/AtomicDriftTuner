using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using AtomicDriftTuner;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckRecordingRecovery(string output)
    {
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("Recorder recovery: " + message); checks++; }
        static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        var root = Path.Combine(output, "recording-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = new TuneInput();
        input.Car.SourceFolderName = "isolated_recording_recovery_car";
        var track = "recovery_fixture_track";
        var car = input.Car.SourceFolderName;
        using var hub = new TelemetryHubService();
        using var map = MemoryMappedFile.CreateNew(null, 4096);
        // No named game map, background polling, wheelbase writes or user-history writes.
        Set(Get(hub, "_reader")!, "_physicsMap", map);
        var window = new TelemetryWindow(input, hub, () => new() { CarModel = car, Track = track });
        var store = (TelemetrySessionStore)Get(window, "_sessionStore")!;
        Set(store, "<RootDirectory>k__BackingField", Path.Combine(root, "sessions"));
        var history = new RunHistoryStore(Path.Combine(root, "history"));
        Set(window, "_history", history);
        var calibration = (CalibrationStore)Get(window, "_calibrationStore")!;
        Set(calibration, "_directory", root);
        Set(calibration, "_path", Path.Combine(root, "calibrations.json"));
        Set(calibration, "_backupPath", Path.Combine(root, "calibrations.backup.json"));
        var driver = history.GetOrCreateDriver("Recording recovery driver");
        var plan = new RecordingPlan(driver.Id, driver.Name, "", "", "", "dry, same section", TuningFocus.Both, false);
        var tick = typeof(TelemetryWindow).GetMethod("Timer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Tick() => tick.Invoke(window, [null, EventArgs.Empty]);
        TelemetrySession Session() => (TelemetrySession)Get(window, "_session")!;
        CompanionCommand Command(string action)
        {
            var state = window.GetCompanionState(true);
            return new() { Action = action, WindowId = state.WindowId, SessionId = state.SessionId, ControlVersion = state.ControlVersion };
        }
        void Execute(string action) { var response = window.ExecuteCompanionCommand(Command(action), true); Check(response.Ok, action + " failed: " + response.Message); }
        void Publish(int packet, double time)
        {
            Set(hub, "_latest", new TelemetrySample { PacketId = packet, TimeSeconds = time, HasExtendedSignals = true,
                SpeedKmh = 55, SlipAngleDeg = 25, Gear = 3, FinalFfb = 1, Throttle = .7 });
            Set(hub, "_updatedUtc", DateTimeOffset.UtcNow);
        }
        void Freeze() { Set(hub, "_updatedUtc", DateTimeOffset.UtcNow.AddSeconds(-1)); Tick(); }
        void ExpireRecovery() { Set(window, "_telemetryUnavailableSince", -5.0); Tick(); }
        try
        {
            window.UseRecordingPlan(plan);
            Publish(10, 300);
            Execute("start");
            for (var i = 0; i < 850; i++) { Publish(10 + i, 300 + i * .02); Tick(); }
            var first = Session();
            Execute("stop");
            Check(!first.Context!.Interrupted, "normal connected stop was marked interrupted");
            Execute("save");
            var savedBaseline = store.ListRecent(input).Single();
            var firstAnalysis = savedBaseline.Analysis;
            Check(!firstAnalysis.CalibrationSuggestion.IsNeutral, "fixture did not produce a real telemetry recommendation");
            var engine = new CalibrationEngine();
            var key = engine.BuildKey(input);
            calibration.Upsert(engine.ApplyTelemetrySuggestion(input, calibration.Get(key), firstAnalysis.CalibrationSuggestion));
            Set(window, "_suggestionApplied", true);
            plan = plan with { BaselineId = first.Id, Recommendation = "Test the recommended AC gain reduction" };
            window.UseRecordingPlan(plan);

            // An idle pause must not turn off the refresh loop between the two runs.
            Freeze();
            Check(((DispatcherTimer)Get(window, "_timer")!).IsEnabled, "idle pause stopped recorder refresh");
            Publish(2000, 600); Tick();
            Check(((Button)window.FindName("RecordButton")).IsEnabled, "desktop Record did not recover between runs");
            Execute("start");
            var second = Session();
            Check(second.Id != first.Id && second.Context!.RecommendationSessionId == first.Id, "comparison run lost its baseline or reused the old session");
            Check(first.Context!.Tune!.Settings.Any(kv => second.Context!.Tune!.Settings.TryGetValue(kv.Key, out var value) && value != kv.Value), "second run did not capture the applied recommendation");
            Check(Get(window, "_telemetryUnavailableSince") is null && !(bool)Get(window, "_suggestionApplied")!, "second run retained previous capture state");
            Tick(); Publish(2001, 600.02); Tick();
            var stopBeforeGap = Command("stop");
            Freeze();
            var waiting = window.GetCompanionState(true);
            Check(waiting.CanStop && !waiting.CanStart && !waiting.CanSave, "brief frozen packet ended the recording or allowed replacement");
            Check(!hub.GetSnapshot().Connected && waiting.Message.Contains("Waiting for fresh"), "waiting state advertised frozen telemetry as live");
            Check(second.Samples.Count == 2, "frozen frame was duplicated or existing samples lost");
            Freeze();
            Check(second.Samples.Count == 2, "repeated polling added frozen data");
            Publish(2010, 602.02); Tick();
            Check(Session().Id == second.Id && window.GetCompanionState(true).CanStop, "recovery replaced or stopped the comparison run");
            Check(second.Samples.Count == 3 && Math.Abs(second.Samples[^1].TimeSeconds - 2.02) < .0001, "recovery filled or compressed the missing interval");
            Publish(2011, 602.04); Tick();
            Execute("stop");
            var recoveredAnalysis = (TelemetryAnalysis)Get(window, "_analysis")!;
            Check(!second.Context!.Interrupted && recoveredAnalysis.Diagnosis.Discontinuities == 1 && recoveredAnalysis.DurationSeconds < .1, "analysis attributed the missing time as driving");
            Check(!window.ExecuteCompanionCommand(stopBeforeGap, true).Ok, "old stop command remained valid after the run ended");
            Execute("save");
            Check(store.TryLoad(savedBaseline.JsonPath)!.Session.Id == first.Id && first.Samples.Count == 850, "second recording changed the saved baseline");

            // Stopping during recovery preserves a partial, ineligible run.
            Publish(3000, 700); Execute("start"); Tick(); Freeze(); Execute("stop");
            Check(Session().Context!.Interrupted && !(bool)typeof(TelemetryWindow).GetMethod("CanApplyCurrentSuggestion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!, "manual stop during a gap allowed calibration apply");
            Check(window.GetCompanionState(true).Message.Contains("telemetry was unavailable"), "companion lost the manual interruption reason");
            Execute("save");

            // A previous wait timer must not instantly terminate another recording.
            Set(window, "_telemetryUnavailableSince", -100.0);
            Publish(4000, 800); Execute("start"); Tick(); Freeze();
            Check(window.GetCompanionState(true).CanStop, "new run inherited an expired recovery timer");
            ExpireRecovery();
            var interrupted = Session();
            Check(interrupted.Context!.Interrupted && !window.GetCompanionState(true).CanStop && window.GetCompanionState(true).CanSave, "long outage did not preserve a savable partial run");
            Check(interrupted.StopReason.Contains("5 seconds") && window.GetCompanionState(true).Message.Contains("5 seconds"), "actual timeout reason missing on desktop/companion");
            Execute("save");
            Check(store.ListRecent(input).Single(r => r.Session.Id == interrupted.Id).Session.StopReason == interrupted.StopReason, "saved history lost the stop reason");

            // Real session boundaries still stop capture immediately.
            Publish(5000, 900); Execute("start"); Tick();
            track = "different_track"; Set(window, "_lastIdentityCheck", -2.0); Tick();
            Check(Session().Context!.Interrupted && Session().StopReason.Contains("car or track changed") && Session().Samples.Count == 1, "track change was merged into the original run");
            Execute("save"); track = "recovery_fixture_track";
            Publish(6000, 1000); Execute("start"); Tick();
            car = "different_car"; Set(window, "_lastIdentityCheck", -2.0); Tick();
            Check(Session().Context!.Interrupted && Session().Samples.Count == 1, "car change was merged into the original run");
            Execute("save"); car = input.Car.SourceFolderName;
            Publish(7000, 1100); Execute("start"); Tick(); Freeze(); Publish(1, 1102); Tick();
            Check(Session().Context!.Interrupted && Session().StopReason.Contains("physics stream restarted") && Session().Samples.Count == 1, "recovery accepted packets from a restarted game");
            Execute("save");

            Publish(7500, 1150); Execute("start"); Tick(); Freeze();
            Set(window, "_telemetryUnavailableSince", -5.0); Publish(7501, 1156); Tick();
            Check(Session().Context!.Interrupted && Session().Samples.Count == 1, "late fresh frame bypassed the recovery deadline");
            Execute("save");
            Publish(7600, 1160); Execute("start"); Tick(); Publish(7601, 1159); Tick();
            Check(Session().Context!.Interrupted && Session().Samples.Count == 1, "source timestamp rollback was joined to the original run");
            Execute("save");

            Publish(8000, 1200); Execute("start"); Freeze(); ExpireRecovery();
            Check(Session().Samples.Count == 0 && !window.GetCompanionState(true).CanStop && window.GetCompanionState(true).Message.Contains("No frames"), "zero-frame timeout hid the cause or offered a false save");
        }
        finally { window.Close(); }
        Progress($"PASS {checks} recording recovery assertions; actual start/save/recommendation/second-run flow, fresh-frame gaps, timeout, context boundaries and persisted reasons.");
    }
}
