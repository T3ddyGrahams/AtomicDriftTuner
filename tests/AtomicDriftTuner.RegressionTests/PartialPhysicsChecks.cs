using System.IO;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PartialPhysicsChecks
{
    internal static void Run(Action<string, Action> test, string root)
    {
        foreach (var kind in new[] { "unpacked", "packed", "matching" })
            test("readable suspension sections survive auxiliary malformed text: " + kind, () =>
            {
                var f = Fixture(root);
                var path = Path.Combine(f.Data, "suspensions.ini");
                File.AppendAllText(path, "\n[DAMAGE]\nMIN_VELOCITY=40; damage threshold\nMINIMUM VELOCITY TO START TAKING DAMAGE\nGAIN=0.0004\nAMOUNT OF STEER ROD DEFLECTION\n");
                if (kind != "unpacked")
                {
                    PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, Directory.GetFiles(f.Data).ToDictionary(p => Path.GetFileName(p), File.ReadAllText));
                    if (kind == "packed") Directory.Move(f.Data, f.Data + "-backup");
                }
                var source = CarDataSource.Open(f.Car);
                var physics = new CarPhysicsService().Read(f.Car, suppliedSource: source);
                Check(physics.Find("suspensions.ini", "FRONT", "SPRING_RATE")?.Value == "80000" &&
                    physics.Find("suspensions.ini", "REAR", "SPRING_RATE")?.Value == "60000" &&
                    physics.Find("suspensions.ini", "BASIC", "WHEELBASE")?.Value == "2.6", "Auxiliary text erased readable suspension context");
                Check(physics.Notes.Any(n => n.Contains("[DAMAGE]") && n.Contains("not imported")) &&
                    !physics.Notes.Any(n => n.Contains("unavailable or malformed")), "Partial import remained an unexplained whole-file failure");
                CarDataSource.EnsureUnchanged(source.Evidence);
            });
        test("ambiguous suspension fields invalidate their whole section but preserve other axles", () =>
        {
            foreach (var problem in new[] { "spring_rate=90000", "unrecognized line", "=80000", "[front]\nSPRING_RATE=90000" })
            {
                var f = Fixture(root);
                File.WriteAllText(Path.Combine(f.Data, "suspensions.ini"), "[FRONT]\nSPRING_RATE=80000\n" + problem + "\n[REAR]\nSPRING_RATE=60000\n");
                var physics = new CarPhysicsService().Read(f.Car);
                Check(!physics.Facts.Any(x => x.File == "suspensions.ini" && x.Section == "FRONT"), "Ambiguous front value was imported");
                Check(physics.Find("suspensions.ini", "REAR", "SPRING_RATE")?.Value == "60000", "Independent rear section was discarded");
            }
        });
        test("malformed headers cannot leak following fields into a previous axle", () =>
        {
            foreach (var header in new[] { "[REAR", "[]", "[ ]", "[REAR] junk", "[[REAR]]" })
            {
                var f = Fixture(root);
                File.WriteAllText(Path.Combine(f.Data, "suspensions.ini"), "[FRONT]\nSPRING_RATE=80000\n" + header + "\nDAMP_BUMP=5000\n[BASIC]\nWHEELBASE=2.6\n");
                var physics = new CarPhysicsService().Read(f.Car);
                Check(!physics.Facts.Any(x => x.File == "suspensions.ini" && (x.Section == "FRONT" || x.Section == "REAR")), "Uncertain section boundary produced axle values");
                Check(physics.Find("suspensions.ini", "BASIC", "WHEELBASE")?.Value == "2.6", "Later valid section did not recover");
                Check(physics.Notes.Any(n => n.Contains("outside a valid section")), "Unassigned data was not explained");
            }
        });
        test("global car data is never assigned to the next physics section", () =>
        {
            var f = Fixture(root);
            File.WriteAllText(Path.Combine(f.Data, "suspensions.ini"), "SPRING_RATE=99999\nVALUE=20\n[FRONT]\nDAMP_BUMP=4000\n[REAR]\nSPRING_RATE=60000\n");
            var physics = new CarPhysicsService().Read(f.Car);
            Check(physics.Find("suspensions.ini", "FRONT", "SPRING_RATE") is null && physics.Find("suspensions.ini", "FRONT", "DAMP_BUMP")?.Value == "4000",
                "Global value inherited the next section or erased valid data");
        });
        test("valid normalized differential lock retains its percent conversion", () =>
        {
            foreach (var values in new[] { ("0", "1", "0", "100"), ("0.85", "0.65", "85", "65") })
            {
                var f = Fixture(root);
                File.WriteAllText(Path.Combine(f.Data, "drivetrain.ini"), $"[DIFFERENTIAL]\nPOWER={values.Item1}\nCOAST={values.Item2}\nPRELOAD=55\n");
                var physics = new CarPhysicsService().Read(f.Car);
                Check(physics.Find("drivetrain.ini", "DIFFERENTIAL", "POWER") is { Unit: "%" } power && power.Value == values.Item3 &&
                    physics.Find("drivetrain.ini", "DIFFERENTIAL", "COAST")?.Value == values.Item4, "Valid base lock scale changed");
                Check(!physics.Notes.Any(n => n.Contains("Effective lock remains unknown")), "Valid lock was marked unknown");
            }
        });
        test("nonstandard differential values are explained without guessing percentages", () =>
        {
            foreach (var raw in new[] { "85", "65", "1.01", "-0.1", "NaN", "Infinity", "unknown" })
            {
                var f = Fixture(root);
                File.WriteAllText(Path.Combine(f.Data, "drivetrain.ini"), $"[DIFFERENTIAL]\nPOWER={raw}\nCOAST={raw}\nPRELOAD=55\n");
                var physics = new CarPhysicsService().Read(f.Car);
                Check(physics.Find("drivetrain.ini", "DIFFERENTIAL", "POWER") is null && physics.Find("drivetrain.ini", "DIFFERENTIAL", "COAST") is null,
                    "Unverified raw lock was converted, clamped or imported as a verified fact");
                Check(physics.Find("drivetrain.ini", "DIFFERENTIAL", "PRELOAD")?.Value == "55", "Unsupported lock erased valid preload");
                Check(physics.Notes.Count(n => n.Contains("Effective lock remains unknown")) == 2, "Differential uncertainty was not explained");
                if (raw is "85" or "65") Check(physics.Notes.Any(n => n.Contains("raw file value " + raw) && n.Contains("0–1")), "Raw value or expected scale missing");
            }
        });
        test("partial physics parsing retains section and field resource limits", () =>
        {
            foreach (var text in new[] {
                string.Join("\n", Enumerable.Range(0, 2049).Select(i => $"[SECTION_{i}]\nX=1")),
                "[FRONT]\n" + string.Join("\n", Enumerable.Range(0, 2049).Select(i => $"KEY_{i}=1")) })
            {
                var f = Fixture(root); File.WriteAllText(Path.Combine(f.Data, "suspensions.ini"), text);
                var physics = new CarPhysicsService().Read(f.Car);
                Check(!physics.Facts.Any(x => x.File == "suspensions.ini") && physics.Notes.Any(n => n.Contains("suspensions.ini: unavailable or malformed")),
                    "Parser resource limit was bypassed");
            }
        });
    }
    private static (CarProfile Car, string Data) Fixture(string root)
    {
        var fixture = CarPhysicsChecks.Fixture(Path.Combine(root, "partial-physics-" + Guid.NewGuid().ToString("N")));
        return (fixture.Car, Path.Combine(fixture.Car.SourceFolderPath!, "data"));
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
