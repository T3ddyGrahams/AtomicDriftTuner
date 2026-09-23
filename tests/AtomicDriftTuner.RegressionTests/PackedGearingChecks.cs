using System.IO;
using System.Security.Cryptography;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PackedGearingChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("packed gearing refuses real adjustable limiter controls and changed limiter evidence on export", () =>
        {
            foreach (var savedOnly in new[] { true, false })
            {
                var f = Fixture(root); var plan = GearingPlanner.Plan(f.Load(), Target());
                if (savedOnly) File.AppendAllText(f.Baseline, "\n[ENGINE_LIMITER]\nVALUE=80\n");
                else { f.Entries["setup.ini"] += "\n[ENGINE_LIMITER]\nMIN=70\nMAX=100\n"; f.Repack(); }
                Refuse(() => f.Load());
                var output = Path.Combine(f.Root, "changed-limiter.ini"); Refuse(() => f.Service.Save(plan, output));
                Check(!File.Exists(output), "Changed limiter created a stale packed setup");
            }
        });
        test("gearing loads and exports matching packed and unpacked copies without changing physics", () =>
        {
            var f = Fixture(root);
            UnpackFixture(f);
            var paths = Directory.GetFiles(f.Car.SourceFolderPath!, "*", SearchOption.AllDirectories);
            var hashes = paths.ToDictionary(p => p, Hash);
            var baseline = File.ReadAllText(f.Baseline);
            var plan = GearingPlanner.Plan(f.Load(), Target());
            Check(plan.Data.CarDataEvidence?.MatchingUnpackedCopy == true && plan.Recommended.FinalDrive.Ratio == 4,
                "Matching copies changed gearing calculation or lost provenance");
            var output = Path.Combine(f.Root, "matching-gearing.ini");
            f.Service.Save(plan, output);
            Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == baseline.Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2").TrimEnd(),
                "Matching-source export changed unrelated controls");
            Check(File.ReadAllText(f.Baseline) == baseline && paths.All(p => Hash(p) == hashes[p]), "Export modified baseline or installed physics");
        });
        test("gearing refuses stale export after either matching source changes", () =>
        {
            foreach (var change in new[] { "unpacked", "packed", "both" })
            {
                var f = Fixture(root); UnpackFixture(f);
                var plan = GearingPlanner.Plan(f.Load(), Target());
                f.Entries["suspensions.ini"] += "; changed\n";
                if (change is "unpacked" or "both")
                    File.WriteAllText(Path.Combine(f.Car.SourceFolderPath!, "data", "suspensions.ini"), f.Entries["suspensions.ini"]);
                if (change is "packed" or "both") f.Repack();
                var output = Path.Combine(f.Root, "stale-matching.ini");
                Refuse(() => f.Service.Save(plan, output));
                Check(!File.Exists(output), "Changed dual source produced a stale gearing file");
            }
        });
        test("packed gearing decodes saved indexes in file order with source evidence", () =>
        {
            var f = Fixture(root);
            var data = f.Load();
            Check(data.FinalDrives.Select(x => x.Ratio).SequenceEqual(new[] { 4.5, 3.0, 4.0 }), "Packed ratio order changed");
            Check(data.CurrentIndex == 1 && data.FinalDrives[data.CurrentIndex].Ratio == 3, "Saved index was treated as a ratio");
            Check(data.CarDataEvidence?.Kind == "packed", "Packed source provenance missing");
            Check(data.Fingerprints.Count == 1 && data.Fingerprints.ContainsKey(f.Baseline), "Baseline is not separately fingerprinted");
            var plan = GearingPlanner.Plan(data, Target());
            Check(plan.Recommended.FinalDrive.Index == 2 && plan.Recommended.FinalDrive.Ratio == 4, "Packed planner result differs from standard fixture");
            Near(plan.Recommended.LowRpm, 3183.0988618, .001);
            Near(plan.Recommended.HighRpm, 5305.1647697, .001);
        });

        test("packed gearing saves only final drive without unpacking or altering the car", () =>
        {
            var f = Fixture(root);
            var archiveHash = Hash(f.Archive);
            var original = File.ReadAllText(f.Baseline);
            var output = Path.Combine(f.Root, "gearing.ini");
            f.Service.Save(GearingPlanner.Plan(f.Load(), Target()), output);
            Check(File.ReadAllText(f.Baseline) == original, "Packed gearing overwrote baseline");
            var expected = original.Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2");
            Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == expected.TrimEnd(), "Packed export changed unrelated setup values");
            Check(Hash(f.Archive) == archiveHash, "Packed car archive changed");
            Check(!Directory.Exists(Path.Combine(f.Car.SourceFolderPath!, "data")), "Packed data was extracted into installed car");
            Check(Directory.GetFiles(f.Car.SourceFolderPath!, "*", SearchOption.AllDirectories).Length == 1, "Unexpected installed car write");
        });

        test("packed gearing rejects archive changes including non-gearing files", () =>
        {
            var f = Fixture(root);
            var plan = GearingPlanner.Plan(f.Load(), Target());
            f.Entries["suspensions.ini"] += "\n; changed physics\n";
            f.Repack();
            var output = Path.Combine(f.Root, "changed.ini");
            Refuse(() => f.Service.Save(plan, output));
            Check(!File.Exists(output), "Changed archive produced a stale gearing export");
        });

        test("packed gearing rejects changed baseline and newly ambiguous source", () =>
        {
            var baselineCase = Fixture(root);
            var baselinePlan = GearingPlanner.Plan(baselineCase.Load(), Target());
            File.AppendAllText(baselineCase.Baseline, "\n[PRESSURE_LF]\nVALUE=24\n");
            Refuse(() => baselineCase.Service.Save(baselinePlan, Path.Combine(baselineCase.Root, "changed.ini")));
            var sourceCase = Fixture(root);
            var sourcePlan = GearingPlanner.Plan(sourceCase.Load(), Target());
            Directory.CreateDirectory(Path.Combine(sourceCase.Car.SourceFolderPath!, "data"));
            Refuse(() => sourceCase.Service.Save(sourcePlan, Path.Combine(sourceCase.Root, "ambiguous.ini")));
        });

        test("packed gearing resolves forward gear three from internal gear four", () =>
        {
            var f = Fixture(root);
            f.Entries["setup.ini"] += "\n[GEAR_3]\nRATIOS=third.rto\n";
            f.Entries["third.rto"] = "First|1.8\nSecond|1.4\n";
            File.AppendAllText(f.Baseline, "\n[INTERNAL_GEAR_4]\nVALUE=1\n");
            f.Repack();
            Near(f.Load().GearRatio, 1.4, 0);
            File.WriteAllText(f.Baseline, File.ReadAllText(f.Baseline).Replace("[INTERNAL_GEAR_4]", "[INTERNAL_GEAR_3]"));
            Refuse(() => f.Load());
        });

        test("packed gearing uses the saved gearset and selected driven tyre compound", () =>
        {
            var f = Fixture(root);
            f.Entries["setup.ini"] += "\n[GEARS]\nUSE_GEARSET=1\n[GEAR_SET_0]\nGEAR_3=1.5\n[GEAR_SET_1]\nNAME=Short box\nGEAR_3=2.0\n";
            f.Entries["tyres.ini"] += "\n[FRONT_1]\nRADIUS=0.31\n[REAR_1]\nRADIUS=0.34\n";
            File.WriteAllText(f.Baseline, File.ReadAllText(f.Baseline).Replace("[TYRES]\nVALUE=0", "[TYRES]\nVALUE=1") + "\n[GEARSET]\nVALUE=1\n");
            f.Repack();
            var rwd = f.Load();
            Near(rwd.GearRatio, 2, 0);
            Near(rwd.TyreRadius, .34, 0);
            f.Entries["drivetrain.ini"] = f.Entries["drivetrain.ini"].Replace("TYPE=RWD", "TYPE=FWD");
            f.Repack();
            Near(f.Load().TyreRadius, .31, 0);
        });

        test("packed gearing retains strict ratio and setup mapping rejection", () =>
        {
            foreach (var bad in new[] { "X|3\nX|4", "Bad|NaN", "Bad|-4", "", "broken line" })
            {
                var f = Fixture(root);
                f.Entries["final.rto"] = bad;
                f.Repack();
                Refuse(() => f.Load());
            }
            var traversal = Fixture(root);
            traversal.Entries["setup.ini"] = "[FINAL_GEAR_RATIO]\nRATIOS=../final.rto\n";
            traversal.Repack();
            Refuse(() => traversal.Load());
            var missing = Fixture(root);
            missing.Entries.Remove("final.rto");
            missing.Repack();
            Refuse(() => missing.Load());
            var wrongCar = Fixture(root);
            File.WriteAllText(wrongCar.Baseline, File.ReadAllText(wrongCar.Baseline).Replace("MODEL=physics_car", "MODEL=other_car"));
            Refuse(() => wrongCar.Load());
        });
    }

    private static GearingTarget Target() => new() { MinimumSpeedKmh = 60, MaximumSpeedKmh = 100, MinimumRpm = 3100, MaximumRpm = 5400 };
    private static void UnpackFixture(PackedFixture fixture)
    {
        var data = Path.Combine(fixture.Car.SourceFolderPath!, "data");
        Directory.CreateDirectory(data);
        foreach (var file in fixture.Entries) File.WriteAllText(Path.Combine(data, file.Key), file.Value);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected, double tolerance) => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
    private static void Refuse(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("An unsupported or stale packed gearing operation was accepted");
    }

    private static PackedFixture Fixture(string root)
    {
        var folder = Path.Combine(root, "packed-gearing-" + Guid.NewGuid().ToString("N"));
        var car = new CarProfile { SourceFolderName = "physics_car", SourceFolderPath = Path.Combine(folder, "physics_car") };
        Directory.CreateDirectory(car.SourceFolderPath);
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["setup.ini"] = "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n",
            ["drivetrain.ini"] = "[TRACTION]\nTYPE=RWD\n[GEARS]\nCOUNT=6\nGEAR_3=1.5\nFINAL=3\n",
            ["engine.ini"] = "[ENGINE_DATA]\nLIMITER=8000\n",
            ["tyres.ini"] = "[COMPOUND_DEFAULT]\nINDEX=1\n[FRONT]\nRADIUS=0.28\n[REAR]\nRADIUS=0.3\nNAME=Test rear\n",
            ["suspensions.ini"] = "[FRONT]\nSPRING_RATE=80000\n[REAR]\nSPRING_RATE=60000\n",
            ["final.rto"] = "Short|4.5\nLong|3\nMiddle|4\n"
        };
        var baseline = Path.Combine(folder, "baseline.ini");
        File.WriteAllText(baseline, "; preserve handling\n[CAR]\nMODEL=physics_car\n[FINAL_RATIO]\nVALUE=1\n[TYRES]\nVALUE=0\n[CAMBER_LF]\nVALUE=-30\n");
        var result = new PackedFixture(folder, car, baseline, entries);
        result.Repack();
        return result;
    }

    private sealed record PackedFixture(string Root, CarProfile Car, string Baseline, Dictionary<string, string> Entries)
    {
        public string Archive => Path.Combine(Car.SourceFolderPath!, "data.acd");
        public GearingDataService Service { get; } = new();
        public GearingData Load() => Service.Load(Car, Baseline, 3);
        public void Repack() => PackedArchiveChecks.WriteArchive(Car.SourceFolderPath!, Entries);
    }
}
