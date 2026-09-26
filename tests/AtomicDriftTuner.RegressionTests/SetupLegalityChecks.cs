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
            f.Analysis.Physics = new() { CarId = "scalar_car", Available = false,
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
    }

    internal static FixtureData Fixture(string root, string section, string definition, double before, bool packed = false, string global = "")
    {
        var dir = Path.Combine(root, "legality-" + Guid.NewGuid().ToString("N"));
        var carDir = Path.Combine(dir, "scalar_car"); Directory.CreateDirectory(carDir);
        var files = new Dictionary<string, string> { ["setup.ini"] = global + $"[{section}]\n{definition}\n" };
        if (packed) PackedArchiveChecks.WriteArchive(carDir, files);
        else { Directory.CreateDirectory(Path.Combine(carDir, "data")); File.WriteAllText(Path.Combine(carDir, "data", "setup.ini"), files["setup.ini"]); }
        var path = Path.Combine(dir, "baseline.ini");
        File.WriteAllText(path, $"[CAR]\nMODEL=scalar_car\n[{section}]\nVALUE={before.ToString("R", CultureInfo.InvariantCulture)}\n");
        var car = new CarProfile { Id = "scalar_car", SourceFolderName = "scalar_car", SourceFolderPath = carDir };
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
