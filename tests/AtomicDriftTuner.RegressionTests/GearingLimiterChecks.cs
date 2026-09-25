using System.IO;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class GearingLimiterChecks
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid limiter mapping accepted."); }
    static GearingTarget Target() => new() { Gear = 2, MinimumSpeedKmh = 45, MaximumSpeedKmh = 65, MinimumRpm = 3100, MaximumRpm = 5900, Sweeper = new(3, 65, 100) };
    static CornerGearingChecks.FixtureData Fixture(string root, bool packed = false, int mode = 1, string raw = "90", string step = "1")
    {
        var f = CornerGearingChecks.Fixture(root, packed);
        f.Entries["setup.ini"] += $"[ENGINE_LIMITER]\nMIN=90\nMAX=100\nSTEP={step}\nSHOW_CLICKS={mode}\n";
        File.AppendAllText(f.Baseline, $"[ENGINE_LIMITER]\nVALUE={raw}\n"); f.Write(); return f;
    }
    public static void Run(Action<string, Action> test, string root)
    {
        foreach (var packed in new[] { false, true })
        {
            test("gearing decodes all standard limiter display modes from " + (packed ? "packed" : "unpacked") + " data", () => {
                foreach (var (mode, raw) in new[] { (0, "94"), (1, "47"), (2, "2") }) {
                    var f = Fixture(root, packed, mode, raw, "2"); var d = f.Service.Load(f.Car, f.Baseline, 2);
                    Check(Math.Abs(d.LimiterRpm - 7520) < .00001 && d.LimiterSource.Contains("94%") && d.LimiterSource.Contains("not a live"), "Wrong percentage/click mapping or active-readback claim.");
                }
                var edge = Fixture(root, packed, raw: "100"); Check(edge.Service.Load(edge.Car, edge.Baseline, 2).LimiterRpm == 8000, "100% changed the base limiter.");
            });
            test("resolved limiter bounds RPM guidance and candidate ranking in " + (packed ? "packed" : "unpacked") + " data", () => {
                var f = Fixture(root, packed); var d = f.Service.Load(f.Car, f.Baseline, 2);
                Check(d.LimiterRpm == 7200 && d.RpmEstimate.Available && d.RpmEstimate.MaximumRpm <= 7200 * .95, "RPM estimate exceeded the selected limit.");
                Reject(() => f.Service.Calculate(f.Car, f.Baseline, Target() with { MaximumRpm = 7500 }));
                var plan = f.Service.Calculate(f.Car, f.Baseline, Target() with { MaximumSpeedKmh = 90, MaximumRpm = 7000, Sweeper = null });
                var shortRatio = plan.Options.Single(x => x.FinalDrive.Index == 0);
                Check(!shortRatio.BelowLimiter && shortRatio.HighRpm < 8000 && shortRatio.HighRpm > 7200, "Ranking used the unadjusted base limiter.");
                Check(plan.Recommended.BelowLimiter && plan.Recommended.FinalDrive.Index != 0, "Suggested an excluded final drive.");
            });
            test("gearing export preserves adjustable limiter and rejects stale baseline/physics in " + (packed ? "packed" : "unpacked") + " data", () => {
                var f = Fixture(root, packed); var before = File.ReadAllText(f.Baseline); var evidence = CarDataSource.Open(f.Car).Evidence;
                var plan = f.Service.Calculate(f.Car, f.Baseline, Target()); Check(plan.HasChange, "Fixture needs a real final-drive change.");
                var output = Path.Combine(f.Root, "new.ini"); f.Service.Save(plan, output);
                Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == before.Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2").TrimEnd(), "Export changed the limiter or another setting.");
                Check(File.ReadAllText(f.Baseline) == before, "Export edited the baseline."); CarDataSource.EnsureUnchanged(evidence);
                File.WriteAllText(f.Baseline, before.Replace("[ENGINE_LIMITER]\nVALUE=90", "[ENGINE_LIMITER]\nVALUE=100"));
                var stale = Path.Combine(f.Root, "stale.ini"); Reject(() => f.Service.Save(plan, stale)); Check(!File.Exists(stale), "Stale export created a file.");
                File.WriteAllText(f.Baseline, before); f.Entries["engine.ini"] = f.Entries["engine.ini"].Replace("LIMITER=8000", "LIMITER=8500"); f.Write();
                Reject(() => f.Service.Save(plan, stale)); Check(!File.Exists(stale), "Changed engine limit escaped export check.");
            });
        }
        test("gearing limiter rejects missing ambiguous malformed or unsupported mappings", () => {
            foreach (var raw in new[] { "NaN", "Infinity", "-1", "89", "101", "90.5" }) { var f = Fixture(root, raw: raw); Reject(() => f.Service.Load(f.Car, f.Baseline, 2)); }
            foreach (var change in new Func<string, string>[] {
                s => s.Replace("STEP=1", "STEP=0"), s => s.Replace("STEP=1", "STEP=-1"), s => s.Replace("STEP=1", ""),
                s => s.Replace("MIN=90", "MIN=10000"), s => s.Replace("MAX=100", "MAX=89"), s => s.Replace("SHOW_CLICKS=1", "SHOW_CLICKS=3"),
                s => s + "[ENGINE_LIMITER]\nMIN=90\nMAX=100\nSTEP=1\n", s => s + "STEP=2\n", s => s + "RATIOS=unknown.rto\n",
                s => s + "LUT=unknown.lut\n", s => s + "[LIMITER]\nMIN=90\nMAX=100\nSTEP=1\n", s => "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n" }) {
                var f = Fixture(root); f.Entries["setup.ini"] = change(f.Entries["setup.ini"]); f.Write(); Reject(() => f.Service.Load(f.Car, f.Baseline, 2));
            }
            var offGrid = Fixture(root, mode: 0, raw: "95", step: "2"); Reject(() => offGrid.Service.Load(offGrid.Car, offGrid.Baseline, 2));
            var missing = Fixture(root); File.WriteAllText(missing.Baseline, File.ReadAllText(missing.Baseline).Replace("[ENGINE_LIMITER]\nVALUE=90\n", "")); Reject(() => missing.Service.Load(missing.Car, missing.Baseline, 2));
            var duplicate = Fixture(root); File.AppendAllText(duplicate.Baseline, "[ENGINE_LIMITER]\nVALUE=90\n"); Reject(() => duplicate.Service.Load(duplicate.Car, duplicate.Baseline, 2));
        });
        test("gearing limiter cannot bypass changed automatic RPM evidence", () => {
            var f = Fixture(root, raw: "100"); var d = f.Service.Load(f.Car, f.Baseline, 2);
            var target = Target() with { MinimumRpm = d.RpmEstimate.MinimumRpm!.Value, MaximumRpm = d.RpmEstimate.MaximumRpm!.Value,
                RpmSourceFingerprint = d.CarDataEvidence!.Fingerprint, RpmSource = "Base engine curve estimate" };
            File.WriteAllText(f.Baseline, File.ReadAllText(f.Baseline).Replace("VALUE=100", "VALUE=90"));
            Reject(() => f.Service.Calculate(f.Car, f.Baseline, target));
        });
    }
}
