using System.IO;
using System.Reflection;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PowertrainChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Near(double a, double b, double tolerance = .01) => Check(Math.Abs(a - b) <= tolerance, $"Expected {b}, got {a}");
    private static CarSetupParameter Param(string key, int value) => new() { Section = key, CurrentRaw = value.ToString(), CurrentValue = value };
    private static List<CarSetupParameter> Selections() => [Param("FINAL_RATIO", 1), Param("INTERNAL_GEAR_4", 1), Param("ENGINE_MAPS", 2)];
    private static Dictionary<string, string> Files()
    {
        var files = SetupDecodingChecks.Fixture();
        files["drivetrain.ini"] = "[GEARS]\nCOUNT=3\nGEAR_1=3.2\nGEAR_2=2.1\nGEAR_3=1.5\nFINAL=3.9\n";
        files["engine.ini"] += "[ENGINE_DATA]\nLIMITER=8000\n";
        return files;
    }
    private static PowertrainContext Read(Dictionary<string, string> files, IReadOnlyList<CarSetupParameter>? selections = null) =>
        new CarSetupDecoder().ReadPowertrain(selections ?? Selections(), name => files.GetValueOrDefault(name));
    internal static TelemetrySession Session(int hz = 50)
    {
        var session = IntelligenceChecks.Session(hz);
        var tune = session.Context!.Tune!; var files = Files();
        tune.BasePhysicsFingerprint = new string('a', 64);
        tune.Powertrain = Read(files); tune.Powertrain.PhysicsFingerprint = tune.BasePhysicsFingerprint;
        tune.DecodedSetup = new CarSetupDecoder().Decode(Selections(), name => files.GetValueOrDefault(name)).ToList();
        tune.GearingTarget = new() { Gear = 3, MinimumSpeedKmh = 50, MaximumSpeedKmh = 100, MinimumRpm = 4000, MaximumRpm = 6500 };
        tune.Settings["ACSetup.FINAL_RATIO"] = 1; tune.Settings["ACSetup.ENGINE_MAPS"] = 2;
        foreach (var f in session.Samples) { f.Rpm = 3500; f.Gear = 4; f.LongitudinalVelocityMs = 12; }
        return session;
    }
    private static PowertrainDiagnosis Analyze(TelemetrySession session) => new TelemetryAnalyzer().Analyze(session).Diagnosis.Powertrain;
    private static SavedTelemetrySession Saved(TelemetrySession session) => new() { Session = session, Analysis = new TelemetryAnalyzer().Analyze(session) };

    internal static void Run(Action<string, Action> test, string root)
    {
        test("powertrain records saved final and individual gear selections instead of base defaults", () =>
        {
            var p = Read(Files()); Near(p.FinalDrive!.Value, 4.3); Near(p.Gears.Single(g => g.Gear == 3).Ratio, 1.8);
            Near(p.Gears.Single(g => g.Gear == 1).Ratio, 3.2); Near(p.BaseLimiterRpm!.Value, 8000);
            Check(!p.AdjustableLimiter && p.Gears.Count == 3, "Fixed limiter or independent gears lost");
            var fixedFiles = Files(); fixedFiles["setup.ini"] = "[OTHER]\nMIN=0\n";
            var fixedBox = Read(fixedFiles, []); Near(fixedBox.FinalDrive!.Value, 3.9); Near(fixedBox.Gears[2].Ratio, 1.5);
            fixedFiles["setup.ini"] += "[GEARS]\nUSE_GEARSET=1\n";
            Near(Read(fixedFiles, []).Gears[2].Ratio, 1.5); // CM-compatible fixed gearbox with no presets.
        });
        test("powertrain preserves independent ratios while refusing missing duplicate and unknown overrides", () =>
        {
            foreach (var selections in new[] { new List<CarSetupParameter>(), Selections().Concat([Param("FINAL_RATIO", 0), Param("INTERNAL_GEAR_4", 0)]).ToList() })
            {
                var p = Read(Files(), selections);
                Check(p.FinalDrive is null && !p.Gears.Any(g => g.Gear == 3) && p.Gears.Count == 2, "Unverified selection fell back to base or erased other gears");
            }
            var files = Files(); files["setup.ini"] = "[OTHER]\nMIN=0\n";
            Check(Read(files, [Param("GEARSET", 0)]).Gears.Count == 0, "Unknown gearset guessed as fixed");
            files = Files(); files["setup.ini"] += "[ENGINE_LIMITER]\nMIN=90\nMAX=110\n";
            Check(Read(files).AdjustableLimiter, "Definition-only adjustable limiter ignored");
            Check(Read(Files(), Selections().Concat([Param("ENGINE_LIMITER", 100)]).ToList()).AdjustableLimiter, "Saved-only adjustable limiter ignored");
        });
        test("powertrain snapshots explicitly selected gearsets and contains damaged source sections", () =>
        {
            var files = Files(); files["setup.ini"] = files["setup.ini"].Replace("USE_GEARSET=0", "USE_GEARSET=1") +
                "[GEAR_SET_1]\nGEAR_1=3.5\nGEAR_2=2.4\nGEAR_3=1.7\n";
            var p = Read(files, Selections().Concat([Param("GEARSET", 1)]).ToList()); Near(p.Gears[2].Ratio, 1.7);
            Check(Read(files).Gears.Count == 0, "Missing selection became a default gearset");
            files = Files(); files["setup.ini"] += "[GEAR_3]\nRATIOS=ambiguous.rto\n";
            p = Read(files); Check(p.Gears.Count == 2 && p.FinalDrive == 4.3, "Bad gear erased unrelated mappings");
            var failure = new CarSetupDecoder().ReadPowertrain(Selections(), name => name == "setup.ini" ? throw new IOException("fixture") : files.GetValueOrDefault(name));
            Check(failure.AdjustableLimiter && failure.BaseLimiterRpm is null && failure.FinalDrive is null, "Read failure became a known limiter/final drive");
        });
        test("powertrain measures forward RPM by real gear and excludes missing invalid and backward evidence", () =>
        {
            var good = Analyze(Session()); Check(good.Gears.Count == 1 && good.Gears[0].Gear == 3 && good.Gears[0].Seconds > 60, "Raw gear offset or drift filtering incorrect");
            foreach (var invalid in new[] { "reverse", "neutral", "rpm", "backward", "pit", "AI", "off-track", "source", "frozen" })
            {
                var s = Session(); foreach (var f in s.Samples)
                {
                    switch (invalid) {
                        case "reverse": f.Gear = 0; break; case "neutral": f.Gear = 1; break; case "rpm": f.Rpm = 0; break;
                        case "backward": f.LongitudinalVelocityMs = -5; break; case "pit": f.PitLimiterOn = true; break;
                        case "AI": f.IsAiControlled = true; break; case "off-track": f.WheelsOutsideTrack = 4; break;
                        case "source": f.InvalidSourceSignals = true; break; case "frozen": f.PacketId = 1; break;
                    }
                }
                Check(Analyze(s).Gears.Count == 0, invalid + " produced RPM evidence");
            }
        });
        test("powertrain time weighted bands and phases remain stable across sample rates", () =>
        {
            PowertrainDiagnosis? reference = null;
            foreach (int hz in new[] { 15, 25, 50, 100 })
            {
                var s = Session(hz); foreach (var f in s.Samples) f.Rpm = f.TimeSeconds % 25 < 12 ? 3500 : 5500;
                var p = Analyze(s); Check(p.Phases.Any(v => v.Phase == "Initiation") && p.Phases.Any(v => v.Phase == "Transition"), "Phase RPM missing");
                var g = p.Gears.Single(); Near(g.LowRpm, 3500); Near(g.HighRpm, 5500);
                if (reference is not null) { Near(g.Seconds, reference.Gears[0].Seconds, .5); Near(g.MedianRpm, reference.Gears[0].MedianRpm); }
                reference = p;
            }
            var uneven = Session(); uneven.Samples.Clear(); double time = 0;
            for (int i = 0; i < 200; i++) { time += .1; uneven.Samples.Add(new() { TimeSeconds = time, PacketId = i + 1, Rpm = 3000, Gear = 4, SpeedKmh = 60, SlipAngleDeg = 30 }); }
            for (int i = 0; i < 1000; i++) { time += .005; uneven.Samples.Add(new() { TimeSeconds = time, PacketId = i + 201, Rpm = 6000, Gear = 4, SpeedKmh = 60, SlipAngleDeg = 30 }); }
            Near(Analyze(uneven).Gears.Single().MedianRpm, 3000);
        });
        test("powertrain target mismatches apply only to the saved gear and speed window", () =>
        {
            var s = Session(); var p = Analyze(s);
            Check(p.Gears.Single().BelowTargetSeconds > 60 && p.NextTest.Contains("shorter final-drive"), "Repeated low RPM did not produce a bounded test hypothesis");
            foreach (var f in s.Samples) f.SpeedKmh = 30;
            p = Analyze(s); Near(p.Gears.Single().BelowTargetSeconds!.Value, 0);
            Check(p.Events.Count == 0 && !p.NextTest.Contains("shorter final-drive"), "Low speed outside requested band caused a gearing suggestion");
            foreach (var f in s.Samples) { f.SpeedKmh = 60; f.Gear = 3; }
            Check(Analyze(s).Gears.Single().BelowTargetSeconds is null, "Another gear inherited the target");
        });
        test("powertrain does not bridge gaps gear shifts clutch movement or brake use into low RPM episodes", () =>
        {
            foreach (var kind in new[] { "gaps", "shifts", "clutch", "brake" })
            {
                var s = Session(); foreach (var f in s.Samples)
                {
                    if (kind == "gaps") f.TimeSeconds += Math.Floor(f.TimeSeconds / .3);
                    if (kind == "shifts") f.Gear = (int)(f.TimeSeconds / .3) % 2 == 0 ? 4 : 3;
                    if (kind == "clutch") f.Clutch = (int)(f.TimeSeconds / .3) % 2;
                    if (kind == "brake") f.Brake = .3;
                }
                var p = Analyze(s); Check(p.Events.Count == 0 && !p.NextTest.Contains("shorter final-drive"), kind + " became continuous low RPM evidence");
            }
        });
        test("powertrain incomplete unconfirmed and interrupted context withholds concrete gearing suggestions", () =>
        {
            foreach (var kind in new[] { "direction", "extended", "unconfirmed", "identity", "interrupted", "monitoring", "target", "ratio" })
            {
                var s = Session(); switch (kind) {
                    case "direction": foreach (var f in s.Samples) f.LongitudinalVelocityMs = null; break;
                    case "extended": foreach (var f in s.Samples) f.HasExtendedSignals = false; break;
                    case "unconfirmed": s.Context!.TuneConfirmedInUse = false; break;
                    case "identity": s.Context!.CarIdentityVerified = false; break;
                    case "interrupted": s.Context!.Interrupted = true; break;
                    case "monitoring": s.Context!.SetupCaptureIssue = "lost"; break;
                    case "target": s.Context!.Tune!.GearingTarget = null; break;
                    case "ratio": s.Context!.Tune!.Powertrain!.Gears.Clear(); break;
                }
                var p = Analyze(s); Check(p.Gears.Count == 1 && !p.NextTest.Contains("shorter final-drive"), kind + " lost raw evidence or authorized a test");
            }
        });
        test("powertrain base limiter proximity never becomes confirmed contact and adjustable limits stay unknown", () =>
        {
            var s = Session(); foreach (var f in s.Samples) f.Rpm = 7900;
            var p = Analyze(s); Check(p.Gears[0].NearBaseLimiterSeconds > 60 && p.NextTest.Contains("Confirm actual limiter contact"), "Base proximity did not preserve uncertainty");
            s.Context!.Tune!.Powertrain!.AdjustableLimiter = true; p = Analyze(s);
            Check(p.Gears[0].NearBaseLimiterSeconds is null && !p.Events.Any(e => e.Kind.Contains("limiter")) && !p.NextTest.Contains("taller"), "Adjustable limit used as active cutoff");
        });
        test("powertrain ECU curve uses only its recorded defined interval and retains unnamed selection uncertainty", () =>
        {
            var s = Session(); var p = Analyze(s);
            Check(p.MapPoints.Count == 3 && p.EcuContext.Contains("1.175") && p.EcuContext.Contains("does not measure horsepower"), "Wrong interpolation or horsepower claim");
            foreach (var f in s.Samples) f.Rpm = 9000;
            p = Analyze(s); Check(p.EcuContext.Contains("does not overlap"), "Curve extrapolated beyond its RPM domain");
            s.Context!.Tune!.HasUnassignedSetupValues = true; p = Analyze(s);
            Check(p.MapPoints.Count == 0 && p.EcuContext.Contains("unidentified"), "Unnamed VALUE guessed as ECU");
            s.Context.Tune.HasUnassignedSetupValues = false;
            s.Context.Tune.DecodedSetup = s.Context.Tune.DecodedSetup.Select(d => d with { EngineMap = null }).ToList();
            Check(Analyze(s).MapPoints.Count == 0, "Legacy textual decode became a made-up curve");
        });
        test("powertrain comparison separates verified gearing and ECU changes from coverage and formatting differences", () =>
        {
            var before = Saved(Session()); var afterSession = RunHistoryStore.Clone(before.Session); afterSession.Id = Guid.NewGuid().ToString("N");
            var tune = afterSession.Context!.Tune!; tune.Powertrain!.FinalDrive = 3.9;
            tune.DecodedSetup = tune.DecodedSetup.Select(d => d.EngineMap is null ? d : d with { EngineMap = d.EngineMap with { Index = 1 } }).ToList();
            var report = PowertrainComparison.Build(Saved(afterSession), before);
            Check(report.Summary.Contains("Both gearing and ECU") && report.Rows.Any(r => r.Metric == "Recorded final drive"), "Combined changes were not explained");
            tune.Powertrain.FinalDrive = 4.3; tune.Powertrain.Gears.RemoveAt(0);
            report = PowertrainComparison.Build(Saved(afterSession), before);
            Check(!report.Summary.Contains("Both gearing and ECU") && report.Summary.Contains("coverage differs"), "Missing gear coverage counted as a changed ratio");
            tune.DecodedSetup = before.Session.Context!.Tune!.DecodedSetup.Select(d => d with { SavedValue = d.SavedValue + ".0", Section = d.Section.ToLowerInvariant() }).ToList();
            report = PowertrainComparison.Build(Saved(afterSession), before);
            Check(report.Rows.Single(r => r.Metric == "Recorded ECU mapping").Change == "Same selection", "Equivalent numeric encoding became another map");
        });
        foreach (var packed in new[] { false, true })
            test("powertrain history preserves target and curve snapshots after files and targets change: " + (packed ? "packed" : "unpacked"), () =>
            {
                var dir = Path.Combine(root, "powertrain-" + Guid.NewGuid().ToString("N")); var carDir = Path.Combine(dir, "physics_car"); Directory.CreateDirectory(carDir);
                var files = Files();
                void WriteFiles() { if (packed) PackedArchiveChecks.WriteArchive(carDir, files); else { var data = Path.Combine(carDir, "data"); Directory.CreateDirectory(data); foreach (var pair in files) File.WriteAllText(Path.Combine(data, pair.Key), pair.Value); } }
                WriteFiles();
                var input = new TuneInput { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = new() { Id = "physics_car", SourceFolderName = "physics_car", SourceFolderPath = carDir }, Intent = BuiltInProfiles.Intents()[1] };
                var setup = Path.Combine(dir, "baseline.ini"); File.WriteAllText(setup, "[CAR]\nMODEL=physics_car\n[FINAL_RATIO]\nVALUE=1\n[INTERNAL_GEAR_4]\nVALUE=1\n[ENGINE_MAPS]\nVALUE=2\n");
                var targets = new GearingTargetStore(Path.Combine(dir, "targets")); var target = new GearingTarget { Gear = 3 };
                targets.Save(input, target);
                var store = new RunHistoryStore(Path.Combine(dir, "history")); var driver = store.GetOrCreateDriver("Powertrain fixture");
                var tune = store.CaptureTune(input, driver, "Baseline", new(), null, setup, gearingTargets: targets);
                Check(tune.GearingTarget == target && tune.Powertrain?.FinalDrive == 4.3 && tune.DecodedSetup.Single(d => d.Section == "ENGINE_MAPS").EngineMap!.Points.Count == 3, "Recorded definitions lost");
                var snapshot = JsonSerializer.Serialize(tune);
                files["race.lut"] = "0|2\n8000|2\n"; WriteFiles(); targets.Save(input, target with { MinimumRpm = 4500 });
                var restored = store.ListTunes(input, driver.Id).Single(); Check(JsonSerializer.Serialize(restored) == snapshot, "History changed with car files or target edits");
                tune.Powertrain!.Gears.Clear(); Check(store.ListTunes(input, driver.Id).Single().Powertrain!.Gears.Count == 3, "Returned object mutated stored history");
                var next = store.CaptureTune(input, driver, "Next", new(), null, setup, gearingTargets: targets);
                Check(next.BasePhysicsFingerprint != restored.BasePhysicsFingerprint && next.GearingTarget!.MinimumRpm == 4500, "Fresh snapshot reused stale files");
                File.WriteAllText(Directory.GetFiles(Path.Combine(dir, "targets")).Single(), "broken");
                var unknown = store.CaptureTune(input, driver, "Unknown target", new(), null, setup, gearingTargets: targets);
                Check(unknown.GearingTarget is null && unknown.GearingTargetStatus.Contains("could not be read"), "Damaged target substituted example RPM");
            });
        test("powertrain malformed context is rejected while case-insensitive engine selections remain valid", () =>
        {
            foreach (var invalid in new[] { "fingerprint", "null", "duplicates", "ratio", "curve", "target" })
            {
                var s = Session(); var t = s.Context!.Tune!;
                switch (invalid) {
                    case "fingerprint": t.Powertrain!.PhysicsFingerprint = new string('b', 64); break;
                    case "null": t.Powertrain!.PhysicsFingerprint = null!; break;
                    case "duplicates": t.Powertrain!.Gears.Add(t.Powertrain.Gears[0]); break;
                    case "ratio": t.Powertrain!.FinalDrive = double.NaN; break;
                    case "curve": t.DecodedSetup = t.DecodedSetup.Select(d => d.EngineMap is null ? d : d with { EngineMap = d.EngineMap with { Points = [new(1000, 1), new(1000, 2)] } }).ToList(); break;
                    case "target": t.GearingTarget = new() { Gear = 0 }; break;
                }
                Check(!RunHistoryStore.ValidContext(s.Context), invalid + " context accepted");
                Check(Analyze(s).MapPoints.Count == 0, "Malformed context supplied ECU points");
            }
            var valid = Session(); valid.Context!.Tune!.DecodedSetup = valid.Context.Tune.DecodedSetup.Select(d => d with { Section = d.Section.ToLowerInvariant() }).ToList();
            Check(RunHistoryStore.ValidContext(valid.Context), "Valid lower-case section rejected");
        });
        test("powertrain preserves raw sessions and all existing handling and FFB results and can skip live work", () =>
        {
            var s = Session(); var raw = JsonSerializer.Serialize(s);
            var full = new DriftDiagnosisEngine().Analyze(s); var without = new DriftDiagnosisEngine().Analyze(s, includePowertrain: false);
            Check(full.Diagnosis.Powertrain.Gears.Count == 1 && without.Diagnosis.Powertrain.Gears.Count == 0, "Live analysis did not skip gearing work");
            Check(without.Diagnosis.DrivingContext.Observations.Count == 0, "Live analysis did not skip condition work");
            full.Diagnosis.Powertrain = new(); full.Diagnosis.DrivingContext = new();
            Check(JsonSerializer.Serialize(full) == JsonSerializer.Serialize(without), "Detailed analysis affected existing diagnosis or calibration");
            Check(raw == JsonSerializer.Serialize(s), "Analysis mutated a recording");
            s.Context!.Tune!.Powertrain = null; s.Context.Tune.GearingTarget = null; s.Context.Tune.DecodedSetup.Clear();
            var legacy = Analyze(s); Check(legacy.Gears.Count == 1 && legacy.MapPoints.Count == 0 && legacy.Limitations.Contains("retrospectively"), "Old run gained new definitions or lost raw RPM");
            var sessionStore = new TelemetrySessionStore(); typeof(TelemetrySessionStore).GetField("<RootDirectory>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sessionStore, Path.Combine(root, "powertrain-roundtrip"));
            var fresh = Session(); var path = sessionStore.Save(fresh, new TelemetryAnalyzer().Analyze(fresh)).JsonPath;
            var bytes = File.ReadAllBytes(path); var reload = sessionStore.TryLoad(path)!;
            Check(reload.Analysis.Diagnosis.Powertrain.MapPoints.Count == 3 && reload.Session.Context!.Tune!.GearingTarget == fresh.Context!.Tune!.GearingTarget && bytes.SequenceEqual(File.ReadAllBytes(path)), "Reload lost context or rewrote history");
        });
    }
}
