using System.IO;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class CamberSetupChecks
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid camber operation accepted"); }
    internal static void Run(Action<string, Action> test, string root)
    {
        foreach (bool packed in new[] { false, true })
            test("camber saved tenths stage and export identically for local modes 0 1 2: " + (packed ? "packed" : "unpacked"), () =>
            {
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    int before = mode == 2 ? 45 : -55; int after = before - 1;
                    var f = Fixture(root, $"SHOW_CLICKS={mode}", before, packed);
                    var p = f.Analysis.Parameters.Single(); p.RecommendedValue = after;
                    var original = File.ReadAllBytes(f.Baseline);
                    var plan = new PitSetupPlanService().Create(f.Analysis, "physics_car", "Camber test");
                    Check(plan.Changes.Single().Before == before && plan.Changes.Single().After == after, "Physical or displayed value sent instead of raw VALUE");
                    Check(p.Range!.Min == -10 && p.Range.Max == -2 && p.Range.Step == 1 && p.Range.CamberValueMode == mode, "Original definition metadata changed");
                    var output = Path.Combine(f.Root, "generated.ini"); new AssettoCorsaSetupService().WriteGenerated(f.Analysis, output);
                    var reloaded = new AssettoCorsaSetupService().LoadBaseline(output, f.Car);
                    Check(reloaded.Parameters.Single().CurrentValue == after && original.SequenceEqual(File.ReadAllBytes(f.Baseline)), "Round trip changed raw value or baseline");
                    Check(p.RangeText.Contains("saved VALUE") && p.RangeText.Contains("0.1"), "UI hides the serialized scale");
                    CarDataSource.EnsureUnchanged(f.Analysis.SourceEvidence!);
                }
            });
        test("camber adjustment direction is equivalent across actual normalized and offset formats", () =>
        {
            foreach (var section in new[] { "CAMBER_LF", "CAMBER_LR" })
                foreach (var intent in BuiltInProfiles.Intents())
                    foreach (int goal in new[] { -2, 0, 2 })
                    {
                        double? expected = null;
                        foreach (int mode in new[] { 0, 1, 2 })
                        {
                            var f = Fixture(root, $"SHOW_CLICKS={mode}", mode == 2 ? 45 : -55, section: section);
                            var input = Input(f.Car); input.Intent = intent;
                            new CarSetupTuningEngine().Generate(input, f.Analysis, SetupAggressiveness.Balanced, new() { FrontEndBite = goal, RearGrip = goal });
                            var p = f.Analysis.Parameters.Single();
                            Check(CamberSetupValues.TrySetupValue(p.Range, p.RecommendedValue!.Value, out var actual), "Invalid mapped recommendation");
                            if (expected.HasValue) Check(Math.Abs(expected.Value - actual) < .000001, "Offset encoding changed the intended camber direction");
                            expected = actual;
                        }
                    }
        });
        test("camber generation clips both end stops and reports holds instead of promising impossible changes", () =>
        {
            foreach (int mode in new[] { 0, 1, 2 })
                foreach (bool upper in new[] { false, true })
                {
                    var f = Fixture(root, $"SHOW_CLICKS={mode}", mode == 2 ? upper ? 80 : 0 : upper ? -20 : -100);
                    var p = f.Analysis.Parameters.Single();
                    for (int goal = -2; goal <= 2; goal++)
                    {
                        var analysis = new CarSetupTuningEngine().Generate(Input(f.Car), f.Analysis, SetupAggressiveness.Balanced, new() { FrontEndBite = goal, SelfSteerSpeed = goal });
                        Check(CamberSetupValues.IsLegal(p.Range, p.RecommendedValue!.Value), "Generated value escaped a camber endpoint");
                        if (p.Changed) {
                            new PitSetupPlanService().Create(analysis, "physics_car", "Boundary test");
                            new AssettoCorsaSetupService().WriteGenerated(analysis, Path.Combine(f.Root, $"boundary-{goal}.ini"));
                        }
                        else Check(!p.Reason.Contains("increase front bite", StringComparison.OrdinalIgnoreCase), "Held parameter promises a change");
                    }
                }
        });
        test("camber staging and export refuse out of range fractional and malformed saved values", () =>
        {
            foreach (var after in new[] { -101d, -19, -55.5, double.NaN, double.PositiveInfinity })
            {
                var f = Fixture(root, "SHOW_CLICKS=1", -55); f.Analysis.Parameters.Single().RecommendedValue = after;
                Reject(() => new PitSetupPlanService().Create(f.Analysis, "physics_car", "Invalid test"));
                // Existing writer omits nonfinite recommendations; test all finite changed outputs.
                if (double.IsFinite(after)) {
                    var output = Path.Combine(f.Root, "bad.ini"); Reject(() => new AssettoCorsaSetupService().WriteGenerated(f.Analysis, output));
                    Check(!File.Exists(output), "Rejected camber export created a tune");
                }
            }
            foreach (var before in new[] { -101d, -19, -55.5 })
            {
                var f = Fixture(root, "SHOW_CLICKS=1", before); f.Analysis.Parameters.Single().RecommendedValue = -55;
                Reject(() => new PitSetupPlanService().Create(f.Analysis, "physics_car", "Bad baseline"));
                new CarSetupTuningEngine().Generate(Input(f.Car), f.Analysis, SetupAggressiveness.Balanced, new() { FrontEndBite = 2 });
                Check(!f.Analysis.Parameters.Single().Changed && f.Analysis.Parameters.Single().Reason.Contains("no verified"), "Off-grid baseline silently repaired");
            }
        });
        test("camber unknown modes custom LUTs and ambiguous definitions stay unsupported", () =>
        {
            foreach (var definition in new[] { "SHOW_CLICKS=3", "SHOW_CLICKS=-1", "SHOW_CLICKS=NaN", "SHOW_CLICKS=1\nLUT=custom.lut", "SHOW_CLICKS=0\nRATIOS=custom.rto", "SHOW_CLICKS=0\nMIN=-20" })
            {
                var f = Fixture(root, definition, -55); f.Analysis.Parameters.Single().RecommendedValue = -56;
                Reject(() => new PitSetupPlanService().Create(f.Analysis, "physics_car", "Unsupported"));
                Reject(() => new AssettoCorsaSetupService().WriteGenerated(f.Analysis, Path.Combine(f.Root, "unknown.ini")));
                new CarSetupTuningEngine().Generate(Input(f.Car), f.Analysis, SetupAggressiveness.Balanced, new() { FrontEndBite = 2 });
                Check(!f.Analysis.Parameters.Single().Changed, "Unsupported camber was adjusted");
            }
        });
        test("camber distinguishes explicit local modes from ambiguous global offset display", () =>
        {
            foreach (var global in new[] { "", "[DISPLAY_METHOD]\nSHOW_CLICKS=0\n", "[DISPLAY_METHOD]\nSHOW_CLICKS=1\n" })
            {
                var f = Fixture(root, "", -55, global: global); f.Analysis.Parameters.Single().RecommendedValue = -56;
                new PitSetupPlanService().Create(f.Analysis, "physics_car", "Default encoding");
            }
            var unknown = Fixture(root, "", -55, global: "[DISPLAY_METHOD]\nSHOW_CLICKS=2\n"); unknown.Analysis.Parameters.Single().RecommendedValue = -56;
            Reject(() => new PitSetupPlanService().Create(unknown.Analysis, "physics_car", "Unknown inheritance"));
            var local = Fixture(root, "SHOW_CLICKS=1", -55, global: "[DISPLAY_METHOD]\nSHOW_CLICKS=2\n"); local.Analysis.Parameters.Single().RecommendedValue = -56;
            new PitSetupPlanService().Create(local.Analysis, "physics_car", "Local override");
        });
        test("camber shares the same mapping across four axles and rejects mismatched metadata", () =>
        {
            foreach (var section in new[] { "CAMBER_LF", "CAMBER_RF", "CAMBER_LR", "CAMBER_RR", "camber_lf" })
            {
                var f = Fixture(root, "SHOW_CLICKS=1", -55, section: section); var p = f.Analysis.Parameters.Single(); p.RecommendedValue = -56;
                new PitSetupPlanService().Create(f.Analysis, "physics_car", "Axle test");
                p.Range!.Section = "CAMBER_OTHER";
                Reject(() => new PitSetupPlanService().Create(f.Analysis, "physics_car", "Wrong axle"));
                Reject(() => new AssettoCorsaSetupService().WriteGenerated(f.Analysis, Path.Combine(f.Root, "wrong.ini")));
            }
        });
        test("camber source changes invalidate staging and export", () =>
        {
            var f = Fixture(root, "SHOW_CLICKS=1", -55); f.Analysis.Parameters.Single().RecommendedValue = -56;
            File.AppendAllText(Path.Combine(f.Car.SourceFolderPath!, "data", "setup.ini"), "\n; changed after analysis\n");
            Reject(() => new PitSetupPlanService().Create(f.Analysis, "physics_car", "Stale definition"));
            Reject(() => new AssettoCorsaSetupService().WriteGenerated(f.Analysis, Path.Combine(f.Root, "stale.ini")));
        });
    }
    private static TuneInput Input(CarProfile car) => new() { Car = car, Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Intent = BuiltInProfiles.Intents()[1] };
    private static FixtureData Fixture(string root, string mode, double before, bool packed = false, string global = "", string section = "CAMBER_LF")
    {
        var dir = Path.Combine(root, "camber-" + Guid.NewGuid().ToString("N")); var carDir = Path.Combine(dir, "physics_car"); Directory.CreateDirectory(carDir);
        var files = new Dictionary<string, string> { ["setup.ini"] = global + $"[{section}]\nMIN=-10\nMAX=-2\nSTEP=1\n" + mode + "\n" };
        if (packed) PackedArchiveChecks.WriteArchive(carDir, files);
        else { Directory.CreateDirectory(Path.Combine(carDir, "data")); File.WriteAllText(Path.Combine(carDir, "data", "setup.ini"), files["setup.ini"]); }
        var path = Path.Combine(dir, "baseline.ini"); File.WriteAllText(path, $"[CAR]\nMODEL=physics_car\n[{section}]\nVALUE={before.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}\n");
        var car = new CarProfile { Id = "physics_car", SourceFolderName = "physics_car", SourceFolderPath = carDir };
        return new(dir, path, car, new AssettoCorsaSetupService().LoadBaseline(path, car));
    }
    private sealed record FixtureData(string Root, string Baseline, CarProfile Car, CarSetupAnalysis Analysis);
}
