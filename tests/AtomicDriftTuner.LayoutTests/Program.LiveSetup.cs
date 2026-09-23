using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AtomicDriftTuner;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static partial class Program
{
    private static void CheckLiveSetup(string output)
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Live setup: " + why); checks++; }
        static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
        static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
        var directory = Path.Combine(output, "live-setup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var input = new TuneInput();
        input.Car.Id = input.Car.SourceFolderName = "isolated_live_setup_fixture";
        var car = input.Car.SourceFolderName;
        var track = "live_setup_fixture_track";
        using var hub = new TelemetryHubService();
        using var physics = MemoryMappedFile.CreateNew(null, 4096);
        Set(Get(hub, "_reader")!, "_physicsMap", physics);
        // Only an anonymous physics map is used. No real AC connection, timer polling or hardware writes.
        var window = new TelemetryWindow(input, hub, () => new() { CarModel = car, Track = track });
        var history = new RunHistoryStore(Path.Combine(directory, "history")); Set(window, "_history", history);
        var sessions = (TelemetrySessionStore)Get(window, "_sessionStore")!;
        Set(sessions, "<RootDirectory>k__BackingField", Path.Combine(directory, "sessions"));
        var calibration = Get(window, "_calibrationStore")!;
        Set(calibration, "_directory", directory); Set(calibration, "_path", Path.Combine(directory, "calibrations.json"));
        Set(calibration, "_backupPath", Path.Combine(directory, "calibrations.backup.json"));
        var driver = history.GetOrCreateDriver("Live setup fixture");
        var manualPath = Path.Combine(directory, "manual-fallback.ini");
        File.WriteAllText(manualPath, "[CAR]\nMODEL=isolated_live_setup_fixture\n[CAMBER_LF]\nVALUE=-40\n");
        var manualBytes = File.ReadAllBytes(manualPath);
        var plan = new RecordingPlan(driver.Id, driver.Name, "", "", manualPath, "dry, same section", TuningFocus.CarSetupOnly, false);
        var automatic = (CheckBox)window.FindName("UseAutomaticSetupCheck");
        var confirmation = (CheckBox)window.FindName("TuneInUseCheck");
        long sequence = 0;
        TelemetrySession Session() => (TelemetrySession)Get(window, "_session")!;
        CapturedCarSetup? Captured() => (CapturedCarSetup?)Get(window, "_liveSetup");
        void FreshHub(int packet = 10, double time = 100)
        {
            Set(hub, "_latest", new TelemetrySample { PacketId = packet, TimeSeconds = time, HasExtendedSignals = true,
                SpeedKmh = 60, SlipAngleDeg = 30, LongitudinalVelocityMs = 14, Gear = 3, Throttle = .7, Clutch = 1,
                FrontWheelSlipAvg = 1, RearWheelSlipAvg = 3, FinalFfb = .4 });
            Set(hub, "_updatedUtc", DateTimeOffset.UtcNow);
        }
        SetupCaptureOffer Offer() { FreshHub(); return (SetupCaptureOffer)Call(window, "OfferSetupCapture")!; }
        LiveSetupCaptureRequest Request(SetupCaptureOffer? offer = null, int camber = -30)
        {
            offer ??= Offer(); sequence++;
            return new() { Available = true, ProtocolVersion = 1, Source = "csp-current-setup", Nonce = offer.Nonce, WindowId = offer.WindowId,
                CarId = input.Car.SourceFolderName!, TrackId = track, TrackLayout = "layout-a", SessionIndex = 0, SessionType = 1,
                SessionGeneration = 1, SetupRevision = sequence, CaptureSequence = sequence, Frame = sequence * 100, SimTimeMs = sequence * 2000,
                SetupIni = $"[CAR]\nMODEL={input.Car.SourceFolderName}\n[CAMBER_LF]\nVALUE={camber}\n[PRESSURE_LF]\nVALUE=25\n" };
        }
        RemoteActionResponse Receive(LiveSetupCaptureRequest request) { FreshHub(); return (RemoteActionResponse)Call(window, "ReceiveSetupCapture", request)!; }
        void Capture(int camber = -30) => Check(Receive(Request(camber: camber)).Ok && Captured() is not null, "valid fixture capture rejected");
        void Start()
        {
            FreshHub(); confirmation.IsChecked = true;
            Call(window, "StartRecording", false);
            ((DispatcherTimer)Get(window, "_timer")!).Stop();
        }
        void AddFrames(int firstPacket, double start)
        {
            for (var i = 0; i < 100; i++)
            {
                FreshHub(firstPacket + i, start + i * .02);
                Call(hub, "PublishSampleLocked", Get(hub, "_latest"));
                Call(window, "ProcessTelemetrySnapshot", hub.GetSnapshot(), i * .02);
            }
        }
        void StopAndSave()
        {
            FreshHub(); Call(window, "FinalizeRecording", false, "Fixture stopped.");
            Call(window, "SaveAnalyzedSession");
            Check((bool)Get(window, "_sessionSaved")!, "fixture recording was not saved to isolated storage");
        }
        try
        {
            window.UseRecordingPlan(plan, force: true);
            automatic.IsChecked = true; Call(window, "AutomaticSetup_Click", window, new RoutedEventArgs());
            var offer = Offer();
            Check(offer.Nonce.Length == 64 && Offer().Nonce == offer.Nonce, "status polling rotated the outstanding capture nonce");
            var request = Request(offer);
            confirmation.IsChecked = true;
            Check(Receive(request).Ok && Captured()?.Values["ACSetup.CAMBER_LF"] == -30 && confirmation.IsChecked == false,
                "first capture did not replace confirmation with current numeric evidence");
            var initial = Captured();
            Check(!Receive(request).Ok && ReferenceEquals(initial, Captured()), "a replayed response changed accepted evidence");
            confirmation.IsChecked = true;
            Capture();
            Check(confirmation.IsChecked == true && Captured()!.Sha256 == initial!.Sha256, "identical periodic capture reset confirmation");
            Capture(-29);
            Check(confirmation.IsChecked == false && Captured()!.Sha256 != initial!.Sha256, "changed setup kept old confirmation");

            request = Request(); request.SetupIni = "[]\nVALUE=2\n" + request.SetupIni;
            Check(Receive(request).Ok && Captured()?.UnassignedValue == 2 &&
                ((TextBlock)window.FindName("SetupSnapshotText")).Text.Contains("unidentified"), "unnamed capture rejected or limitation hidden");
            confirmation.IsChecked = true;
            request = Request(); request.SetupIni = "VALUE=3\n" + request.SetupIni;
            Check(Receive(request).Ok && Captured()?.UnassignedValue == 3 && confirmation.IsChecked == false,
                "unnamed value change retained confirmation");

            offer = Offer(); request = Request(offer); request.WindowId = Guid.NewGuid().ToString("N");
            Check(!Receive(request).Ok && Offer().Nonce == offer.Nonce, "another window used or consumed this recorder's challenge");
            request = Request(offer); Set(window, "_setupChallengeUtc", DateTime.UtcNow.AddSeconds(-6));
            Check(!Receive(request).Ok, "expired setup response accepted");
            Capture(); confirmation.IsChecked = true;
            car = "different_live_car";
            Check(Receive(Request()).Ok && Captured() is null && confirmation.IsChecked == false, "identity mismatch retained a confirmed setup");
            car = input.Car.SourceFolderName;
            Capture(); request = Request(); request.Available = false;
            Check(Receive(request).Ok && Captured() is null, "unsupported/unavailable capture reused stale setup evidence");
            Capture(); request = Request(); request.SetupIni = "[CAMBER_LF]\nVALUE=invalid\n";
            Check(Receive(request).Ok && Captured() is null, "malformed capture reused prior numeric evidence");

            automatic.IsChecked = false; Call(window, "AutomaticSetup_Click", window, new RoutedEventArgs());
            Check((string?)Get(window, "_setupSnapshotPath") == manualPath && File.ReadAllBytes(manualPath).SequenceEqual(manualBytes),
                "automatic fallback erased or modified the manual attachment");
            Check(Call(window, "OfferSetupCapture") is null, "manual mode offered an automatic setup challenge");
            Check(((TextBlock)window.FindName("SetupSnapshotText")).Text.Contains("manual-fallback.ini"), "manual fallback hid the retained attachment");
            automatic.IsChecked = true; Call(window, "AutomaticSetup_Click", window, new RoutedEventArgs());
            Capture(); Start(); AddFrames(100, 200);
            var first = Session(); var recorded = first.Context!.Tune!;
            var tunePath = Path.Combine(history.RootDirectory, "tunes", recorded.Id + ".json");
            var savedTuneBytes = File.ReadAllBytes(tunePath);
            Check(first.Context.TuneConfirmedInUse && recorded.SetupSource == "csp-current-setup" && recorded.Settings["ACSetup.CAMBER_LF"] == -30 &&
                recorded.SetupFileName == "Current CSP setup" && recorded.SetupCapturedUtc is not null && recorded.SetupTrackLayout == "layout-a",
                "real recording start failed to snapshot current CSP values/provenance");
            Check(first.Samples.Count == 100 && recorded.Settings["ACSetup.CAMBER_LF"] != -40, "automatic capture silently used the manual fallback file");
            var frozenTune = JsonSerializer.Serialize(recorded);
            Capture(-28);
            Check((bool)Get(window, "_recording")! && first.Samples.Count == 100 && !first.Context.TuneConfirmedInUse && first.Context.SetupCaptureIssue.Length > 0,
                "mid-run setup change stopped/discarded capture or kept setup attribution");
            Check(JsonSerializer.Serialize(recorded) == frozenTune && File.ReadAllBytes(tunePath).SequenceEqual(savedTuneBytes),
                "mid-run setup refresh overwrote immutable start evidence or tune history");
            Check(((RecordingEvidenceProgress)Call(window, "CurrentEvidence")!).State == "setup-unverified", "live guide hid setup attribution loss");
            StopAndSave();
            var savedFirst = sessions.ListRecent(input).Single(s => s.Session.Id == first.Id);
            Check(savedFirst.Session.Context!.SetupCaptureIssue.Length > 0 && !savedFirst.Session.Context.TuneConfirmedInUse &&
                savedFirst.Session.Context.Tune!.Settings["ACSetup.CAMBER_LF"] == -30, "saved run lost the limitation or original setup values");

            Capture(-28); Start(); AddFrames(500, 300);
            var second = Session();
            Check(second.Id != first.Id && second.Context!.SetupCaptureIssue == "" && second.Context.TuneConfirmedInUse &&
                second.Context.Tune!.Settings["ACSetup.CAMBER_LF"] == -28, "new recording inherited the previous run's invalid setup state");
            Set(window, "_liveSetup", Captured()! with { ReceivedUtc = DateTime.UtcNow.AddSeconds(-6) });
            FreshHub(); Call(window, "RefreshSetupCapture");
            Check(Captured() is null && (bool)Get(window, "_recording")! && second.Samples.Count == 100 &&
                second.Context!.SetupCaptureIssue.Length > 0 && !second.Context.TuneConfirmedInUse, "expired setup monitoring lost samples or stayed attributable");
            StopAndSave();

            // A completed worker from the preceding run must not turn a new empty run green.
            var oldEvidence = new TaskCompletionSource<RecordingEvidenceProgress>();
            Set(window, "_evidenceTask", oldEvidence.Task); Set(window, "_evidenceSessionId", second.Id);
            Capture(-28); Start();
            oldEvidence.SetResult(new() { State = "ready", ReadyToReview = true, UsableDriftSeconds = 99, Message = "OLD RUN" });
            Set(window, "_nextEvidenceSeconds", 100d);
            Call(window, "UpdateLiveEvidence", 0d);
            var current = (RecordingEvidenceProgress)Call(window, "CurrentEvidence")!;
            Check(!current.ReadyToReview && current.UsableDriftSeconds == 0 && !current.Message.Contains("OLD RUN"), "late evidence worker leaked across recording IDs");
            Check(File.ReadAllBytes(tunePath).SequenceEqual(savedTuneBytes) && File.ReadAllBytes(manualPath).SequenceEqual(manualBytes),
                "later captures changed earlier history or the manual source setup");
        }
        finally
        {
            ((DispatcherTimer)Get(window, "_timer")!).Stop();
            Set(window, "_recording", false); window.Close();
        }
        Progress($"PASS {checks} live setup assertions; challenge freshness, isolated snapshots, confirmation, fallback, mid-run attribution and evidence generations.");
    }
}
