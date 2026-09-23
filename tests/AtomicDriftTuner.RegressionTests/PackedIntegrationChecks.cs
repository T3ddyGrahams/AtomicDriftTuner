using System.IO;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PackedIntegrationChecks
{
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static (CarProfile Car, string Baseline, Dictionary<string, string> Files) Fixture(string root)
    {
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"), "physics_car"); Directory.CreateDirectory(folder);
        var files = new Dictionary<string, string> {
            ["car.ini"] = "[BASIC]\nTOTALMASS=1200\n",
            ["setup.ini"] = "[DIFF_POWER]\nMIN=10\nMAX=65\nSTEP=1\n[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n[ENGINE_MAPS]\nSHOW_CLICKS=0\nLUT=maps.lut\n",
            ["drivetrain.ini"] = "[TRACTION]\nTYPE=RWD\n[DIFFERENTIAL]\nPOWER=0.6\n[GEARS]\nFINAL=4\n",
            ["engine.ini"] = "[ENGINE_DATA]\nLIMITER=8000\n[MAP]\nMAP_0=low.lut\nMAP_1=high.lut\n",
            ["final.rto"] = "4.30|4.80\n3.63|4.23\n", ["maps.lut"] = "Low|0\nHigh|1\n",
            ["low.lut"] = "0|1\n8000|1\n", ["high.lut"] = "0|1.2\n8000|1.3\n" };
        PackedArchiveChecks.WriteArchive(folder, files);
        var baseline = Path.Combine(Path.GetDirectoryName(folder)!, "baseline.ini");
        File.WriteAllText(baseline, "[CAR]\nMODEL=physics_car\n[DIFF_POWER]\nVALUE=60\n[FINAL_RATIO]\nVALUE=1\n[ENGINE_MAPS]\nVALUE=0\n");
        return (new() { SourceFolderPath = folder, SourceFolderName = "physics_car" }, baseline, files);
    }
    public static void Run(Action<string, Action> test, string root)
    {
        test("packed setup guidance uses declared ranges and keeps label distinct from ratio", () =>
        {
            var f = Fixture(root); var service = new AssettoCorsaSetupService(); var baseline = service.LoadBaseline(f.Baseline, f.Car);
            Check(baseline.HasSetupDefinition && baseline.Parameters.Single(x => x.Section == "DIFF_POWER").Range!.Max == 65, "Packed ranges were ignored");
            var ratio = baseline.Physics!.DecodedSettings.Single(x => x.Section == "FINAL_RATIO");
            Check(ratio.NumericValue == 4.23 && ratio.Value.Contains("3.63"), "Saved label was used as physical ratio");
            var generated = new CarSetupTuningEngine().Generate(new() { Car = f.Car }, baseline, SetupAggressiveness.Aggressive);
            Check(generated.Parameters.Single(x => x.Section == "DIFF_POWER").RecommendedValue <= 65, "Packed range not respected");
            var output = service.WriteGenerated(generated, Path.Combine(Path.GetDirectoryName(f.Baseline)!, "output.ini"));
            Check(File.ReadAllText(output).Contains("[ENGINE_MAPS]"), "Unchanged engine map lost");
        });
        test("packed cache refreshes on archive changes and invalidates old setup guidance", () =>
        {
            var f = Fixture(root); var service = new AssettoCorsaSetupService(); var a = service.LoadBaseline(f.Baseline, f.Car);
            var first = a.Physics!.Fingerprint; f.Files["high.lut"] += "; new map revision\n"; PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, f.Files);
            Check(service.LoadBaseline(f.Baseline, f.Car).Physics!.Fingerprint != first, "Cache reused stale physics");
            try { service.WriteGenerated(a, Path.Combine(Path.GetDirectoryName(f.Baseline)!, "stale.ini")); }
            catch (InvalidOperationException) { return; }
            throw new Exception("Stale mapping was exported");
        });
        test("history retains verified meanings and blocks unnamed setup attribution", () =>
        {
            var f = Fixture(root); var input = new TuneInput { Car = f.Car };
            var store = new RunHistoryStore(Path.Combine(Path.GetDirectoryName(f.Baseline)!, "history")); var driver = store.GetOrCreateDriver("Fixture");
            var before = store.CaptureTune(input, driver, "Before", new(), null, f.Baseline);
            var text = File.ReadAllText(f.Baseline);
            File.WriteAllText(f.Baseline, text.Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=0"));
            var after = store.CaptureTune(input, driver, "After", new(), null, f.Baseline);
            Check(store.ListTunes(input).Single(x => x.Id == before.Id).DecodedSetup.Any(x => x.NumericValue == 4.23), "Decoded history did not survive reopening");
            var first = IntelligenceChecks.Session(); var second = RunHistoryStore.Clone(first); second.Id = Guid.NewGuid().ToString("N"); second.StartedUtc = first.StartedUtc.AddMinutes(2);
            first.Context!.DriverId = second.Context!.DriverId = driver.Id; first.Context.Tune = before; second.Context.Tune = after;
            var a = new SavedTelemetrySession { Session = first, Analysis = new TelemetryAnalyzer().Analyze(first) };
            var b = new SavedTelemetrySession { Session = second, Analysis = new TelemetryAnalyzer().Analyze(second) };
            var comparison = new RunComparisonEngine().Compare(a, b);
            Check(comparison.Comparable && comparison.TuneChanges.Single(x => x.Metric == "ACSetup.FINAL_RATIO").Current.Contains("4.8"), "Comparison lost decoded meaning");
            File.WriteAllText(f.Baseline, "VALUE=2\n" + File.ReadAllText(f.Baseline));
            var incomplete = store.CaptureTune(input, driver, "Unnamed", new(), null, f.Baseline);
            Check(incomplete.HasUnassignedSetupValues, "Unnamed ECU-like value silently ignored");
            second.Context.Tune = incomplete;
            comparison = new RunComparisonEngine().Compare(a, b);
            Check(!comparison.Comparable && !comparison.RecommendationTestTracked && comparison.Limitations.Any(x => x.Contains("unnamed VALUE")), "Unknown control change credited as setup improvement");
            var analysis = new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car);
            var exported = new AssettoCorsaSetupService().WriteGenerated(analysis, Path.Combine(Path.GetDirectoryName(f.Baseline)!, "preserved.ini"));
            Check(File.ReadAllText(exported).StartsWith("VALUE=2"), "Unnamed original value lost");
        });
        test("unpacked source evidence includes referenced and newly added text files", () =>
        {
            var f = CarPhysicsChecks.Fixture(Path.Combine(root, "referenced-text"));
            var source = CarDataSource.Open(f.Car);
            File.WriteAllText(Path.Combine(f.Car.SourceFolderPath!, "data", "new_map.lut"), "0|1\n8000|1.3\n");
            try { CarDataSource.EnsureUnchanged(source.Evidence); } catch (InvalidDataException) { return; }
            throw new Exception("New physics text did not invalidate snapshot");
        });
    }
}
