using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class LogitechG27Checks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Refused(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Invalid G27 data was accepted.");
    }
    private static TuneInput Input() => new() {
        Hardware = BuiltInProfiles.Hardware().Single(h => h.Id == LogitechG27Support.HardwareId),
        Wheel = BuiltInProfiles.Wheels().Single(w => w.Id == LogitechG27Support.WheelId),
        Car = BuiltInProfiles.Cars().First(), DriftPack = BuiltInProfiles.DriftPacks().Single(p => p.Id == BuiltInProfiles.Cars().First().PackId),
        Intent = BuiltInProfiles.Intents().First(), LogitechG27 = new() };
    public static void Run(Action<string, Action> run, string root)
    {
        run("G27 screenshot limits accept endpoints and reject invalid controls", () => {
            foreach (var high in new[] { false, true }) {
                var s = new LogitechG27Settings { OverallEffectsStrength = high ? 150 : 0, SpringEffectStrength = high ? 150 : 0,
                    DamperEffectStrength = high ? 150 : 0, CenteringSpringStrength = high ? 150 : 0, DegreesOfRotation = high ? 900 : 40 };
                s.Validate();
            }
            foreach (var name in new[] { "OverallEffectsStrength", "SpringEffectStrength", "DamperEffectStrength", "CenteringSpringStrength" })
                foreach (var n in new[] { -1, 151 }) { var s = new LogitechG27Settings(); typeof(LogitechG27Settings).GetProperty(name)!.SetValue(s, n); Refused(s.Validate); }
            Refused(new LogitechG27Settings { DegreesOfRotation = 39 }.Validate);
            Refused(new LogitechG27Settings { DegreesOfRotation = 901 }.Validate);
            Refused(new LogitechG27Settings { Ac = new() { GainPct = 101 } }.Validate);
            Refused(new LogitechG27Settings { Ac = null! }.Validate);
            Refused(new LogitechG27Settings { SoftwareVersion = "bad\nversion" }.Validate);
        });
        run("G27 store preserves plan after invalid save and detects corruption", () => {
            var dir = Path.Combine(root, "g27-store"); var store = new LogitechG27Store(dir);
            Check(store.Load().DegreesOfRotation == 900 && !Directory.Exists(dir), "Reading defaults wrote a file.");
            var s = new LogitechG27Settings { OverallEffectsStrength = 125, DegreesOfRotation = 540, SoftwareVersion = "tester version" };
            store.Save(s); s.OverallEffectsStrength = 151; Refused(() => store.Save(s));
            Check(store.Load().OverallEffectsStrength == 125 && store.Load().DegreesOfRotation == 540, "Invalid save changed the old plan.");
            File.WriteAllText(Path.Combine(dir, "logitech-g27.json"), "null"); Refused(() => store.Load());
        });
        run("G27 engine preserves manual values without DD torque modelling or input mutation", () => {
            var input = Input(); input.LogitechG27!.OverallEffectsStrength = 143; input.LogitechG27.Ac.MinimumForcePct = 11;
            var before = JsonSerializer.Serialize(input); var result = new TuningEngine().Generate(input);
            Check(result.LogitechG27!.OverallEffectsStrength == 143 && result.Ac.MinimumForcePct == 11, "Manual baseline replaced.");
            Check(result.EstimatedPeakWheelTorqueNm == 0 && result.SelfSteerScore == 0 && result.StabilityScore == 0, "G27 invented DD scores.");
            result.LogitechG27.OverallEffectsStrength = 10; result.Ac.MinimumForcePct = 20;
            Check(JsonSerializer.Serialize(input) == before, "Generate returned mutable aliases into input.");
        });
        run("G27 uses only identity-matched AC gain calibration", () => {
            var input = Input(); var engine = new TuningEngine();
            var c = new CalibrationProfile { Key = new CalibrationEngine().BuildKey(input), Samples = 2, AcGainDelta = -7,
                TorqueLimitDelta = -20, WheelSpeedDelta = 15, DampingDelta = 20, FrictionDelta = 20 };
            var result = engine.Generate(input, c);
            Check(result.Ac.GainPct == 43 && result.LogitechG27!.Ac.GainPct == 50, "Gain adjustment lost or baseline changed.");
            Check(JsonSerializer.Serialize(result.LogitechG27) == JsonSerializer.Serialize(input.LogitechG27), "MOZA calibration altered manual Logitech settings.");
            c.AcGainDelta = int.MaxValue; Check(engine.Generate(input, c).Ac.GainPct == 62, "Calibration overflow or excessive gain step.");
            c.AcGainDelta = int.MinValue; input.LogitechG27!.Ac.GainPct = 5;
            Check(engine.Generate(input, c).Ac.GainPct == 0, "Negative calibrated gain escaped range.");
            input.LogitechG27.Ac.GainPct = 50;
            c.Key = "unrelated"; Check(engine.Generate(input, c).Ac.GainPct == 50, "Unrelated calibration applied.");
        });
        run("G27 plans invalidate tune confirmation and preserve legacy signatures", () => {
            var input = Input(); var engine = new TuningEngine(); var a = engine.Generate(input); var signature = GuidedWorkflowEngine.TuneSignature(a);
            foreach (Action<LogitechG27Settings> change in new Action<LogitechG27Settings>[] {
                s => s.OverallEffectsStrength++, s => s.DegreesOfRotation = 540, s => s.ReportCombinedPedals = true }) {
                var copy = RunHistoryStore.Clone(a); change(copy.LogitechG27!);
                Check(GuidedWorkflowEngine.TuneSignature(copy) != signature, "Changed plan retained previous confirmation.");
            }
            input.Hardware = BuiltInProfiles.Hardware().First(); input.Wheel = BuiltInProfiles.Wheels().First();
            var old = engine.Generate(input); input.LogitechG27 = null;
            Check(JsonSerializer.Serialize(old) == JsonSerializer.Serialize(engine.Generate(input)), "Unused G27 plan altered MOZA output.");
            Check(GuidedWorkflowEngine.TuneSignature(old) == Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { old.Ac, old.Azom }))), "Legacy signature changed.");
        });
        run("G27 saved tune and share code round trips retain supported values", () => {
            var input = Input(); input.LogitechG27!.SpringEffectStrength = 150; input.LogitechG27.DegreesOfRotation = 40;
            var result = new TuningEngine().Generate(input); var store = new ProfileStore(); var path = Path.Combine(root, "g27.adt.json");
            store.Save(new SavedTune { Name = "G27 fixture", Input = input, Result = result }, path);
            Check(store.Load(path).Input.LogitechG27!.SpringEffectStrength == 150, "Saved tune lost G27 controls.");
            var service = new ShareCodeService(); var payload = service.Decode(service.Encode(service.Create(input, result, new())));
            Check(service.ToTuneInput(payload).LogitechG27!.DegreesOfRotation == 40, "Share code lost rotation.");
            Check(service.BuildPreview(payload).Contains("G27") && !service.BuildPreview(payload).Contains("Base torque output"), "G27 share preview shows MOZA advice.");
            payload.Input.LogitechG27!.DamperEffectStrength = 151; Refused(() => service.Encode(payload));
        });
        run("G27 run history records driver-entered plans and compares each control", () => {
            var input = Input(); var store = new RunHistoryStore(Path.Combine(root, "g27-history")); var driver = store.GetOrCreateDriver("G27 fixture");
            var gears = new GearingTargetStore(Path.Combine(root, "g27-gears"));
            TuneVersion Capture(FfbProvider p) => store.CaptureTune(input, driver, "G27 test", new(), null, ffbProvider: p, gearingTargets: gears);
            var a = Capture(FfbProvider.LogitechG27); input.LogitechG27!.OverallEffectsStrength = 120; input.LogitechG27.EnableCenteringSpring = true;
            var b = Capture(FfbProvider.LogitechG27);
            Check(a.Settings["Manual.LogitechG27.OverallEffectsStrength"] == 100 && b.Settings["Manual.LogitechG27.EnableCenteringSpring"] == 1, "History missed manual controls.");
            Check(!a.Settings.Keys.Any(k => k.StartsWith("Generated.AZOM") || k.StartsWith("Generated.PitHouse")), "G27 snapshot included MOZA values.");
            var rows = SetupComparisonPresentation.Recorded(a, b).Where(r => r.Category == "G27 manual plan").ToArray();
            Check(rows.Length == 15 && rows.Count(r => r.Changed) == 2 && rows.All(r => r.Explanation.Contains("not hardware readback") && r.AfterHeading == "After plan"), "Comparison lost controls or misrepresented readback.");
            Check(rows.Single(r => r.Key.EndsWith("OverallEffectsStrength")).After == "120%" && rows.Single(r => r.Key.EndsWith("EnableCenteringSpring")).After == "On", "Known G27 units or switch states lost.");
            Refused(() => Capture(FfbProvider.SimHubAzom));
            store.CaptureTune(input, driver, "car only", new(), null, focus: TuningFocus.CarSetupOnly, gearingTargets: gears);
        });
        run("G27 remote context blocks AZOM writes even with a stale enabled flag", () => {
            using var hub = new TelemetryHubService(); var remote = new RemoteServerService(hub);
            try {
                var input = Input(); remote.UpdateTuneContext(input, new TuningEngine().Generate(input));
                typeof(RemoteServerService).GetField("_remoteWritesEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(remote, true);
                Check(!remote.RemoteWritesEnabled, "G27 allowed AZOM writes.");
                var context = (RemoteTuneContext)typeof(RemoteServerService).GetField("_tune", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(remote)!;
                Check(context.LogitechG27 is not null && context.RecommendedAzom is null && context.RecommendedAc is not null, "Remote publishes wrong provider.");
            } finally { remote.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
        run("G27 workflow uses manual instructions without SimHub or Pit House requirements", () => {
            Check((int)FfbProvider.Manual == 2 && (int)FfbProvider.LogitechG27 == 3, "Existing provider identities changed.");
            Check(!FfbProviderOptions.UsesAzom(new() { FfbProvider = FfbProvider.LogitechG27 }), "G27 requests AZOM.");
            Check(LogitechG27Support.Instructions(true).Contains("Global Device Settings") && LogitechG27Support.Instructions(false).Contains("does not apply"), "Guidance misses actual menu or manual-only boundary.");
        });
    }
}
