using System.Globalization;
using System.IO;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class SetupLegalityChecks
{
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Unsupported setup change accepted"); }
    internal static void Run(Action<string, Action> test, string root)
    {
        test("setup legality: offset rebound endpoint cannot export an eighteenth click", () =>
        {
            var f = Fixture(root, "DAMP_REBOUND_LR", "MIN=500\nMAX=9000\nSTEP=500\nSHOW_CLICKS=2", 17);
            f.Generate();
            Check(!f.Parameter.Changed && f.Parameter.Reason.StartsWith("Left unchanged"), "Maximum rebound click increased beyond its physical limit");
            f.Parameter.RecommendedValue = 18;
            f.RejectWrite();
        });
        test("setup legality: off-grid ARB and toe baselines never move opposite the request", () =>
        {
            foreach (var (section, definition, baseline) in new[] {
                ("ARB_REAR", "MIN=0\nMAX=30000\nSTEP=1000\nSHOW_CLICKS=0", 5002d),
                ("TOE_OUT_LF", "MIN=-100\nMAX=100\nSTEP=5\nSHOW_CLICKS=0", -39d) })
            {
                var f = Fixture(root, section, definition, baseline); f.Generate();
                Check(!f.Parameter.Changed && f.Parameter.Reason.StartsWith("Left unchanged"), "Off-grid setup silently normalized in the wrong direction");
                f.Parameter.RecommendedValue = section == "ARB_REAR" ? 6000 : -35;
                f.RejectWrite();
            }
        });
        test("setup legality: coarse adjustment steps report a hold without promising increased stiffness", () =>
        {
            var f = Fixture(root, "ARB_REAR", "MIN=0\nMAX=30000\nSTEP=1000\nSHOW_CLICKS=0", 5000);
            f.Generate();
            Check(!f.Parameter.Changed && f.Parameter.Reason.StartsWith("Left unchanged") && !f.Parameter.Reason.Contains("One-step"), "No-op still promises a mechanical change");
        });
        test("setup legality: setup exposure is enforced without any readable base physics", () =>
        {
            var f = Fixture(root, "DIFF_POWER", "MIN=0\nMAX=100\nSTEP=1\nSHOW_CLICKS=0", 60);
            f.Analysis.Physics = new() { CarId = "physics_car", Available = false,
                HasSetupDefinition = true, AdjustableSections = ["PRESSURE_LF"] };
            f.Generate();
            Check(!f.Parameter.Changed && f.Parameter.BlendStatus == "Not exposed", "Base-fact availability bypassed setup exposure");
            f.Parameter.RecommendedValue = 64;
            f.RejectWrite();
        });
        test("setup legality: unknown serialization and custom lookup controls are held and cannot be exported", () =>
        {
            foreach (var mode in new[] { "SHOW_CLICKS=3", "SHOW_CLICKS=-1", "SHOW_CLICKS=bad", "SHOW_CLICKS=0\nLUT=custom.lut", "SHOW_CLICKS=0\nRATIOS=custom.rto" })
            {
                var f = Fixture(root, "PRESSURE_LF", "MIN=10\nMAX=40\nSTEP=1\n" + mode, 28);
                f.Generate(); Check(!f.Parameter.Changed, "Unknown serialization was tuned");
                f.Parameter.RecommendedValue = 27; f.RejectWrite();
            }
        });
        foreach (bool packed in new[] { false, true })
            test("setup legality: scalar mode round trips, pit plans and displayed units agree: " + (packed ? "packed" : "unpacked"), () =>
            {
                foreach (var section in new[] { "ARB_REAR", "SPRING_RATE_LR", "DAMP_REBOUND_LR", "DAMP_FAST_BUMP_RF", "TOE_OUT_LF", "PRESSURE_LF", "DIFF_POWER", "DIFF_COAST", "FRONT_BIAS" })
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        var min = section == "TOE_OUT_LF" ? -10 : 10;
                        double before = mode == 0 ? 20 : mode == 1 ? 10 : (20 - min) / 2;
                        double after = before + (mode == 0 ? 2 : 1);
                        var f = Fixture(root, section, $"MIN={min}\nMAX=40\nSTEP=2\nSHOW_CLICKS={mode}\nUNITS=control units", before, packed);
                        f.Parameter.RecommendedValue = after;
                        var original = File.ReadAllBytes(f.Baseline);
                        var plan = new PitSetupPlanService().Create(f.Analysis, f.Car.SourceFolderName!, "Scalar test");
                        Check(plan.Changes.Single().Before == before && plan.Changes.Single().After == after, "Pit payload confused saved and displayed values");
                        var output = Path.Combine(f.Root, "valid.ini"); new AssettoCorsaSetupService().WriteGenerated(f.Analysis, output);
                        Check(new AssettoCorsaSetupService().LoadBaseline(output, f.Car).Parameters.Single().CurrentValue == after, "Saved VALUE round trip failed");
                        var row = SetupComparisonPresentation.Proposed(f.Analysis, true).Single();
                        Check(row.Before.StartsWith("20 control units") && row.After.StartsWith("22 control units") && row.Difference == "+2 control units", "Display used clicks as physical units");
                        Check(File.ReadAllBytes(f.Baseline).SequenceEqual(original), "Baseline changed");
                        CarDataSource.EnsureUnchanged(f.Analysis.SourceEvidence!);
                        foreach (var illegal in new[] { mode == 0 ? 41d : mode == 1 ? 21d : (40 - min) / 2d + 1, after + .3 })
                        { f.Parameter.RecommendedValue = illegal; f.RejectWrite(); }
                    }
            });
        test("setup legality: generation has equivalent physical requests in modes 0 1 2", () =>
        {
            foreach (var section in new[] { "PRESSURE_LF", "DIFF_POWER", "TOE_OUT_LF", "ARB_REAR", "SPRING_RATE_LR", "DAMP_REBOUND_LR", "FRONT_BIAS" })
                foreach (int goal in new[] { -2, 0, 2 })
                {
                    double? expected = null;
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        double before = mode == 0 ? 28 : mode == 1 ? 56 : 36;
                        var f = Fixture(root, section, $"MIN=10\nMAX=40\nSTEP=0.5\nSHOW_CLICKS={mode}", before);
                        f.Generate(new() { RearGrip = goal, TransitionSpeed = goal, InitiationSharpness = goal });
                        Check(SetupValueMapping.TryCreate(section, f.Parameter.Range, out var map) && map.IsLegal(f.Parameter.RecommendedValue!.Value), "Generation produced illegal selection");
                        var actual = map.SetupValue(f.Parameter.RecommendedValue!.Value);
                        if (expected.HasValue) Check(Math.Abs(expected.Value - actual) < .000001, "Equivalent setup encodings produced different requests");
                        expected = actual;
                        if (f.Parameter.Changed)
                        {
                            new PitSetupPlanService().Create(f.Analysis, "physics_car", "Generated");
                            new AssettoCorsaSetupService().WriteGenerated(f.Analysis, Path.Combine(f.Root, "generated.ini"));
                            Check(f.Parameter.Reason.Contains("legal step(s)"), "Explanation omits final result");
                        }
                    }
                }
        });
        test("setup legality: negative minima fractional steps and partial last steps remain on the grid", () =>
        {
            foreach (var (definition, before) in new[] { ("MIN=-5\nMAX=7\nSTEP=3", 7d), ("MIN=0\nMAX=7\nSTEP=3", 6d), ("MIN=-5\nMAX=-1\nSTEP=0.25", -2d) })
            {
                var f = Fixture(root, "TOE_OUT_LF", definition + "\nSHOW_CLICKS=0", before); f.Generate();
                Check(SetupValueMapping.TryCreate(f.Parameter.Section, f.Parameter.Range, out var map) && map.IsLegal(f.Parameter.RecommendedValue!.Value), "Clamping invented an illegal end stop");
                Check(f.Parameter.RecommendedValue >= before, "Positive request reversed direction");
                if (!f.Parameter.Changed) Check(f.Parameter.Reason.StartsWith("Left unchanged"), "End stop promises a change");
            }
        });
        test("setup legality: ambiguous global display stays held but explicit local modes override it", () =>
        {
            foreach (var global in new[] { "1", "2", "3", "bad" })
            {
                var header = $"[DISPLAY_METHOD]\nSHOW_CLICKS={global}\n";
                var f = Fixture(root, "PRESSURE_LF", "MIN=10\nMAX=40\nSTEP=2", 28, global: header);
                f.Generate(); Check(!f.Parameter.Changed, "Global-only mode was guessed");
                f.Parameter.RecommendedValue = 26; f.RejectWrite();
                var local = Fixture(root, "PRESSURE_LF", "MIN=10\nMAX=40\nSTEP=2\nSHOW_CLICKS=0", 28, global: header);
                local.Parameter.RecommendedValue = 26; new PitSetupPlanService().Create(local.Analysis, "physics_car", "Explicit override");
            }
        });
        test("setup legality: invalid ranges and missing definitions never fall back to raw increments", () =>
        {
            foreach (var definition in new[] { "", "MIN=10\nMAX=40", "MIN=40\nMAX=10\nSTEP=1", "MIN=10\nMAX=40\nSTEP=0", "MIN=NaN\nMAX=40\nSTEP=1", "MIN=10\nMAX=Infinity\nSTEP=1", "MIN=5\nMAX=40\nSTEP=2\nSHOW_CLICKS=1" })
            {
                var f = Fixture(root, "PRESSURE_LF", definition, 28); f.Generate(); Check(!f.Parameter.Changed, "Invalid metadata was ignored");
                f.Parameter.RecommendedValue = 27; f.RejectWrite();
            }
        });
        test("setup legality: precise representation and changed definitions are checked again at export", () =>
        {
            var f = Fixture(root, "TOE_OUT_LF", "MIN=-10\nMAX=10\nSTEP=0.00001\nSHOW_CLICKS=0", 0);
            f.Parameter.RecommendedValue = .12345; f.RejectWrite();
            f = Fixture(root, "PRESSURE_LF", "MIN=10\nMAX=40\nSTEP=1\nSHOW_CLICKS=0", 28); f.Parameter.RecommendedValue = 27;
            f.Parameter.Range!.Section = "PRESSURE_RF"; f.RejectWrite();
            f.Parameter.Range.Section = "PRESSURE_LF";
            File.AppendAllText(Path.Combine(f.Car.SourceFolderPath!, "data", "setup.ini"), "\n; changed\n");
            f.RejectWrite();
        });
        test("setup legality: verified low differential percentages do not infer click encoding from magnitude", () =>
        {
            foreach (double before in new[] { 0, 1, 9, 10 })
            {
                var f = Fixture(root, "DIFF_POWER", "MIN=0\nMAX=100\nSTEP=1\nSHOW_CLICKS=0", before); f.Generate();
                Check(f.Parameter.RecommendedValue == before + 4, "Value magnitude changed a verified percentage request");
            }
        });
    }

    internal static FixtureData Fixture(string root, string section, string definition, double before, bool packed = false, string global = "")
    {
        var dir = Path.Combine(root, "legality-" + Guid.NewGuid().ToString("N"));
        var carDir = Path.Combine(dir, "physics_car"); Directory.CreateDirectory(carDir);
        var files = new Dictionary<string, string> { ["setup.ini"] = global + $"[{section}]\n{definition}\n" };
        if (packed) PackedArchiveChecks.WriteArchive(carDir, files);
        else { Directory.CreateDirectory(Path.Combine(carDir, "data")); File.WriteAllText(Path.Combine(carDir, "data", "setup.ini"), files["setup.ini"]); }
        var path = Path.Combine(dir, "baseline.ini");
        File.WriteAllText(path, $"[CAR]\nMODEL=physics_car\n[{section}]\nVALUE={before.ToString("R", CultureInfo.InvariantCulture)}\n");
        var car = new CarProfile { Id = "physics_car", SourceFolderName = "physics_car", SourceFolderPath = carDir };
        return new(dir, path, car, new AssettoCorsaSetupService().LoadBaseline(path, car));
    }
    internal sealed record FixtureData(string Root, string Baseline, CarProfile Car, CarSetupAnalysis Analysis)
    {
        internal CarSetupParameter Parameter => Analysis.Parameters.Single();
        internal void Generate(CarBehaviorTarget? goal = null)
        {
            var input = new TuneInput { Car = Car, Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0],
                DriftPack = BuiltInProfiles.DriftPacks()[0], Intent = BuiltInProfiles.Intents()[1] };
            input.Intent.Kind = DriftStyleKind.FastSelfSteer;
            new CarSetupTuningEngine().Generate(input, Analysis, SetupAggressiveness.Balanced, goal);
        }
        internal void RejectWrite()
        {
            var original = File.ReadAllBytes(Baseline); var output = Path.Combine(Root, "refused.ini");
            Reject(() => new AssettoCorsaSetupService().WriteGenerated(Analysis, output));
            Reject(() => new PitSetupPlanService().Create(Analysis, Car.SourceFolderName!, "Legality check"));
            Check(!File.Exists(output) && original.SequenceEqual(File.ReadAllBytes(Baseline)), "Rejected proposal modified a setup");
        }
    }
}
