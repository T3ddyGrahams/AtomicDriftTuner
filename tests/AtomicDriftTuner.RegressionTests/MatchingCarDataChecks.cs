using System.IO;
using System.Security.Cryptography;
using System.Text;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class MatchingCarDataChecks
{
    internal static void Run(Action<string, Action> test, string root)
    {
        test("dual sources accept only verified seat-camera value differences without editing either copy", () =>
        {
            foreach (var field in new[] { "eyes", "pitch", "both" })
            {
                var f = CameraFixture(root); var text = f.Files["car.ini"];
                if (field is "eyes" or "both") text = text.Replace("-0.36,0.93,-0.60", "-0.36,0.87,-0.17");
                if (field is "pitch" or "both") text = text.Replace("0.343915", "2.472130");
                File.WriteAllText(Path.Combine(f.Data, "car.ini"), text);
                var before = HashFiles(f.Car.SourceFolderPath!); var source = CarDataSource.Open(f.Car);
                Check(source.Evidence.CameraOnlyDifferences && source.Evidence.MatchingUnpackedCopy && source.Kind == "packed", "Camera difference not explained");
                Check(source.ReadText("car.ini") == f.Files["car.ini"] && source.Evidence.Fingerprints.Count == f.Files.Count + 1, "Sources mixed or fingerprint omitted");
                Check(new CarPhysicsService().Read(f.Car).Notes.Any(n => n.Contains("driver-eye")), "Physics review incorrectly claims exact byte match");
                CarDataSource.EnsureUnchanged(source.Evidence);
                Check(before.OrderBy(x => x.Key).SequenceEqual(HashFiles(f.Car.SourceFolderPath!).OrderBy(x => x.Key)), "Source was modified");
            }
        });
        test("camera exception retains mass inertia steering fuel and other-file conflict gates", () =>
        {
            foreach (var change in new[] { ("TOTALMASS=1200", "TOTALMASS=1201"), ("INERTIA=1,2,3", "INERTIA=1,2,4"),
                ("STEER_RATIO=9", "STEER_RATIO=10"), ("FFMULT=1.95", "FFMULT=2"), ("MAX_FUEL=55", "MAX_FUEL=60"),
                ("ONBOARD_EXPOSURE=20", "ONBOARD_EXPOSURE=21"), ("; camera", "; different comment") })
            {
                var f = CameraFixture(root);
                File.WriteAllText(Path.Combine(f.Data, "car.ini"), f.Files["car.ini"].Replace("0.343915", "2.472130").Replace(change.Item1, change.Item2));
                Refuse(() => CarDataSource.Open(f.Car));
            }
            var other = CameraFixture(root);
            File.WriteAllText(Path.Combine(other.Data, "car.ini"), other.Files["car.ini"].Replace("0.343915", "2.472130"));
            File.AppendAllText(Path.Combine(other.Data, "power.lut"), "9000|350\n");
            Check(Refuse(() => CarDataSource.Open(other.Car)).Contains("power.lut"), "Camera difference masked another file conflict");
        });
        test("camera exception rejects malformed ambiguous nonfinite or misplaced camera fields", () =>
        {
            foreach (var transform in new Func<string, string>[] {
                s => s.Replace("[GRAPHICS]", "[OTHER]"), s => s.Replace("[GRAPHICS]", "[GRAPHICS]garbage"),
                s => s + "[GRAPHICS]\nDRIVEREYES=1,2,3\n", s => s.Replace("ONBOARD_EXPOSURE=20", "DRIVEREYES=1,2,3"),
                s => s.Replace("0.343915", "NaN"), s => s.Replace("0.343915", "Infinity"), s => s.Replace("-0.36,0.93,-0.60", "1,2"),
                s => s.Replace("ON_BOARD_PITCH_ANGLE", "BONNET_CAMERA_PITCH"), s => s.Replace("[CONTROLS]", "[CONTROLS\n"),
                s => s + "\0" })
            {
                var f = CameraFixture(root); f.Files["car.ini"] = transform(f.Files["car.ini"]);
                PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, f.Files);
                File.WriteAllText(Path.Combine(f.Data, "car.ini"), f.Files["car.ini"].Replace("-0.36,0.93,-0.60", "-0.36,0.87,-0.17").Replace("0.343915", "2.472130"));
                Refuse(() => CarDataSource.Open(f.Car));
            }
        });
        test("camera changes still invalidate old fingerprints and cached export plans", () =>
        {
            var f = CameraFixture(root); var original = CarDataSource.Open(f.Car);
            File.WriteAllText(Path.Combine(f.Data, "car.ini"), f.Files["car.ini"].Replace("0.343915", "2.472130"));
            var modified = CarDataSource.Open(f.Car);
            Check(modified.Evidence.CameraOnlyDifferences && modified.Evidence.Fingerprint != original.Evidence.Fingerprint, "Camera edit erased raw fingerprint change");
            Refuse(() => CarDataSource.EnsureUnchanged(original.Evidence));
            CarDataSource.EnsureUnchanged(modified.Evidence);
        });
        test("identical packed and unpacked physics use one snapshot and fingerprint both", () =>
        {
            var f = Fixture(root);
            var before = HashFiles(f.Car.SourceFolderPath!);
            var source = CarDataSource.Open(f.Car);
            Check(source.Kind == "packed" && source.Evidence.MatchingUnpackedCopy, "Matching provenance missing");
            Check(source.FileNames.Count() == f.Files.Count && source.Evidence.Fingerprints.Count == f.Files.Count + 1,
                "Sources were combined or one source was not fingerprinted");
            Check(source.Evidence.Fingerprints.ContainsKey("data.acd") && source.Evidence.Fingerprints.ContainsKey("data/final.rto"),
                "Both source fingerprints missing");
            foreach (var file in f.Files) Check(source.ReadText(file.Key) == file.Value, "Packed snapshot content changed");
            var physics = new CarPhysicsService().Read(f.Car);
            Check(physics.Available && physics.Notes.Any(n => n.Contains("match exactly")), "Matching-copy explanation missing from physics review");
            CarDataSource.EnsureUnchanged(source.Evidence);
            Check(before.OrderBy(x => x.Key).SequenceEqual(HashFiles(f.Car.SourceFolderPath!).OrderBy(x => x.Key)), "Reading modified car files");
        });
        test("single source fingerprints retain the existing format", () =>
        {
            var f = Fixture(root);
            var packedHash = Hash(Path.Combine(f.Car.SourceFolderPath!, "data.acd"));
            Directory.Move(f.Data, f.Data + "-saved");
            var packed = CarDataSource.Open(f.Car);
            Check(packed.Evidence.Fingerprint == Digest("adt/car-data/1\npacked\ndata.acd=" + packedHash) && !packed.Evidence.MatchingUnpackedCopy,
                "Unchanged packed-only cars received a new fingerprint");
            Directory.Move(f.Data + "-saved", f.Data);
            File.Delete(Path.Combine(f.Car.SourceFolderPath!, "data.acd"));
            var unpacked = CarDataSource.Open(f.Car);
            var rows = f.Files.Keys.OrderBy(n => n, StringComparer.Ordinal).Select(n => n + "=" + Hash(Path.Combine(f.Data, n)));
            Check(unpacked.Evidence.Fingerprint == Digest("adt/car-data/1\nunpacked\n" + string.Join("\n", rows)) && !unpacked.Evidence.MatchingUnpackedCopy,
                "Unchanged unpacked-only cars received a new fingerprint");
        });
        test("dual source verification rejects different bytes in every supported extension", () =>
        {
            foreach (var name in new[] { "car.ini", "power.lut", "final.rto", "script.lua" })
            {
                var f = Fixture(root);
                File.AppendAllText(Path.Combine(f.Data, name), "\n"); // Even formatting changes require verification.
                var error = Refuse(() => CarDataSource.Open(f.Car));
                Check(error.Contains(name) && error.Contains("differ"), "Conflict did not identify the differing file");
            }
        });
        test("dual source verification rejects missing or extra files without merging sources", () =>
        {
            var missing = Fixture(root);
            File.Delete(Path.Combine(missing.Data, "power.lut"));
            Check(Refuse(() => CarDataSource.Open(missing.Car)).Contains("power.lut"), "Missing file was filled from archive");
            var extra = Fixture(root);
            File.WriteAllText(Path.Combine(extra.Data, "extra.ini"), "[X]\nVALUE=1\n");
            Check(Refuse(() => CarDataSource.Open(extra.Car)).Contains("extra.ini"), "Extra unpacked physics was ignored");
        });
        test("packed cache cannot bypass rechecking the unpacked copy", () =>
        {
            var f = Fixture(root);
            var source = CarDataSource.Open(f.Car);
            File.AppendAllText(Path.Combine(f.Data, "car.ini"), "; changed\n");
            Refuse(() => CarDataSource.EnsureUnchanged(source.Evidence));
            Refuse(() => CarDataSource.Open(f.Car));
            File.WriteAllText(Path.Combine(f.Data, "car.ini"), f.Files["car.ini"]);
            Check(CarDataSource.Open(f.Car).Evidence.Fingerprint == source.Evidence.Fingerprint, "Restored matching bytes kept stale cache state");
        });
        test("changes to either or both matching sources invalidate prior evidence", () =>
        {
            foreach (var change in new[] { "archive", "both", "remove-archive", "remove-data" })
            {
                var f = Fixture(root); var source = CarDataSource.Open(f.Car);
                if (change == "remove-archive") File.Delete(Path.Combine(f.Car.SourceFolderPath!, "data.acd"));
                else if (change == "remove-data") Directory.Move(f.Data, f.Data + "-saved");
                else
                {
                    f.Files["car.ini"] = "[BASIC]\nTOTALMASS=1300\n";
                    PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, f.Files);
                    if (change == "both") File.WriteAllText(Path.Combine(f.Data, "car.ini"), f.Files["car.ini"]);
                }
                Refuse(() => CarDataSource.EnsureUnchanged(source.Evidence));
            }
        });
        test("dual sources retain archive validation and unpacked resource limits", () =>
        {
            var broken = Fixture(root);
            File.WriteAllBytes(Path.Combine(broken.Car.SourceFolderPath!, "data.acd"), [1, 2, 3]);
            Refuse(() => CarDataSource.Open(broken.Car));
            var tooLarge = Fixture(root);
            File.WriteAllBytes(Path.Combine(tooLarge.Data, "power.lut"), new byte[1024 * 1024 + 1]);
            Check(Refuse(() => CarDataSource.Open(tooLarge.Car)).Contains("limit"), "Oversized unpacked source bypassed bounds");
        });
    }

    private static (CarProfile Car, string Data, Dictionary<string, string> Files) Fixture(string root)
    {
        var carRoot = Path.Combine(root, "matching-" + Guid.NewGuid().ToString("N"), "physics_car");
        var data = Path.Combine(carRoot, "data"); Directory.CreateDirectory(data);
        var files = new Dictionary<string, string> { ["car.ini"] = "[BASIC]\nTOTALMASS=1200\n",
            ["power.lut"] = "0|100\n8000|300\n", ["final.rto"] = "Short|4.3\nLong|3.7\n", ["script.lua"] = "-- never executed\n" };
        foreach (var file in files) File.WriteAllText(Path.Combine(data, file.Key), file.Value);
        PackedArchiveChecks.WriteArchive(carRoot, files);
        return (new() { SourceFolderName = "physics_car", SourceFolderPath = carRoot }, data, files);
    }
    private static (CarProfile Car, string Data, Dictionary<string, string> Files) CameraFixture(string root)
    {
        var f = Fixture(root);
        f.Files["car.ini"] = "[BASIC]\nTOTALMASS=1200\nINERTIA=1,2,3\n[GRAPHICS]\nDRIVEREYES=-0.36,0.93,-0.60 ; camera\nON_BOARD_PITCH_ANGLE=0.343915\nONBOARD_EXPOSURE=20\n[CONTROLS]\nSTEER_RATIO=9\nFFMULT=1.95\n[FUEL]\nMAX_FUEL=55\n";
        File.WriteAllText(Path.Combine(f.Data, "car.ini"), f.Files["car.ini"]);
        PackedArchiveChecks.WriteArchive(f.Car.SourceFolderPath!, f.Files);
        return f;
    }
    private static Dictionary<string, string> HashFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static string Refuse(Action action)
    {
        try { action(); } catch (InvalidDataException ex) { return ex.Message; }
        throw new Exception("Ambiguous or changed car data was accepted");
    }
}
