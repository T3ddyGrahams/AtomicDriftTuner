using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
using System.IO;
using System.Security.Cryptography;

internal static class CarPhysicsChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refuses(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected stale physics refusal."); }
    internal static (CarProfile Car, string Baseline) Fixture(string root)
    {
        var carPath = Path.Combine(root, "physics_car"); var data = Path.Combine(carPath, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "car.ini"), "[BASIC]\nTOTALMASS=1300\n");
        File.WriteAllText(Path.Combine(data, "suspensions.ini"), "[BASIC]\nWHEELBASE=2.6\nCG_LOCATION=0.54\n[FRONT]\nTYPE=STRUT\nSPRING_RATE=80000\nDAMP_BUMP=4000\nSTATIC_CAMBER=-4\n[REAR]\nTYPE=DWB\nSPRING_RATE=60000\n[ARB]\nFRONT=18000\nREAR=9000\n");
        File.WriteAllText(Path.Combine(data, "drivetrain.ini"), "[TRACTION]\nTYPE=RWD\n[DIFFERENTIAL]\nPOWER=0.7\nCOAST=0.4\nPRELOAD=80\n[GEARS]\nFINAL=4.1\n");
        File.WriteAllText(Path.Combine(data, "tyres.ini"), "[FRONT]\nNAME=Default\nWIDTH=0.235\n[REAR]\nWIDTH=0.245\n[FRONT_1]\nNAME=Selected front\nWIDTH=0.275\nPRESSURE_STATIC=28\nPRESSURE_IDEAL=32\n[REAR_1]\nNAME=Selected rear\nWIDTH=0.285\nPRESSURE_STATIC=26\n");
        File.WriteAllText(Path.Combine(data, "engine.ini"), "[ENGINE_DATA]\nLIMITER=8000\n");
        File.WriteAllText(Path.Combine(data, "brakes.ini"), "[DATA]\nFRONT_SHARE=0.62\nMAX_TORQUE=2500\n");
        File.WriteAllText(Path.Combine(data, "setup.ini"), "[SPRING_RATE_LF]\nMIN=1\nMAX=30\nSTEP=1\nSHOW_CLICKS=1\n[DIFF_POWER]\nMIN=0\nMAX=100\nSTEP=1\n[PRESSURE_LF]\nMIN=10\nMAX=45\nSTEP=1\n[TYRES]\nMIN=0\nMAX=1\nSTEP=1\n");
        var baseline = Path.Combine(root, "baseline.ini");
        File.WriteAllText(baseline, "[CAR]\nMODEL=physics_car\n[SPRING_RATE_LF]\nVALUE=12\n[DIFF_POWER]\nVALUE=60\n[PRESSURE_LF]\nVALUE=28\n[TYRES]\nVALUE=1\n[DIFF_COAST]\nVALUE=40\n");
        return (new() { SourceFolderName = "physics_car", SourceFolderPath = carPath, IsInstalled = true }, baseline);
    }
    public static void Run(Action<string, Action> test, string root)
    {
        test("physics history persists a digest without local paths and preserves previous snapshots", () =>
        {
            var f = Fixture(Path.Combine(root, "physics-history")); var input = new TuneInput { Car = f.Car };
            var store = new RunHistoryStore(Path.Combine(root, "physics-history-store")); var driver = store.GetOrCreateDriver("Fixture");
            var before = store.CaptureTune(input, driver, "Before", new(), null, f.Baseline);
            Check(before.BasePhysicsFingerprint.Length == 64, "Physics digest missing");
            File.AppendAllText(Path.Combine(f.Car.SourceFolderPath!, "data", "suspensions.ini"), "; new revision\n");
            var after = store.CaptureTune(input, driver, "After", new(), null, f.Baseline);
            Check(before.BasePhysicsFingerprint != after.BasePhysicsFingerprint, "Changed physics not captured");
            var reopened = new RunHistoryStore(store.RootDirectory).ListTunes(input, driver.Id);
            Check(reopened.Count == 2 && reopened.Single(t => t.Id == before.Id).BasePhysicsFingerprint == before.BasePhysicsFingerprint, "Previous snapshot overwritten");
            var json = System.Text.Json.JsonSerializer.Serialize(reopened);
            Check(!json.Contains("SourceFolderPath") && !json.Contains("CarPath") && !json.Contains("SPRING_RATE=80000"), "Raw physics or paths stored in history");
            after.Id = Guid.NewGuid().ToString("N"); after.BasePhysicsFingerprint = "invalid";
            try { store.SaveTune(after); } catch (InvalidDataException) { return; }
            throw new Exception("Malformed physics digest accepted");
        });
        test("physics comparison rejects mismatches while retaining legacy and matching comparisons", () =>
        {
            var first = IntelligenceChecks.Session(); var second = RunHistoryStore.Clone(first);
            second.Id = Guid.NewGuid().ToString("N"); second.StartedUtc = first.StartedUtc.AddMinutes(5);
            var before = new SavedTelemetrySession { Session = first, Analysis = new TelemetryAnalyzer().Analyze(first) };
            var after = new SavedTelemetrySession { Session = second, Analysis = new TelemetryAnalyzer().Analyze(second) };
            var engine = new RunComparisonEngine();
            Check(engine.Compare(before, after).Comparable, "Legacy comparison changed");
            first.Context!.Tune!.BasePhysicsFingerprint = new string('A', 64);
            Check(!engine.Compare(before, after).Comparable, "Missing after fingerprint accepted as known same");
            second.Context!.Tune!.BasePhysicsFingerprint = new string('B', 64);
            var changed = engine.Compare(before, after);
            Check(!changed.Comparable && !changed.RecommendationTestTracked && changed.Limitations.Any(x => x.StartsWith("Base car physics changed")), "Physics revision attributed to setup");
            second.Context.Tune.BasePhysicsFingerprint = first.Context.Tune.BasePhysicsFingerprint;
            Check(engine.Compare(before, after).Comparable, "Matching physics rejected");
        });
        test("physics import selects saved tyre compound and preserves units/source without writing", () =>
        {
            var f = Fixture(Path.Combine(root, "physics-read"));
            var files = Directory.GetFiles(f.Car.SourceFolderPath!, "*", SearchOption.AllDirectories);
            var before = files.ToDictionary(x => x, x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))));
            var a = new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car); var p = a.Physics!;
            Check(p.Available && p.Facts.Count > 15 && p.HasSetupDefinition && p.DriveType == "RWD", "Incomplete physics import");
            Check(p.Find("tyres.ini", "FRONT_1", "WIDTH")!.Value == "275" && p.Find("tyres.ini", "FRONT", "WIDTH") is null, "Wrong active tyre inferred");
            Check(p.Find("suspensions.ini", "FRONT", "SPRING_RATE")!.Value == "80000" && a.Parameters[0].CurrentValue == 12, "Base value replaced saved clicks");
            Check(p.Details.Contains("not a cold setup target") && p.Details.Contains("not simulated"), "Limits missing");
            foreach (var file in files) Check(before[file] == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), "Reader modified car data");
            Check(p.Find("car.ini", "BASIC", "TOTALMASS")!.Value == "1300", "Base mass missing");
        });
        test("physics guidance adds provenance and holds controls not exposed in setup definition", () =>
        {
            var f = Fixture(Path.Combine(root, "physics-guidance")); var input = new TuneInput { Car = f.Car };
            var service = new AssettoCorsaSetupService(); var engine = new CarSetupTuningEngine();
            var a = engine.Generate(input, service.LoadBaseline(f.Baseline, f.Car), SetupAggressiveness.Balanced, new() { FrontEndBite = 1 });
            Check(a.Parameters.Single(p => p.Section == "DIFF_POWER").Changed, "Valid RWD recommendation lost");
            var coast = a.Parameters.Single(p => p.Section == "DIFF_COAST");
            Check(!coast.Changed && coast.Reason.Contains("not exposed"), "Unexposed control was changed");
            Check(a.Parameters.Single(p => p.Section == "SPRING_RATE_LF").PhysicsContext.Contains("80000") &&
                a.Parameters.Single(p => p.Section == "PRESSURE_LF").PhysicsContext.Contains("FRONT_1"), "Relevant base context missing");
            var output = service.WriteGenerated(a, Path.Combine(root, "physics-guidance", "generated.ini"));
            Check(File.ReadAllText(output).Contains("[CAR]\nMODEL=physics_car".Replace("\n", Environment.NewLine)) || File.ReadAllText(output).Contains("MODEL=physics_car"), "Setup identity lost");
        });
        foreach (var drive in new[] { "FWD", "AWD" })
            test("physics guidance holds unverified differential adjustments for " + drive, () =>
            {
                var f = Fixture(Path.Combine(root, "physics-" + drive)); var path = Path.Combine(f.Car.SourceFolderPath!, "data", "drivetrain.ini");
                File.WriteAllText(path, File.ReadAllText(path).Replace("TYPE=RWD", "TYPE=" + drive));
                var a = new CarSetupTuningEngine().Generate(new() { Car = f.Car }, new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car), SetupAggressiveness.Balanced);
                Check(a.Parameters.Where(p => p.Section.StartsWith("DIFF_")).All(p => !p.Changed && p.Reason.Contains(drive)), "Rear-drive advice applied to different drivetrain");
            });
        test("physics import opt-out preserves existing baseline-only recommendations", () =>
        {
            var f = Fixture(Path.Combine(root, "physics-off")); var service = new AssettoCorsaSetupService();
            var a = new CarSetupTuningEngine().Generate(new() { Car = f.Car }, service.LoadBaseline(f.Baseline, f.Car, false), SetupAggressiveness.Balanced);
            Check(!a.Physics!.Available && a.Parameters.Single(p => p.Section == "DIFF_COAST").Changed, "Opt-out unexpectedly added physics guard");
        });
        foreach (var kind in new[] { "packed", "ambiguous", "missing" })
            test("physics import explains unavailable source: " + kind, () =>
            {
                var carPath = Path.Combine(root, "physics-unavailable-" + kind); Directory.CreateDirectory(carPath);
                if (kind != "missing") File.WriteAllText(Path.Combine(carPath, "data.acd"), "fixture");
                if (kind == "ambiguous") Directory.CreateDirectory(Path.Combine(carPath, "data"));
                var p = new CarPhysicsService().Read(new() { SourceFolderPath = carPath });
                Check(!p.Available && p.Facts.Count == 0 && p.Status.Length > 30, "Unavailable source fabricated data");
            });
        test("physics import never guesses a compound and rejects malformed duplicate values", () =>
        {
            var f = Fixture(Path.Combine(root, "physics-invalid"));
            File.WriteAllText(Path.Combine(f.Car.SourceFolderPath!, "data", "suspensions.ini"), "[FRONT]\nSPRING_RATE=80000\nSPRING_RATE=90000\n");
            var p = new CarPhysicsService().Read(f.Car);
            Check(p.Facts.All(x => x.File != "suspensions.ini" && x.File != "tyres.ini") && p.Notes.Any(n => n.Contains("unknown until")), "Ambiguous values were used");
            var baseline = new[] { new CarSetupParameter { Section = "TYRES", CurrentValue = 9 } };
            p = new CarPhysicsService().Read(f.Car, baseline);
            Check(p.Facts.All(x => x.File != "tyres.ini") && p.Notes.Any(n => n.Contains("no fallback compound")), "Missing compound defaulted silently");
        });
        foreach (var name in new[] { "suspensions.ini", "setup.ini", "data.acd", "brakes.ini" })
            test("physics changes refuse save and pit stage: " + name, () =>
            {
                var f = Fixture(Path.Combine(root, "physics-stale-" + name)); var service = new AssettoCorsaSetupService();
                var a = new CarSetupTuningEngine().Generate(new() { Car = f.Car }, service.LoadBaseline(f.Baseline, f.Car), SetupAggressiveness.Balanced);
                File.AppendAllText(Path.Combine(f.Car.SourceFolderPath!, name == "data.acd" ? "" : "data", name), "; changed");
                Refuses(() => service.WriteGenerated(a, Path.Combine(root, "should-not-exist-" + name)));
                Refuses(() => new PitSetupPlanService().Create(a, "physics_car", "test"));
            });
    }
}
