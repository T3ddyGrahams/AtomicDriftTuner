using System.IO;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PackedIdentityChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("packed setup decoding refuses another car's explicit baseline identity", () =>
        {
            var f = Fixture(root);
            var original = File.ReadAllText(f.Baseline).Replace("MODEL=physics_car", "MODEL=another_car");
            File.WriteAllText(f.Baseline, original);
            Refuse(() => new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car));
            var input = new TuneInput { Car = f.Car };
            var store = new RunHistoryStore(Path.Combine(f.Root, "history"));
            var driver = store.GetOrCreateDriver("Fixture");
            Refuse(() => store.CaptureTune(input, driver, "Wrong car", new(), null, f.Baseline));
            Check(store.ListTunes(input).Count == 0, "Wrong-car mapping was persisted");
            Check(File.ReadAllText(f.Baseline) == original, "Rejected baseline was modified");
        });

        test("packed setup decoding refuses duplicate CAR MODEL fields and sections", () =>
        {
            foreach (var duplicate in new[]
            {
                "MODEL=physics_car\nMODEL=physics_car",
                "MODEL=physics_car\nMODEL=another_car",
                "MODEL=physics_car\n[CAR]\nMODEL=physics_car"
            })
            {
                var f = Fixture(root);
                File.WriteAllText(f.Baseline, File.ReadAllText(f.Baseline).Replace("MODEL=physics_car", duplicate));
                Refuse(() => new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car));
            }
        });

        test("missing baseline car identity remains partial through saved history", () =>
        {
            var f = Fixture(root);
            File.WriteAllText(f.Baseline, File.ReadAllText(f.Baseline).Replace("[CAR]\nMODEL=physics_car\n", ""));
            var analysis = new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car);
            var mapping = analysis.Physics!.DecodedSettings.Single(x => x.Section == "FINAL_RATIO");
            Check(mapping.Status == DecodedSetupSetting.Partial && mapping.NumericValue == 4.23, "Unknown car identity was presented as verified");
            Check(mapping.Explanation.Contains("identity is unverified") && analysis.DecodeWarnings.Any(x => x.Contains("CAR/MODEL")), "Identity limitation was hidden");
            var input = new TuneInput { Car = f.Car };
            var store = new RunHistoryStore(Path.Combine(f.Root, "history"));
            var driver = store.GetOrCreateDriver("Fixture");
            var captured = store.CaptureTune(input, driver, "Unknown identity", new(), null, f.Baseline);
            Check(captured.DecodedSetup.Single(x => x.Section == "FINAL_RATIO").Status == DecodedSetupSetting.Partial, "Capture re-decoded the attachment as verified");
            var reopened = new RunHistoryStore(store.RootDirectory).ListTunes(input, driver.Id).Single();
            var stored = reopened.DecodedSetup.Single(x => x.Section == "FINAL_RATIO");
            Check(stored.Status == DecodedSetupSetting.Partial && stored.Explanation == mapping.Explanation, "Partial identity evidence was lost on disk round trip");
            Check(reopened.Settings["ACSetup.FINAL_RATIO"] == 1, "Unknown identity altered saved numeric value");
        });

        test("empty and malformed baseline headers preserve unnamed values and block attribution", () =>
        {
            foreach (var header in new[] { "[]", "[ ]", "[BROKEN" })
            {
                var f = Fixture(root);
                var original = File.ReadAllText(f.Baseline).Replace("MODEL=physics_car\n", "MODEL=physics_car\n" + header + "\nVALUE=2\n");
                File.WriteAllText(f.Baseline, original);
                var service = new AssettoCorsaSetupService();
                var analysis = service.LoadBaseline(f.Baseline, f.Car);
                Check(analysis.HasUnassignedValues && analysis.Parameters.Count == 1 && analysis.Parameters[0].Section == "FINAL_RATIO", "Unnamed value inherited the CAR section");
                Check(analysis.DecodeWarnings.Any(x => x.Contains("without a section name")), "Unnamed control warning missing");
                analysis.Parameters[0].RecommendedValue = 0;
                var output = service.WriteGenerated(analysis, Path.Combine(f.Root, "preserved.ini"));
                var expected = original.Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=0");
                Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == expected.TrimEnd(), "Unassigned entry or unrelated setup content changed");
                Check(File.ReadAllText(f.Baseline) == original, "Original unnamed baseline was altered");
                var input = new TuneInput { Car = f.Car };
                var store = new RunHistoryStore(Path.Combine(f.Root, "history"));
                var driver = store.GetOrCreateDriver("Fixture");
                var captured = store.CaptureTune(input, driver, "Unnamed control", new(), null, f.Baseline);
                Check(captured.HasUnassignedSetupValues && !captured.Settings.ContainsKey("ACSetup.CAR"), "History fabricated a named control from an invalid header");
                Check(new RunHistoryStore(store.RootDirectory).ListTunes(input).Single().HasUnassignedSetupValues, "Unnamed guard did not persist");

                var first = IntelligenceChecks.Session();
                var second = RunHistoryStore.Clone(first);
                second.Id = Guid.NewGuid().ToString("N"); second.StartedUtc = first.StartedUtc.AddMinutes(2);
                second.Context!.Tune!.HasUnassignedSetupValues = captured.HasUnassignedSetupValues;
                var comparison = new RunComparisonEngine().Compare(
                    new() { Session = first, Analysis = new TelemetryAnalyzer().Analyze(first) },
                    new() { Session = second, Analysis = new TelemetryAnalyzer().Analyze(second) });
                Check(!comparison.Comparable && !comparison.RecommendationTestTracked && comparison.Limitations.Any(x => x.Contains("unnamed VALUE")), "Unassigned setup values bypassed comparison guard");
            }
        });

        test("decoded-only physics keeps its fingerprint and rejects changed ratio history", () =>
        {
            var f = Fixture(root);
            f.Files.Remove("car.ini");
            // Base facts reject this malformed global line; the per-setting decoder can still
            // validate the independently well-formed FINAL_GEAR_RATIO section below it.
            f.Files["setup.ini"] = "unparsed global line\n" + f.Files["setup.ini"];
            PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, f.Files);
            var input = new TuneInput { Car = f.Car };
            var store = new RunHistoryStore(Path.Combine(f.Root, "history"));
            var driver = store.GetOrCreateDriver("Fixture");
            var baseline = new AssettoCorsaSetupService().LoadBaseline(f.Baseline, f.Car);
            Check(!baseline.Physics!.Available && baseline.Physics.Facts.Count == 0, "Fixture unexpectedly imported base facts");
            Check(baseline.Physics.DecodedSettings.Single().Status == DecodedSetupSetting.Verified, "Fixture did not reach independently verified setting mapping");
            Check(baseline.Physics.Fingerprint.Length == 64, "Decoded setting lost its source fingerprint");
            var before = store.CaptureTune(input, driver, "Before source change", new(), null, f.Baseline);
            f.Files["final.rto"] = "4.30|4.80\n3.63|4.31\n";
            PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, f.Files);
            var after = store.CaptureTune(input, driver, "After source change", new(), null, f.Baseline);
            Check(before.BasePhysicsFingerprint.Length == 64 && before.BasePhysicsFingerprint != after.BasePhysicsFingerprint, "Referenced ratio change was not fingerprinted");
            Check(before.DecodedSetup.Single().NumericValue == 4.23 && after.DecodedSetup.Single().NumericValue == 4.31, "Snapshot decoding did not follow exact file revision");
            var first = IntelligenceChecks.Session();
            var second = RunHistoryStore.Clone(first);
            second.Id = Guid.NewGuid().ToString("N"); second.StartedUtc = first.StartedUtc.AddMinutes(2);
            first.Context!.DriverId = second.Context!.DriverId = driver.Id;
            first.Context.Tune = before; second.Context.Tune = after;
            var comparison = new RunComparisonEngine().Compare(
                new() { Session = first, Analysis = new TelemetryAnalyzer().Analyze(first) },
                new() { Session = second, Analysis = new TelemetryAnalyzer().Analyze(second) });
            Check(!comparison.Comparable && !comparison.RecommendationTestTracked && comparison.Limitations.Any(x => x.StartsWith("Base car physics changed")), "Decoder-only file change escaped history source guard");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Refuse(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Ambiguous or wrong baseline car identity was accepted");
    }

    private static (string Root, CarProfile Car, string Baseline, Dictionary<string, string> Files) Fixture(string root)
    {
        var folder = Path.Combine(root, "packed-identity-" + Guid.NewGuid().ToString("N"));
        var car = new CarProfile { SourceFolderName = "physics_car", SourceFolderPath = Path.Combine(folder, "physics_car") };
        Directory.CreateDirectory(car.SourceFolderPath);
        var files = new Dictionary<string, string>
        {
            ["car.ini"] = "[BASIC]\nTOTALMASS=1200\n",
            ["setup.ini"] = "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n",
            ["final.rto"] = "4.30|4.80\n3.63|4.23\n"
        };
        PackedArchiveChecks.WriteArchive(car.SourceFolderPath, files);
        var baseline = Path.Combine(folder, "baseline.ini");
        File.WriteAllText(baseline, "[CAR]\nMODEL=physics_car\n[FINAL_RATIO]\nVALUE=1\n");
        return (folder, car, baseline, files);
    }
}
