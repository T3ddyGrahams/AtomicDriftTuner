using System.IO;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PitHouseChecks
{
    private sealed class Fake : IMozaMotorApi
    {
        public string Device = "MOZA fixture base";
        public Dictionary<string, int> Values = PitHouseCatalog.Settings.ToDictionary(s => s.Key, s => s.Min);
        public List<string> Writes = [];
        public Action<string, int>? OnWrite;
        public string? FailRead;
        public bool IgnoreWrites;
        public string DeviceName() => Device;
        public int Read(string key) => key == FailRead ? throw new IOException("Unreadable fixture control") : Values[key];
        public void Write(string key, int value) { Writes.Add(key); OnWrite?.Invoke(key, value); if (!IgnoreWrites) Values[key] = value; }
        public void Dispose() { }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Refuses(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } catch (InvalidDataException) { return; } throw new Exception("Expected refusal"); }
    private static PitHousePlan Plan(PitHouseReading read) => PitHouseService.Plan(read, new[] { KeyValuePair.Create("FfbStrength", 20), KeyValuePair.Create("PeakTorque", 60) });
    public static void Run(Action<string, Action> test, string root)
    {
        PitHouseService Service(Fake fake, string name, Action? guard = null) => new(() => fake, Path.Combine(root, name), guard ?? (() => { }));
        test("provider preferences preserve legacy defaults and survive switching", () =>
        {
            var dir = Path.Combine(root, "providers"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "preferences.json"), "{\"DriverName\":\"Existing driver\",\"SimHub\":\"Yes\",\"Azom\":\"Yes\",\"WantLiveConnection\":true}");
            var store = new GuidedWorkflowStore(dir); var p = store.Preferences();
            Check(p.FfbProvider == FfbProvider.SimHubAzom && p.WantLiveConnection, "Legacy AZOM choice lost");
            p.FfbProvider = FfbProvider.MozaPitHouse; p.MozaSdkFolder = @"C:\SDK test\x64"; store.SavePreferences(p);
            p = store.Preferences(); Check(p.FfbProvider == FfbProvider.MozaPitHouse && p.SimHub == "Yes" && p.MozaSdkFolder.EndsWith("x64"), "Provider/path/old answers lost");
            p.FfbProvider = FfbProvider.Manual; store.SavePreferences(p);
            Check(store.Preferences().FfbProvider == FfbProvider.Manual, "Manual not persisted");
            p.FfbProvider = (FfbProvider)99; Refuses(() => store.SavePreferences(p));
        });
        test("provider instructions use the selected software even with stale AZOM status", () =>
        {
            foreach (var detail in new[] { false, true })
            {
                var p = new GuidedPreferences { FfbProvider = FfbProvider.MozaPitHouse, ShowDetailedHelp = detail, WantLiveConnection = true, SimHub = "Yes", Azom = "Yes" };
                var text = GuidedWorkflowEngine.Instructions(p, new(true, true, true, true, true, true));
                Check(text.Contains("Pit House") && !text.Contains("AZOM readback is"), "Wrong provider guidance");
                p.FfbProvider = FfbProvider.Manual;
                Check(GuidedWorkflowEngine.Instructions(p, new(true, true, true, true, true, true)).Contains("manual", StringComparison.OrdinalIgnoreCase), "Manual selected live AZOM");
                p.Focus = TuningFocus.CarSetupOnly;
                Check(GuidedWorkflowEngine.Instructions(p, null).Contains("not needed"), "Car only required connection");
            }
        });
        test("provider change requires a new guided baseline and preserves old history", () =>
        {
            var p = new GuidedPreferences { Completed = true, FocusChoiceConfirmed = true, FfbProvider = FfbProvider.MozaPitHouse };
            var j = new GuidedJourney { BaselineId = "old", Reviewed = true, CarConfirmed = true };
            Check(GuidedWorkflowEngine.Next(p, j, "goal").Stage == GuidedStage.GoalsChanged && j.BaselineId == "old", "Provider change reused baseline or erased it");
            Refuses(() => FfbProviderOptions.Require(FfbProvider.SimHubAzom, p.FfbProvider));
            Refuses(() => FfbProviderOptions.Require(FfbProvider.MozaPitHouse, FfbProvider.Manual));
        });
        test("before after comparisons reject different FFB providers but retain metrics", () =>
        {
            var first = IntelligenceChecks.Session(); var second = RunHistoryStore.Clone(first);
            second.Id = Guid.NewGuid().ToString("N"); second.StartedUtc = first.StartedUtc.AddMinutes(5);
            second.Context!.Tune!.FfbProvider = FfbProvider.MozaPitHouse;
            var before = new SavedTelemetrySession { Session = first, Analysis = new TelemetryAnalyzer().Analyze(first) };
            var after = new SavedTelemetrySession { Session = second, Analysis = new TelemetryAnalyzer().Analyze(second) };
            var comparison = new RunComparisonEngine().Compare(before, after);
            Check(!comparison.Comparable && comparison.Limitations.Any(x => x.Contains("Wheelbase software")) && comparison.Metrics.Count > 0, "Different providers produced improvement verdict or lost evidence");
            first.Context!.Focus = first.Context.Tune!.Focus = second.Context.Focus = second.Context.Tune.Focus = TuningFocus.CarSetupOnly;
            Check(!new RunComparisonEngine().Compare(before, after).Limitations.Any(x => x.Contains("Wheelbase software")), "Car-only comparisons depend on optional FFB selection");
        });
        test("Pit House translation distinguishes inertia controls and never clamps targets", () =>
        {
            var a = new AzomSettings(); a.WheelbaseEffects.NaturalInertia = 170; a.Protection.SteeringWheelInertia = 1450;
            Check(PitHouseCatalog.Find("NaturalInertia").Target(a) == 170 && PitHouseCatalog.Find("NaturalInertiaRatio").Target(a) == 1450, "Inertia meanings swapped");
            var f = new Fake(); var s = Service(f, "moza-ranges"); var read = s.ReadAsync().GetAwaiter().GetResult();
            Refuses(() => PitHouseService.Plan(read, new[] { KeyValuePair.Create("LimitWheelSpeed", 150) }));
            Refuses(() => PitHouseService.Plan(read, new[] { KeyValuePair.Create("FfbReverse", 1) }));
            Refuses(() => PitHouseService.Plan(read, new[] { KeyValuePair.Create("RoadSensitivity", 3) }));
            Check(f.Writes.Count == 0, "Planning wrote to hardware");
        });
        test("Pit House missing or out-of-range readback disables that control", () =>
        {
            var f = new Fake { FailRead = "FfbStrength" }; f.Values["PeakTorque"] = 20;
            var read = Service(f, "moza-unreadable").ReadAsync().GetAwaiter().GetResult();
            Check(!read.Values.ContainsKey("FfbStrength") && !read.Values.ContainsKey("PeakTorque") && read.Errors.Count == 2, "Bad reads became valid values");
            Refuses(() => Plan(read)); Check(f.Writes.Count == 0, "Read touched settings");
        });
        test("Pit House saves original selected values before writing and verifies restore", () =>
        {
            var f = new Fake(); var path = Path.Combine(root, "moza-apply"); var s = Service(f, "moza-apply");
            f.OnWrite = (_, _) => Check(Directory.GetFiles(path, "*.json").Length > 0, "Write occurred before backup");
            var read = s.ReadAsync().GetAwaiter().GetResult();
            var result = s.ApplyAsync(Plan(read)).GetAwaiter().GetResult();
            Check(result.Verified && f.Values["FfbStrength"] == 20 && f.Values["PeakTorque"] == 60, "Apply failed");
            Check(f.Writes.SequenceEqual(new[] { "FfbStrength", "PeakTorque" }), "Unselected setting written");
            var backup = PitHouseService.LoadBackup(result.BackupPath);
            Check(backup.Changes[0].Before == 0 && backup.Changes[1].Before == 50, "Backup lost original values");
            read = s.ReadAsync().GetAwaiter().GetResult();
            var restore = PitHouseService.Plan(read, PitHouseService.RestoreTargets(read, backup));
            Check(s.ApplyAsync(restore).GetAwaiter().GetResult().Verified && f.Values["FfbStrength"] == 0 && f.Values["PeakTorque"] == 50, "Restore failed");
        });
        test("Pit House stale source expiry and mismatched device block all writes", () =>
        {
            var f = new Fake(); var s = Service(f, "moza-stale"); var read = s.ReadAsync().GetAwaiter().GetResult(); var p = Plan(read);
            f.Values["FfbStrength"] = 1; Refuses(() => s.ApplyAsync(p).GetAwaiter().GetResult()); f.Values["FfbStrength"] = 0;
            Refuses(() => s.ApplyAsync(p with { CapturedUtc = DateTimeOffset.UtcNow.AddMinutes(-3) }).GetAwaiter().GetResult());
            f.Device = "different"; Refuses(() => s.ApplyAsync(p).GetAwaiter().GetResult());
            Check(f.Writes.Count == 0 && !Directory.Exists(Path.Combine(root, "moza-stale")), "Rejected plan wrote/created backup");
        });
        test("Pit House failed readback stops the batch and preserves recovery evidence", () =>
        {
            var f = new Fake { IgnoreWrites = true }; var s = Service(f, "moza-failed-readback");
            var result = s.ApplyAsync(Plan(s.ReadAsync().GetAwaiter().GetResult())).GetAwaiter().GetResult();
            Check(!result.Verified && f.Writes.Count == 1 && File.Exists(result.BackupPath) && result.Message.Contains("may have changed"), "Failure was reported as success or retried");
        });
        test("Pit House setter failure and provider change stop remaining writes", () =>
        {
            foreach (bool providerChange in new[] { false, true })
            {
                bool allowed = true; var f = new Fake(); var s = Service(f, "moza-partial-" + providerChange, () => { if (!allowed) throw new InvalidOperationException("Provider changed"); });
                f.OnWrite = (_, _) => { if (providerChange) allowed = false; else throw new IOException("SDK failure after handoff"); };
                var result = s.ApplyAsync(Plan(s.ReadAsync().GetAwaiter().GetResult())).GetAwaiter().GetResult();
                Check(!result.Verified && f.Writes.Count == 1 && File.Exists(result.BackupPath), "Partial batch was retried or backup missing");
            }
        });
        test("Pit House backup failure prevents any write", () =>
        {
            File.WriteAllText(Path.Combine(root, "moza-blocked-backup"), "not a directory");
            var f = new Fake(); var s = Service(f, "moza-blocked-backup");
            try { s.ApplyAsync(Plan(s.ReadAsync().GetAwaiter().GetResult())).GetAwaiter().GetResult(); throw new Exception("Expected I/O error"); } catch (IOException) { }
            Check(f.Writes.Count == 0, "Missing backup allowed a write");
        });
        test("Pit House run snapshots preserve provider and omit unsupported generated controls", () =>
        {
            var history = new RunHistoryStore(Path.Combine(root, "moza-history"));
            var input = new TuneInput(); var driver = history.GetOrCreateDriver("Provider fixture");
            var version = history.CaptureTune(input, driver, "Pit House", new(), null, ffbProvider: FfbProvider.MozaPitHouse);
            Check(version.FfbProvider == FfbProvider.MozaPitHouse && version.Settings.Keys.Any(x => x.StartsWith("Generated.PitHouse.")) &&
                !version.Settings.Keys.Any(x => x.StartsWith("Generated.AZOM.")) && version.Source.Contains("live hardware values are not read"), "Snapshot claimed unsupported/live values");
            Check(history.ListTunes(input, driver.Id).Single().FfbProvider == FfbProvider.MozaPitHouse, "Provider was lost on disk");
        });
        test("Pit House provider refusal occurs before opening SDK or saving a backup", () =>
        {
            bool opened = false;
            var service = new PitHouseService(() => { opened = true; return new Fake(); }, Path.Combine(root, "moza-refused"), () => throw new InvalidOperationException("Manual selected"));
            Refuses(() => service.ReadAsync().GetAwaiter().GetResult());
            Check(!opened && !Directory.Exists(Path.Combine(root, "moza-refused")), "Wrong provider still opened native SDK");
        });
        test("Pit House SDK unavailable errors stay actionable without loading vendor code", () =>
        {
            Refuses(() => new MozaNativeApi(""));
            foreach (int code in new[] { 1, 2, 3, 4, 9, 10, 123 }) Refuses(() => MozaNativeApi.Check(code));
            MozaNativeApi.Check(0);
        });
    }
}
