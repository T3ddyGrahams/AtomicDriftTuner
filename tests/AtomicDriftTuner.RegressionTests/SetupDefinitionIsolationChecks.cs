using System.IO;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class SetupDefinitionIsolationChecks
{
    private const string Conflict = "\n[FRONT_BIAS]\nMIN=55\nMAX=100\nSTEP=1\nSHOW_CLICKS=0\n[front_bias]\nMIN=45\nMAX=85\nSTEP=1\nSHOW_CLICKS=0\n";
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { return; } throw new Exception("Unsafe definition accepted"); }
    public static void Run(Action<string, Action> test, string root)
    {
        foreach (var packed in new[] { false, true })
        {
            test($"setup conflict isolation preserves generation legal ARB staging and export ({(packed ? "packed" : "unpacked")})", () =>
            {
                var f = Fixture(root, packed); var original = File.ReadAllBytes(f.Baseline);
                var a = f.Load(); var input = new TuneInput { Car = f.Car, Intent = BuiltInProfiles.Intents().First(i => i.Kind == DriftStyleKind.Competition) };
                new CarSetupTuningEngine().Generate(input, a, SetupAggressiveness.Balanced, new() { RearGrip = 2 });
                var bias = a.Parameters.Single(p => p.Section == "FRONT_BIAS");
                Check(!bias.Changed && bias.Reason.Contains("duplicate") && bias.RangeText.Contains("duplicate"), "Conflicting brake bias was tuned or not explained");
                Check(a.DecodeWarnings.Any(w => w.Contains("FRONT_BIAS")), "Partial definition warning missing");
                var arb = a.Parameters.Single(p => p.Section == "ARB_REAR");
                Check(arb.Range is { Min: 0, Max: 30000, Step: 1000, ShowClicks: false }, "Valid ARB range was discarded");
                Check(arb.RecommendedValue == 4000, "Sub-step ARB delta escaped quantization");
                Check(a.Parameters.Single(p => p.Section == "DIFF_POWER").Changed, "Independent valid tuning was blocked");
                new PitSetupPlanService().Create(a, "physics_car", "Generated test");
                arb.RecommendedValue = 3000;
                var plan = new PitSetupPlanService().Create(a, "physics_car", "Reviewed legal ARB step");
                Check(plan.Changes.Any(c => c.Section == "ARB_REAR" && c.Before == 4000 && c.After == 3000) && plan.Changes.All(c => c.Section != "FRONT_BIAS"), "Staging lost raw ARB step or changed ambiguous control");
                var output = Path.Combine(f.Root, "export.ini"); new AssettoCorsaSetupService().WriteGenerated(a, output);
                var reread = new AssettoCorsaSetupService().LoadBaseline(output, f.Car);
                Check(reread.Parameters.Single(p => p.Section == "ARB_REAR").CurrentValue == 3000 && reread.Parameters.Single(p => p.Section == "FRONT_BIAS").CurrentValue == 60, "Export changed ambiguous bias or lost ARB value");
                Check(original.SequenceEqual(File.ReadAllBytes(f.Baseline)), "Source baseline modified"); CarDataSource.EnsureUnchanged(a.SourceEvidence!);
                arb.RecommendedValue = 3999; Reject(() => new PitSetupPlanService().Create(a, "physics_car", "Illegal step"));
            });
            test($"gearing ignores unrelated definition conflicts but preserves them in export ({(packed ? "packed" : "unpacked")})", () =>
            {
                var f = Fixture(root, packed); var target = new GearingTarget { Gear = 3, MinimumSpeedKmh = 60, MaximumSpeedKmh = 100, MinimumRpm = 3100, MaximumRpm = 5400 };
                var s = new GearingDataService(); var plan = s.Calculate(f.Car, f.Baseline, target);
                Check(plan.HasChange && plan.Recommended.FinalDrive.Ratio == 4 && plan.Data.DefinitionWarnings.Single().Contains("FRONT_BIAS"), "Unrelated conflict blocked or changed gearing ranking");
                var before = File.ReadAllBytes(f.Baseline); var output = Path.Combine(f.Root, "gearing.ini"); s.Save(plan, output);
                var expected = File.ReadAllText(f.Baseline).Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2");
                Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == expected.TrimEnd(), "Gearing export changed more than final drive");
                Check(before.SequenceEqual(File.ReadAllBytes(f.Baseline)), "Baseline modified"); CarDataSource.EnsureUnchanged(plan.Data.CarDataEvidence!);
            });
        }
        test("ambiguous controls cannot be forced through export or pit staging", () =>
        {
            foreach (var definition in new[] { Conflict, "\n[FRONT_BIAS]\nMIN=45\nMAX=85\nSTEP=1\nMIN=55\n", "\n[FRONT_BIAS]\nMIN=45\nMAX=85\nSTEP=1\n[FRONT_BIAS]\nMIN=45\nMAX=85\nSTEP=1\n" })
            {
                var f = Fixture(root); f.Entries["setup.ini"] = f.Entries["setup.ini"].Replace(Conflict, definition); f.Write();
                var a = f.Load(); a.Parameters.Single(p => p.Section == "FRONT_BIAS").RecommendedValue = 61;
                Reject(() => new PitSetupPlanService().Create(a, "physics_car", "Unsupported bias"));
                var output = Path.Combine(f.Root, "bad.ini"); Reject(() => new AssettoCorsaSetupService().WriteGenerated(a, output));
                Check(!File.Exists(output), "Rejected export created a file");
            }
        });
        test("ambiguous global display only disables controls that inherit it", () =>
        {
            var f = Fixture(root); f.Entries["setup.ini"] += "\n[DISPLAY_METHOD]\nSHOW_CLICKS=0\n[DISPLAY_METHOD]\nSHOW_CLICKS=1\n"; f.Write();
            var a = f.Load(); Check(a.Parameters.Single(p => p.Section == "DIFF_POWER").Range?.UnavailableReason?.Contains("DISPLAY_METHOD") == true, "Conflicting display silently defaulted");
            var arb = a.Parameters.Single(p => p.Section == "ARB_REAR"); arb.RecommendedValue = 3000;
            new PitSetupPlanService().Create(a, "physics_car", "Explicit local mode");
            new GearingDataService().Load(f.Car, f.Baseline, 3);
        });
        test("gearing relevant conflicting definitions remain blocked", () =>
        {
            foreach (var extra in new[] {
                "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n", "[GEARS]\nUSE_GEARSET=0\n[GEARS]\nUSE_GEARSET=1\n",
                "[GEAR_3]\nRATIOS=final.rto\n[GEAR_3]\nRATIOS=other.rto\n",
                "[GEARS]\nUSE_GEARSET=1\n[GEAR_SET_0]\nGEAR_3=1.5\n[GEAR_SET_0]\nGEAR_3=1.9\n",
                "[ENGINE_LIMITER]\nMIN=70\n[ENGINE_LIMITER]\nMIN=80\n",
                "[LIMITER]\nMIN=70\n[LIMITER]\nMIN=80\n" })
            {
                var f = Fixture(root); f.Entries["setup.ini"] += "\n" + extra; f.Write();
                if (extra.Contains("GEAR_SET_0")) File.AppendAllText(f.Baseline, "[GEARSET]\nVALUE=0\n");
                Reject(() => new GearingDataService().Load(f.Car, f.Baseline, 3));
            }
        });
        test("malformed definition boundaries cannot hide overrides or leak ranges", () =>
        {
            foreach (var bad in new[] { "[FRONT_BIAS", "[]", "[FINAL_GEAR_RATIO] trailing", "[[GEAR_3]]" })
            {
                var f = Fixture(root); f.Entries["setup.ini"] += "\n" + bad + "\nRATIOS=other.rto\n"; f.Write();
                Reject(() => new GearingDataService().Load(f.Car, f.Baseline, 3));
                var a = f.Load(); new CarSetupTuningEngine().Generate(new TuneInput { Car = f.Car }, a, SetupAggressiveness.Balanced);
                Check(a.Parameters.All(p => !p.Changed && p.Range?.UnavailableReason is not null), "Malformed boundary leaked a control range");
            }
        });
        test("saved baseline duplicates remain strict and changed car definitions invalidate plans", () =>
        {
            var f = Fixture(root); File.AppendAllText(f.Baseline, "[FRONT_BIAS]\nVALUE=60\n");
            Reject(() => new GearingDataService().Load(f.Car, f.Baseline, 3));
            var a = f.Load(); a.Parameters.Single(p => p.Section == "ARB_REAR").RecommendedValue = 3000;
            Reject(() => new PitSetupPlanService().Create(a, "physics_car", "Duplicate baseline"));
            f = Fixture(root); a = f.Load(); a.Parameters.Single(p => p.Section == "ARB_REAR").RecommendedValue = 3000;
            f.Entries["setup.ini"] += "\n; updated after analysis\n"; f.Write();
            Reject(() => new PitSetupPlanService().Create(a, "physics_car", "Stale car data"));
            Reject(() => new AssettoCorsaSetupService().WriteGenerated(a, Path.Combine(f.Root, "stale.ini")));
        });
    }
    private static FixtureData Fixture(string root, bool packed = false)
    {
        var dir = Path.Combine(root, "definitions-" + Guid.NewGuid().ToString("N"));
        var car = new CarProfile { Id = "physics_car", SourceFolderName = "physics_car", SourceFolderPath = Path.Combine(dir, "physics_car") }; Directory.CreateDirectory(car.SourceFolderPath);
        var baseline = Path.Combine(dir, "baseline.ini");
        File.WriteAllText(baseline, "[CAR]\nMODEL=physics_car\n[FINAL_RATIO]\nVALUE=1\n[TYRES]\nVALUE=0\n[FRONT_BIAS]\nVALUE=60\n[ARB_REAR]\nVALUE=4000\n[DIFF_POWER]\nVALUE=60\n");
        var entries = new Dictionary<string, string> {
            ["setup.ini"] = "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n[ARB_REAR]\nMIN=0\nMAX=30000\nSTEP=1000\nSHOW_CLICKS=0\n[DIFF_POWER]\nMIN=0\nMAX=100\nSTEP=1\n" + Conflict,
            ["drivetrain.ini"] = "[TRACTION]\nTYPE=RWD\n[GEARS]\nCOUNT=3\nGEAR_1=3.2\nGEAR_2=2.1\nGEAR_3=1.5\nFINAL=3\n",
            ["engine.ini"] = "[ENGINE_DATA]\nLIMITER=8000\n", ["tyres.ini"] = "[REAR]\nRADIUS=0.3\n",
            ["final.rto"] = "Short|4.5\nLong|3\nMiddle|4\n" };
        var f = new FixtureData(dir, car, baseline, entries, packed); f.Write(); return f;
    }
    private sealed record FixtureData(string Root, CarProfile Car, string Baseline, Dictionary<string, string> Entries, bool Packed)
    {
        public CarSetupAnalysis Load() => new AssettoCorsaSetupService().LoadBaseline(Baseline, Car);
        public void Write()
        {
            if (Packed) PackedArchiveChecks.WriteArchive(Car.SourceFolderPath!, Entries);
            else { var data = Path.Combine(Car.SourceFolderPath!, "data"); Directory.CreateDirectory(data); foreach (var p in Entries) File.WriteAllText(Path.Combine(data, p.Key), p.Value); }
        }
    }
}
