using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class CornerGearingChecks
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Near(double a, double b, double tolerance = .001) => Check(Math.Abs(a - b) <= tolerance, $"Expected {b}, got {a}");
    static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException) { return; } throw new Exception("Invalid gearing action accepted"); }
    static GearingTarget Target() => new() { Gear = 2, MinimumSpeedKmh = 45, MaximumSpeedKmh = 65, MinimumRpm = 3100, MaximumRpm = 5900, Sweeper = new(3, 65, 100) };
    internal static void Run(Action<string, Action> test, string root)
    {
        foreach (bool packed in new[] { false, true }) test("corner gearing compares both goals and exports only the final drive: " + (packed ? "packed" : "unpacked"), () =>
        {
            var f = Fixture(root, packed); var source = CarDataSource.Open(f.Car); var original = File.ReadAllBytes(f.Baseline);
            var plan = f.Service.Calculate(f.Car, f.Baseline, Target());
            Check(plan.Recommended.FinalDrive.Index == 2 && plan.Recommended.FitsTarget && plan.Recommended.Goals.Count == 2, "Wrong joint fit or lost goal");
            Near(plan.Recommended.Goals[0].LowRpm, 3342.253805); Near(plan.Recommended.Goals[1].HighRpm, 5305.16477);
            Check(plan.Data.GearboxKind == "Fixed individual gears" && plan.Data.FinalDrives.Select(r => r.Ratio).SequenceEqual(new[] { 4.5, 3, 4 }), "Preset order or gearbox detection lost");
            var output = Path.Combine(f.Root, "joint.ini"); f.Service.Save(plan, output);
            var expected = File.ReadAllText(f.Baseline).Replace("[FINAL_RATIO]\nVALUE=1", "[FINAL_RATIO]\nVALUE=2");
            Check(File.ReadAllText(output).Replace("\r\n", "\n").TrimEnd() == expected.TrimEnd(), "Export changed individual gears or handling");
            Check(original.SequenceEqual(File.ReadAllBytes(f.Baseline)), "Baseline modified"); CarDataSource.EnsureUnchanged(source.Evidence);
        });
        test("corner gearing rejects a preset reaching the limiter in either section and reports compromises", () =>
        {
            var f = Fixture(root); var t = Target() with { Sweeper = new(3, 65, 135) };
            var p = f.Service.Calculate(f.Car, f.Baseline, t);
            Check(!p.Options[0].Goals[1].BelowLimiter && p.Options[0].Goals[0].BelowLimiter && p.Recommended.FinalDrive.Index != 0 && !p.Recommended.FitsTarget, "Sweeper limiter breach ignored or impossible full fit claimed");
            Reject(() => f.Service.Calculate(f.Car, f.Baseline, Target() with { Sweeper = new(3, 190, 300) }));
            Reject(() => f.Service.Calculate(f.Car, f.Baseline, Target() with { Sweeper = new(9, 65, 100) }));
            Reject(() => f.Service.Calculate(f.Car, f.Baseline, Target() with { Sweeper = new(3, 100, 65) }));
            Reject(() => GearingPlanner.Plan(f.Service.Load(f.Car, f.Baseline, 2), Target()));
        });
        test("corner gearing rejects mixed baselines and stale second-gear evidence", () =>
        {
            var f = Fixture(root); var first = f.Service.Load(f.Car, f.Baseline, 2); var other = Fixture(root);
            Reject(() => GearingPlanner.Plan(first, Target(), other.Service.Load(other.Car, other.Baseline, 3)));
            var plan = f.Service.Calculate(f.Car, f.Baseline, Target()); f.Entries["drivetrain.ini"] = f.Entries["drivetrain.ini"].Replace("GEAR_3=1.5", "GEAR_3=1.7"); f.Write();
            var output = Path.Combine(f.Root, "stale.ini"); Reject(() => f.Service.Save(plan, output)); Check(!File.Exists(output), "Stale two-goal export created");
        });
        test("corner gearing detects adjustable gears gearsets and fixed final drive", () =>
        {
            var f = Fixture(root); f.Entries["setup.ini"] += "[GEAR_2]\nRATIOS=gear.rto\n"; f.Entries["gear.rto"] = "Selected|2.1\nOther|2.8\n";
            File.AppendAllText(f.Baseline, "[INTERNAL_GEAR_3]\nVALUE=0\n"); f.Write();
            var plan = f.Service.Calculate(f.Car, f.Baseline, Target()); Check(plan.Data.SelectedGearChoices == 2 && plan.Data.GearboxKind.Contains("adjustable") && plan.Data.GearRatio == 2.1, "Selected individual gear or capability lost");
            var output = Path.Combine(f.Root, "individual.ini"); f.Service.Save(plan, output); Check(File.ReadAllText(output).Contains("[INTERNAL_GEAR_3]"), "Individual selection removed");
            f = Fixture(root); f.Entries["setup.ini"] += "[GEARS]\nUSE_GEARSET=1\n[GEAR_SET_0]\nNAME=Test set\nGEAR_2=2.1\nGEAR_3=1.5\n"; File.AppendAllText(f.Baseline, "[GEARSET]\nVALUE=0\n"); f.Write();
            plan = f.Service.Calculate(f.Car, f.Baseline, Target()); Check(plan.Data.GearboxKind.Contains("gearboxes") && plan.SweeperData!.GearRatio == 1.5, "Gearset not used for both goals");
            f = Fixture(root); f.Entries["setup.ini"] = "[OTHER]\nMIN=0\n"; File.WriteAllText(f.Baseline, File.ReadAllText(f.Baseline).Replace("[FINAL_RATIO]\nVALUE=1\n", "")); f.Write();
            plan = f.Service.Calculate(f.Car, f.Baseline, Target()); Check(!plan.Data.FinalDriveAdjustable && !plan.HasChange && plan.Options.Count == 1, "Fixed final drive invented alternatives"); Reject(() => f.Service.Save(plan, Path.Combine(f.Root, "fixed.ini")));
        });
        test("corner gearing preserves duplicate actual ratios without inventing a change", () =>
        {
            var f = Fixture(root); f.Entries["final.rto"] = "Choice A|4\nChoice B|4\n"; f.Write();
            var p = f.Service.Calculate(f.Car, f.Baseline, Target()); Check(!p.HasChange && p.Recommended.FinalDrive.Index == 1, "Equivalent ratios churned saved index");
        });
        test("engine RPM estimate uses curve coverage power and base limiter with no extrapolation", () =>
        {
            var f = Fixture(root); var data = f.Service.Load(f.Car, f.Baseline, 2); var band = data.RpmEstimate;
            Check(band.Available && band.MinimumRpm >= 1000 && band.MaximumRpm <= 7600 && band.Explanation.Contains("Turbo") && band.Explanation.Contains("80%"), "Estimate lacks bounds or uncertainty");
            // Peak is 6000 × 280; 80% = 1,344,000. 4600 × 290 is below it;
            // 4700 × 292.5 and 7100 × 190 are inside, while 7200 × 180 is below.
            Near(band.MinimumRpm!.Value, 4700); Near(band.MaximumRpm!.Value, 7100);
            var t = Target() with { MinimumRpm = band.MinimumRpm.Value, MaximumRpm = band.MaximumRpm.Value, RpmSource = "Base engine curve estimate", RpmSourceFingerprint = data.CarDataEvidence!.Fingerprint };
            f.Service.Calculate(f.Car, f.Baseline, t);
            f.Entries["power.lut"] = f.Entries["power.lut"].Replace("6000|280", "6000|400"); f.Write(); Reject(() => f.Service.Calculate(f.Car, f.Baseline, t));
            f = Fixture(root); f.Entries["power.lut"] = "2000|300\n3000|300\n5000|300\n"; f.Write(); data = f.Service.Load(f.Car, f.Baseline, 2);
            Check(data.RpmEstimate.MaximumRpm <= 5000, "Curve extrapolated to engine limiter");
        });
        test("engine RPM estimate leaves malformed missing or narrow curves unavailable", () =>
        {
            foreach (var curve in new[] { "", "1000|100\n1000|200\n5000|300", "1000|100\nNaN|200\n5000|300", "1000|100\n3000|-200\n5000|300", "1000|0\n3000|0\n5000|0", "5000|300\n5100|300\n5200|300" })
            {
                var f = Fixture(root); f.Entries["power.lut"] = curve; f.Write(); Check(!f.Service.Load(f.Car, f.Baseline, 2).RpmEstimate.Available, "Invalid engine curve generated a band");
                f.Service.Calculate(f.Car, f.Baseline, Target()); // Explicit manual targets still work.
            }
            var bad = Fixture(root); bad.Entries["engine.ini"] = bad.Entries["engine.ini"].Replace("power.lut", "../outside.lut"); bad.Write(); Check(!bad.Service.Load(bad.Car, bad.Baseline, 2).RpmEstimate.Available, "External RPM curve followed");
        });
        test("corner targets and default units persist separately and legacy single targets roundtrip", () =>
        {
            var f = Fixture(root); var store = new GearingTargetStore(Path.Combine(f.Root, "targets")); var input = new TuneInput { Car = f.Car };
            var target = Target() with { DisplayMph = true }; store.Save(input, target); Check(store.Load(input) == target, "Both targets lost in roundtrip");
            var legacy = Target() with { Sweeper = null, DisplayMph = false }; store.Save(input, legacy); Check(store.Load(input) == legacy, "Legacy target lost");
            var oldJson = JsonSerializer.Deserialize<GearingTarget>("{\"Gear\":3,\"MinimumSpeedKmh\":60,\"MaximumSpeedKmh\":100,\"MinimumRpm\":4000,\"MaximumRpm\":6500,\"DisplayMph\":true}")!;
            oldJson.Validate(); Check(oldJson.Sweeper is null && oldJson.RpmSource == "Driver target", "Old profile invented a second goal or an automatic engine band");
            var unitsFile = Path.Combine(f.Root, "units.json"); var prefs = new SpeedUnitPreferenceStore(unitsFile); Check(prefs.Load() is null, "Missing preference invented"); prefs.Save(true); Check(new SpeedUnitPreferenceStore(unitsFile).Load() == true && store.Load(input) == legacy, "Global unit default changed a car's physical targets");
            File.WriteAllText(unitsFile, "broken"); Reject(() => prefs.Save(false)); Check(File.ReadAllText(unitsFile) == "broken", "Damaged preference erased");
        });
        test("recorded two-goal exposure retains both gears and does not double count overlapping windows", () =>
        {
            var s = PowertrainChecks.Session(); var t = s.Context!.Tune!; t.GearingTarget = Target() with { MinimumSpeedKmh = 50, MaximumSpeedKmh = 80, MinimumRpm = 4000, Sweeper = new(3, 50, 100), DisplayMph = true };
            foreach (var frame in s.Samples) { frame.SpeedKmh = 60; frame.Gear = frame.TimeSeconds < 40 ? 3 : 4; }
            var result = new TelemetryAnalyzer().Analyze(s).Diagnosis.Powertrain;
            Check(result.Gears.Count == 2 && result.Gears.All(g => g.BelowTargetSeconds > 10) && result.TargetContext.Contains("Tight corners") && result.TargetContext.Contains("mph") && result.NextTest.Contains("BOTH"), "Both corner exposures or joint guidance missing");
            var prior = JsonSerializer.Serialize(t.GearingTarget); var cloned = RunHistoryStore.Clone(t); Check(JsonSerializer.Serialize(cloned.GearingTarget) == prior, "History lost second target");
            foreach (var frame in s.Samples) frame.Gear = 4;
            t.GearingTarget = t.GearingTarget with { Gear = 3 };
            result = new TelemetryAnalyzer().Analyze(s).Diagnosis.Powertrain;
            Check(result.Gears.Single().TargetSpeedSeconds <= result.Gears.Single().Seconds + .001, "Same-gear overlap double-counted");
            t.GearingTarget = t.GearingTarget with { RpmSourceFingerprint = new string('b', 64) };
            result = new TelemetryAnalyzer().Analyze(s).Diagnosis.Powertrain;
            Check(result.TargetContext.Contains("different car data") && !result.NextTest.Contains("shorter"), "Stale engine band supported a test conclusion");
        });
        test("recorded speed helper requires verified identity and all selected gears before filling", () =>
        {
            var input = new TuneInput(); var s = PowertrainChecks.Session(); s.Context!.Tune!.ContextKey = RunHistoryStore.ContextKey(input);
            var saved = new SavedTelemetrySession { Session = s, Analysis = new TelemetryAnalyzer().Analyze(s) };
            var row = saved.Analysis.Diagnosis.Powertrain.Gears.Single(); row.LowSpeedKmh = 55; row.HighSpeedKmh = 85;
            var speeds = GearingRunSpeeds.Read(saved, input, new[] { 3 }); Near(speeds[0].MinimumSpeedKmh, 55);
            Reject(() => GearingRunSpeeds.Read(saved, input, new[] { 3, 2 }));
            row.Confidence = "LOW"; Reject(() => GearingRunSpeeds.Read(saved, input, new[] { 3 })); row.Confidence = "MEDIUM";
            s.Context.CarIdentityVerified = false; Reject(() => GearingRunSpeeds.Read(saved, input, new[] { 3 }));
        });
        test("gearing comparison distinguishes target changes from unit-only edits", () =>
        {
            var before = new SavedTelemetrySession { Session = PowertrainChecks.Session() }; var after = new SavedTelemetrySession { Session = RunHistoryStore.Clone(before.Session) };
            after.Session.Context!.Tune!.GearingTarget = after.Session.Context.Tune.GearingTarget! with { DisplayMph = true };
            var report = PowertrainComparison.Build(after, before);
            Check(report.Rows.Single(r => r.Metric == "Recorded gearing targets").Change == "Same", "Display unit changed the physical goal");
            after.Session.Context.Tune.GearingTarget = after.Session.Context.Tune.GearingTarget with { Sweeper = new(2, 40, 70) };
            report = PowertrainComparison.Build(after, before);
            Check(report.Rows.Single(r => r.Metric == "Recorded gearing targets").Change == "Changed" && report.Summary.Contains("not a like-for-like"), "Different corner goals compared without an explanation");
        });
    }
    internal static FixtureData Fixture(string root, bool packed = false)
    {
        var dir = Path.Combine(root, "corners-" + Guid.NewGuid().ToString("N")); var car = new CarProfile { Id = "physics_car", SourceFolderName = "physics_car", SourceFolderPath = Path.Combine(dir, "physics_car") };
        Directory.CreateDirectory(car.SourceFolderPath); var baseline = Path.Combine(dir, "baseline.ini");
        File.WriteAllText(baseline, "[CAR]\nMODEL=physics_car\n[FINAL_RATIO]\nVALUE=1\n[TYRES]\nVALUE=0\n[CAMBER_LF]\nVALUE=-30\n");
        var entries = new Dictionary<string, string> {
            ["setup.ini"] = "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\n", ["drivetrain.ini"] = "[TRACTION]\nTYPE=RWD\n[GEARS]\nCOUNT=3\nGEAR_1=3.2\nGEAR_2=2.1\nGEAR_3=1.5\nFINAL=3\n",
            ["engine.ini"] = "[HEADER]\nPOWER_CURVE=power.lut\n[ENGINE_DATA]\nLIMITER=8000\nMINIMUM=1000\n", ["tyres.ini"] = "[REAR]\nRADIUS=0.3\nNAME=Test tyre\n",
            ["final.rto"] = "Short|4.5\nLong|3\nMiddle|4\n", ["power.lut"] = "1000|100\n3000|250\n5000|300\n6000|280\n7000|200\n8000|100\n" };
        var f = new FixtureData(dir, car, baseline, entries, packed); f.Write(); return f;
    }
    internal sealed record FixtureData(string Root, CarProfile Car, string Baseline, Dictionary<string,string> Entries, bool Packed)
    {
        public GearingDataService Service { get; } = new();
        public void Write()
        {
            if (Packed) PackedArchiveChecks.WriteArchive(Car.SourceFolderPath!, Entries);
            else { var data = Path.Combine(Car.SourceFolderPath!, "data"); Directory.CreateDirectory(data); foreach (var p in Entries) File.WriteAllText(Path.Combine(data, p.Key), p.Value); }
        }
    }
}
