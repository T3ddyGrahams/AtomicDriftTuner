using System.IO;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class GearingChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("gearing decodes unsorted final drives without confusing index and ratio", () =>
        {
            var f = Fixture(root);
            var data = f.Load();
            Check(data.FinalDrives.Select(x => x.Ratio).SequenceEqual(new[] { 4.5, 3.0, 4.0 }), "List order changed");
            Check(data.CurrentIndex == 1 && data.FinalDrives[data.CurrentIndex].Ratio == 3, "Current ratio not decoded");
            var plan = GearingPlanner.Plan(data, Target());
            Check(plan.Recommended.FinalDrive.Index == 2 && plan.Recommended.FinalDrive.Ratio == 4 && plan.HasChange, "Did not select actual 4:1 ratio");
            Near(plan.Recommended.LowRpm, 3183.0988618, .001);
            Near(plan.Recommended.HighRpm, 5305.1647697, .001);
        });
        test("gearing export changes only final drive and preserves the baseline", () =>
        {
            var f = Fixture(root);
            var original = File.ReadAllText(f.Baseline);
            var plan = GearingPlanner.Plan(f.Load(), Target());
            var output = Path.Combine(f.Root, "gearing.ini");
            f.Service.Save(plan, output);
            Check(File.ReadAllText(f.Baseline) == original, "Baseline was overwritten");
            var expected = original.Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2");
            Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == expected.TrimEnd(), "Unrelated setup fields changed");
            Refuse(() => f.Service.Save(plan, f.Baseline));
            Refuse(() => f.Service.Save(plan, Path.Combine(f.Car.SourceFolderPath!, "data", "setup.ini")));
        });
        test("gearing rejects stale baseline including non-gearing changes", () =>
        {
            var f = Fixture(root); var plan = GearingPlanner.Plan(f.Load(), Target());
            File.AppendAllText(f.Baseline, "\n[PRESSURE_LF]\nVALUE=24\n");
            Refuse(() => f.Service.Save(plan, Path.Combine(f.Root, "changed.ini")));
            Check(!File.Exists(Path.Combine(f.Root, "changed.ini")), "Stale output created");
        });
        test("gearing rejects reordered ratios and changed tyres after calculation", () =>
        {
            foreach (var name in new[] { "final.rto", "tyres.ini", "engine.ini", "setup.ini", "drivetrain.ini" })
            {
                var f = Fixture(root); var plan = GearingPlanner.Plan(f.Load(), Target());
                File.AppendAllText(Path.Combine(f.DataPath, name), "\n; changed\n");
                Refuse(() => f.Service.Save(plan, Path.Combine(f.Root, "changed.ini")));
            }
        });
        test("gearing detects a different car's baseline", () =>
        {
            var f = Fixture(root); f.ReplaceBaseline("MODEL=test_car", "MODEL=other_car");
            Refuse(() => f.Load());
        });
        test("gearing uses selected gearbox and refuses missing gears", () =>
        {
            var f = Fixture(root);
            File.AppendAllText(Path.Combine(f.DataPath, "setup.ini"), "\n[GEARS]\nUSE_GEARSET=1\n[GEAR_SET_0]\nGEAR_3=1.5\n[GEAR_SET_1]\nNAME=Short box\nGEAR_3=2.0\nGEAR_6=0\n");
            File.AppendAllText(f.Baseline, "\n[GEARSET]\nVALUE=1\n");
            Near(f.Load().GearRatio, 2, 0);
            Refuse(() => f.Load(6));
            f.ReplaceBaseline("[GEARSET]\nVALUE=1", "[GEARSET]\nVALUE=7");
            Refuse(() => f.Load());
        });
        test("gearing resolves individually adjustable gear from INTERNAL_GEAR index", () =>
        {
            var f = Fixture(root);
            File.AppendAllText(Path.Combine(f.DataPath, "setup.ini"), "\n[GEAR_3]\nRATIOS=third.rto\n");
            File.WriteAllText(Path.Combine(f.DataPath, "third.rto"), "First|1.8\nSecond|1.4\n");
            File.AppendAllText(f.Baseline, "\n[INTERNAL_GEAR_4]\nVALUE=1\n");
            Near(f.Load().GearRatio, 1.4, 0);
            f.ReplaceBaseline("[INTERNAL_GEAR_4]", "[GEAR_3]"); Refuse(() => f.Load());
        });
        test("gearing allows fixed transmission when unused gearset flag has no presets", () =>
        {
            var f = Fixture(root); File.AppendAllText(Path.Combine(f.DataPath, "setup.ini"), "\n[GEARS]\nUSE_GEARSET=1 ; author comment\n");
            Near(f.Load().GearRatio, 1.5, 0);
        });
        test("gearing uses selected driven tyre compound instead of default", () =>
        {
            var f = Fixture(root); f.ReplaceBaseline("[TYRES]\nVALUE=0", "[TYRES]\nVALUE=1");
            File.AppendAllText(Path.Combine(f.DataPath, "tyres.ini"), "\n[FRONT_1]\nRADIUS=0.31\n[REAR_1]\nRADIUS=0.34\n");
            Near(f.Load().TyreRadius, .34, 0);
            f.ReplaceData("drivetrain.ini", "TYPE=RWD", "TYPE=FWD");
            Near(f.Load().TyreRadius, .31, 0);
        });
        test("gearing refuses packed ambiguous AWD and adjustable-limiter sources", () =>
        {
            var f = Fixture(root); File.WriteAllText(Path.Combine(f.Car.SourceFolderPath!, "data.acd"), "packed"); Refuse(() => f.Load());
            f = Fixture(root); f.ReplaceData("drivetrain.ini", "TYPE=RWD", "TYPE=AWD"); Refuse(() => f.Load());
            f = Fixture(root); File.AppendAllText(f.Baseline, "\n[LIMITER]\nVALUE=90\n"); Refuse(() => f.Load());
            f = Fixture(root); File.Delete(Path.Combine(f.DataPath, "engine.ini")); Refuse(() => f.Load());
            f = Fixture(root); f.Car.SourceFolderPath = Path.Combine(f.Root, "missing"); Refuse(() => f.Load());
        });
        test("gearing recognizes ENGINE_LIMITER in saved and definition-only setups", () =>
        {
            foreach (var savedOnly in new[] { true, false })
            {
                var f = Fixture(root);
                File.AppendAllText(savedOnly ? f.Baseline : Path.Combine(f.DataPath, "setup.ini"),
                    "\n[ENGINE_LIMITER]\n" + (savedOnly ? "VALUE=80\n" : "MIN=70\nMAX=100\nSTEP=1\n"));
                Refuse(() => f.Load());
                f = Fixture(root); var plan = GearingPlanner.Plan(f.Load(), Target());
                File.AppendAllText(savedOnly ? f.Baseline : Path.Combine(f.DataPath, "setup.ini"), "\n[ENGINE_LIMITER]\nVALUE=80\n");
                var output = Path.Combine(f.Root, "changed-limiter.ini"); Refuse(() => f.Service.Save(plan, output));
                Check(!File.Exists(output), "Changed limiter created a stale setup");
            }
        });
        test("gearing rejects malformed indexes ratios paths and duplicate setup values", () =>
        {
            foreach (var bad in new[] { "-1", "1.5", "NaN", "Infinity", "3" })
            {
                var f = Fixture(root); f.ReplaceBaseline("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=" + bad); Refuse(() => f.Load());
            }
            foreach (var bad in new[] { "Bad|NaN", "Bad|-4", "X|3\nX|4", "", "broken line", "x|Infinity" })
            {
                var f = Fixture(root); File.WriteAllText(Path.Combine(f.DataPath, "final.rto"), bad); Refuse(() => f.Load());
            }
            var invalid = Fixture(root); invalid.ReplaceData("setup.ini", "final.rto", "../final.rto"); Refuse(() => invalid.Load());
            invalid = Fixture(root); File.AppendAllText(invalid.Baseline, "\n[FINAL_RATIO]\nVALUE=0\n"); Refuse(() => invalid.Load());
        });
        test("gearing never recommends a limiter breach and explains impossible speed ranges", () =>
        {
            var f = Fixture(root);
            var data = f.Load() with { LimiterRpm = 5500 };
            var plan = GearingPlanner.Plan(data, Target());
            Check(plan.Recommended.HighRpm < 5500, "Selected a limiter breach");
            Refuse(() => GearingPlanner.Plan(data, Target() with { MinimumSpeedKmh = 240, MaximumSpeedKmh = 300 }));
            Refuse(() => GearingPlanner.Plan(data, Target() with { MaximumRpm = 6000 }));
        });
        test("gearing retains a best-match baseline and refuses no-op export", () =>
        {
            var f = Fixture(root); f.ReplaceBaseline("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2");
            var plan = GearingPlanner.Plan(f.Load(), Target());
            Check(!plan.HasChange, "Invented unnecessary change");
            Refuse(() => f.Service.Save(plan, Path.Combine(f.Root, "no-op.ini")));
        });
        test("gearing exposes a partial fit when no ratio spans the requested RPM band", () =>
        {
            var f = Fixture(root); var plan = GearingPlanner.Plan(f.Load(), Target() with { MinimumRpm = 4800, MaximumRpm = 5000 });
            Check(!plan.Recommended.FitsTarget, "Claimed a full fit where none exists");
        });
        test("gearing rejects invalid target fields and keeps unit conversion physical", () =>
        {
            foreach (var bad in new[] { Target() with { Gear = 0 }, Target() with { MinimumSpeedKmh = 100 },
                Target() with { MaximumRpm = double.NaN }, Target() with { MaximumSpeedKmh = double.PositiveInfinity },
                Target() with { MinimumRpm = 5500 } }) Refuse(bad.Validate);
            Near(GearingPlanner.Rpm(60 * GearingPlanner.KmhPerMph, 1.5, 4, .3), 5122.7012, .01);
        });
        test("gearing saves targets per car and preserves damaged storage", () =>
        {
            var folder = Path.Combine(root, "gear-targets"); var store = new GearingTargetStore(folder);
            var input = new TuneInput(); input.Car.Id = "car-a"; input.Car.SourceFolderName = "car-a";
            var target = Target() with { DisplayMph = true };
            store.Save(input, target); Check(store.Load(input) == target, "Target did not roundtrip");
            input.Car.Id = input.Car.SourceFolderName = "car-b"; Check(store.Load(input) is null, "Car target leaked to another car");
            input.Car.Id = input.Car.SourceFolderName = "car-a";
            var file = Directory.GetFiles(folder).Single(); File.WriteAllText(file, "broken");
            Refuse(() => store.Save(input, Target())); Check(File.ReadAllText(file) == "broken", "Damaged target overwritten");
        });

        // Optional read-only validation against an installed car; never exports to user/game folders.
        var liveCar = Environment.GetEnvironmentVariable("ADT_GEARING_TEST_CAR");
        var liveSetup = Environment.GetEnvironmentVariable("ADT_GEARING_TEST_SETUP");
        if (!string.IsNullOrWhiteSpace(liveCar) && !string.IsNullOrWhiteSpace(liveSetup))
            test("gearing reads an installed car and its saved baseline without modifying either", () =>
            {
                var car = new CarProfile { SourceFolderPath = liveCar, SourceFolderName = Path.GetFileName(liveCar) };
                var service = new GearingDataService(); var data = service.Load(car, liveSetup, 3);
                var plan = GearingPlanner.Plan(data, new GearingTarget { MaximumRpm = Math.Min(data.LimiterRpm * .9, 6500) });
                Console.WriteLine($"LIVE {car.SourceFolderName}: {data.GearSource}; gear {data.GearRatio}; tyre {data.TyreRadius}; current {plan.Current.FinalDrive.Ratio}; recommended {plan.Recommended.FinalDrive.Ratio}");
                if (plan.HasChange) service.Save(plan, Path.Combine(root, "live-car-gearing-preview.ini"));
            });
    }

    private static GearingTarget Target() => new() { MinimumSpeedKmh = 60, MaximumSpeedKmh = 100, MinimumRpm = 3100, MaximumRpm = 5400 };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Near(double actual, double expected, double tolerance) => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
    private static void Refuse(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or System.Text.Json.JsonException) { return; }
        throw new Exception("An unsupported or unsafe gearing operation was accepted");
    }
    private static CarFixture Fixture(string root)
    {
        var folder = Path.Combine(root, "gearing-" + Guid.NewGuid().ToString("N"));
        var car = new CarProfile { SourceFolderName = "test_car", SourceFolderPath = Path.Combine(folder, "test_car") };
        var data = Path.Combine(car.SourceFolderPath, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "setup.ini"), "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n");
        File.WriteAllText(Path.Combine(data, "drivetrain.ini"), "[TRACTION]\nTYPE=RWD\n[GEARS]\nCOUNT=6\nGEAR_3=1.5\nFINAL=3\n");
        File.WriteAllText(Path.Combine(data, "engine.ini"), "[ENGINE_DATA]\nLIMITER=8000\n");
        File.WriteAllText(Path.Combine(data, "tyres.ini"), "[COMPOUND_DEFAULT]\nINDEX=1\n[FRONT]\nRADIUS=0.28\n[REAR]\nRADIUS=0.3\nNAME=Test rear\n");
        File.WriteAllText(Path.Combine(data, "final.rto"), "Short|4.5\nLong|3\nMiddle|4\n");
        var baseline = Path.Combine(folder, "baseline.ini");
        File.WriteAllText(baseline, "; preserve handling\n[CAR]\nMODEL=test_car\n[FINAL_RATIO]\nVALUE=1\n[TYRES]\nVALUE=0\n[CAMBER_LF]\nVALUE=-30\n");
        return new(folder, car, data, baseline);
    }
    private sealed record CarFixture(string Root, CarProfile Car, string DataPath, string Baseline)
    {
        public GearingDataService Service { get; } = new();
        public GearingData Load(int gear = 3) => Service.Load(Car, Baseline, gear);
        public void ReplaceBaseline(string old, string value) => File.WriteAllText(Baseline, File.ReadAllText(Baseline).Replace(old, value));
        public void ReplaceData(string file, string old, string value)
        {
            var path = Path.Combine(DataPath, file); File.WriteAllText(path, File.ReadAllText(path).Replace(old, value));
        }
    }
}
