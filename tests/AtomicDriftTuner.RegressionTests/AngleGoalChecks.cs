using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class AngleGoalChecks
{
    internal static void Run(Action<string, Action> test, string root)
    {
        test("angle goals are optional, independently saved and validate custom ranges", () =>
        {
            var legacy = JsonSerializer.Deserialize<CarBehaviorTarget>("{\"AngleStability\":2}")!;
            Check(!legacy.HasAngleGoal && GuidedWorkflowStore.GoalSignature(legacy) == "0/0/0/0/2/0/0", "Legacy handling goals changed");
            var goal = Goal(); goal.AngleStability = 2;
            Check(goal.AngleMinDeg == 65 && goal.AngleMaxDeg == 80 && goal.AngleStability == 2, "Angle and stability were coupled");
            goal.CustomAngleMinDeg = 68; goal.CustomAngleMaxDeg = 83;
            Check(goal.ValidAngleGoal && !RunHistoryStore.SameBehavior(goal, Goal()), "Custom goal did not change comparison identity");
            foreach (var (min, max) in new[] { (19d, 70d), (70d, 86d), (70d, 74d), (double.NaN, 80d) })
            {
                goal.CustomAngleMinDeg = min; goal.CustomAngleMaxDeg = max;
                Check(!goal.ValidAngleGoal, "Invalid custom band accepted");
                goal.Normalize(); Check(goal.CustomAngleMinDeg is null && goal.CustomAngleMaxDeg is null, "Invalid band not reset");
            }
            goal.SustainedAngle = (SustainedAnglePreference)99; goal.Normalize();
            Check(!goal.HasAngleGoal, "Unknown angle preference enabled diagnosis");
        });
        test("angle goals survive per-car storage, share import and immutable tune history", () =>
        {
            var input = Input(); var goal = Goal(); goal.CustomAngleMinDeg = 67; goal.CustomAngleMaxDeg = 82;
            var dir = Path.Combine(root, "angle-behavior"); var store = BehaviorStore(dir);
            store.Save(input, goal);
            Check(RunHistoryStore.SameBehavior(BehaviorStore(dir).Load(input), goal), "Saved car angle changed on reopen");
            var other = RunHistoryStore.Clone(input); other.Car.Id = "different-angle-car"; other.Car.SourceFolderName = "different-angle-car";
            Check(!store.Load(other).HasAngleGoal, "Angle goal leaked to another car");
            var shares = new ShareCodeService(); var payload = shares.Create(input, new TuningEngine().Generate(input), goal);
            var imported = shares.Decode(shares.Encode(payload));
            Check(RunHistoryStore.SameBehavior(imported.Behavior.ToTarget(), goal), "Share import lost the angle band");
            var history = new RunHistoryStore(Path.Combine(root, "angle-history")); var driver = history.GetOrCreateDriver("Angle driver");
            var before = history.CaptureTune(input, driver, "Extreme target", goal, null);
            goal.SustainedAngle = SustainedAnglePreference.Current;
            var reopened = history.ListTunes(input, driver.Id).Single();
            Check(reopened.DesiredBehavior.HasAngleGoal && reopened.DesiredBehavior.AngleMinDeg == 67, "Goal mutation changed a tune snapshot");
            var after = history.CaptureTune(input, driver, "Current target", goal, null);
            Check(before.Settings.OrderBy(p => p.Key).SequenceEqual(after.Settings.OrderBy(p => p.Key)), "Choosing an angle goal altered FFB generation");
            var path = Path.Combine(dir, "car-behavior-targets.json"); var original = File.ReadAllText(path);
            var invalid = original.Replace("82", "89"); Check(invalid != original, "Invalid goal fixture did not change");
            File.WriteAllText(path, invalid); bool refused = false;
            try { store.Save(input, goal); } catch (InvalidDataException) { refused = true; }
            Check(refused && File.ReadAllText(path) == invalid, "Malformed saved goal was overwritten");
        });
        test("extreme angle means sustained in-band time with speed and observed forward recovery", () =>
        {
            var saved = Saved(Session()); var d = saved.Analysis.Diagnosis.AngleGoal;
            Check(d.Enabled && d.CompletedAttempts == 6 && d.RecoveredAttempts == 6 && d.ControlConcerns == 0, $"Unexpected holds/recovery: {d.Summary}");
            Near(d.TargetSeconds, 24, .1, "In-band time"); Near(d.LongestHoldSeconds, 4, .05, "Longest hold"); Near(d.SpeedRetentionPct!.Value, 100, .01, "Retained speed");
            Check(saved.Analysis.SpinEvents == 0 && saved.Analysis.Diagnosis.Metric("angle-hold")?.Confidence == "MEDIUM", "Requested angle alone was labelled loss of control");
            var normal = Session(); normal.Context!.Tune!.DesiredBehavior.SustainedAngle = SustainedAnglePreference.Current;
            var old = Saved(normal).Analysis;
            Check(!old.Diagnosis.AngleGoal.Enabled && old.SpinEvents == 6 && old.DriftTimeSeconds < saved.Analysis.DriftTimeSeconds - 20, "Opt-out no longer uses the existing angle assessment");
            var custom = Session(); custom.Context!.Tune!.DesiredBehavior.CustomAngleMinDeg = 45; custom.Context.Tune.DesiredBehavior.CustomAngleMaxDeg = 60;
            Check(Saved(custom).Analysis.Diagnosis.AngleGoal.TargetSeconds == 0, "Above-band angle rewarded as a held target");
        });
        test("angle hold duration and recovery are stable across supported sample rates", () =>
        {
            foreach (var hz in new[] { 15, 25, 50, 100 })
            {
                var d = Saved(Session(hz)).Analysis.Diagnosis.AngleGoal;
                Check(d.CompletedAttempts == 6 && d.RecoveredAttempts == 6, $"Recovery changed at {hz}Hz: {d.Summary}");
                Near(d.LongestHoldSeconds, 4, .08, $"Hold at {hz}Hz"); Near(d.TargetSeconds, 24, .1, $"Band time at {hz}Hz");
            }
        });
        test("brief angle peaks and clipped entry/recovery cannot establish sustained control", () =>
        {
            var spike = Session(50, .2); var a = Saved(spike).Analysis.Diagnosis.AngleGoal;
            Check(a.Attempts.Count == 0 && a.LongestHoldSeconds < .3, "Brief peaks became complete attempts");
            var end = Session(); end.Samples.RemoveAll(f => f.TimeSeconds >= 6.2);
            a = Saved(end).Analysis.Diagnosis.AngleGoal;
            Check(a.CompletedAttempts == 0 && a.IncompleteAttempts == 1 && a.RecoveredAttempts == 0, "Stopped recording invented recovery");
            var start = Session(); start.Samples.RemoveAll(f => f.TimeSeconds < 2);
            a = Saved(start).Analysis.Diagnosis.AngleGoal;
            Check(a.IncompleteAttempts == 1 && !a.Attempts[0].Complete, "Clipped entry accepted as complete");
        });
        test("angle holds cannot bridge missing packets or excluded driving", () =>
        {
            foreach (var exclude in new Action<TelemetrySample>[] { f => f.PitLimiterOn = true, f => f.IsAiControlled = true,
                f => f.Gear = 0, f => f.WheelsOutsideTrack = 4, f => f.LateralG = 6, f => f.SpeedKmh = double.NaN })
            {
                var s = Session(); s.Samples.RemoveAll(f => f.TimeSeconds >= 10);
                foreach (var f in s.Samples.Where(f => f.TimeSeconds >= 3.9 && f.TimeSeconds <= 4.1)) exclude(f);
                var d = Saved(s).Analysis.Diagnosis.AngleGoal;
                Check(d.LongestHoldSeconds < 2.2 && d.CompletedAttempts == 0 && d.RecoveredAttempts == 0, "Excluded interval bridged a hold or recovery");
            }
            var gap = Session(); gap.Samples.RemoveAll(f => f.TimeSeconds >= 10 || f.TimeSeconds >= 3.7 && f.TimeSeconds <= 4.3);
            Check(Saved(gap).Analysis.Diagnosis.AngleGoal.LongestHoldSeconds < 2, "Gap bridged a hold");
            var frozen = Session(); frozen.Samples.RemoveAll(f => f.TimeSeconds >= 10);
            foreach (var f in frozen.Samples.Where(f => f.TimeSeconds >= 3.9 && f.TimeSeconds < 4.1)) f.PacketId = 195;
            Check(Saved(frozen).Analysis.Diagnosis.AngleGoal.CompletedAttempts == 0, "Frozen packet established complete recovery");
            var reset = Session(); foreach (var f in reset.Samples.Where(f => f.TimeSeconds >= 4)) f.TimeSeconds -= 4;
            var r = Saved(reset).Analysis;
            Check(r.Diagnosis.TimelineReset && r.Diagnosis.AngleGoal.CompletedAttempts == 0, "Restarted timeline established angle control");
        });
        test("folded backward slip and major speed loss cannot count as successful extreme-angle recovery", () =>
        {
            var reverse = Session();
            foreach (var f in reverse.Samples.Where(f => f.TimeSeconds % 10 >= 5.5 && f.TimeSeconds % 10 < 6.5)) f.LongitudinalVelocityMs = -4;
            var a = Saved(reverse).Analysis;
            Check(a.Diagnosis.AngleGoal.ControlConcerns == 6 && a.Diagnosis.AngleGoal.RecoveredAttempts == 0 && a.SpinEvents >= 6, "Backward folded slip was rewarded");
            var slow = Session(); foreach (var f in slow.Samples.Where(f => f.TimeSeconds % 10 >= 5.5 && f.TimeSeconds % 10 < 6)) f.SpeedKmh = 30;
            var d = Saved(slow).Analysis.Diagnosis.AngleGoal;
            Check(d.SpeedRetentionPct < 60 && d.ControlConcerns == 6 && d.RecoveredAttempts == 0, "Major speed loss counted as goal success");
            var unknown = Session(); foreach (var f in unknown.Samples) f.LongitudinalVelocityMs = null;
            d = Saved(unknown).Analysis.Diagnosis.AngleGoal;
            Check(d.CompletedAttempts == 0 && d.IncompleteAttempts == 6 && d.TargetSeconds > 20, "Legacy direction coverage was invented or descriptive angle lost");
            var bad = Session(); bad.Samples[50].LongitudinalVelocityMs = double.NaN;
            Check(Saved(bad).Analysis.Diagnosis.InvalidSamples == 1, "Invalid longitudinal velocity accepted");
        });
        test("native telemetry preserves forward and backward velocity alongside folded slip", () =>
        {
            using var reader = new AssettoCorsaTelemetryReader(); using var map = MemoryMappedFile.CreateNew(null, 4096);
            using var view = map.CreateViewAccessor(); Set(reader, "_physicsMap", map);
            var type = typeof(AssettoCorsaTelemetryReader).GetNestedType("AcPhysics", BindingFlags.NonPublic)!;
            var offset = Marshal.OffsetOf(type, "LocalVelocity").ToInt64();
            view.WriteArray(offset, new[] { 12f, 0f, -3f }, 0, 3);
            var backward = reader.Read(1);
            Check(backward.LongitudinalVelocityMs == -3 && Math.Abs(backward.SlipAngleDeg) < 90, "Reader lost backward evidence from the folded angle signal");
            view.WriteArray(offset, new[] { 12f, 0f, 3f }, 0, 3);
            Check(reader.Read(1.02).LongitudinalVelocityMs == 3, "Reader lost forward direction");
        });
        test("remote handling updates preserve the desktop angle target", () =>
        {
            var input = Input(); var goal = Goal(); goal.CustomAngleMinDeg = 67; goal.CustomAngleMaxDeg = 82;
            var store = BehaviorStore(Path.Combine(root, "angle-remote")); store.Save(input, goal);
            var remote = (RemoteServerService)RuntimeHelpers.GetUninitializedObject(typeof(RemoteServerService));
            Set(remote, "_stateGate", new object()); Set(remote, "_currentInput", input); Set(remote, "_behaviorStore", store);
            var response = (RemoteActionResponse)typeof(RemoteServerService).GetMethod("SaveBehaviorFromRemote", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(remote, [new RemoteBehaviorUpdateRequest { RearGrip = 2, AngleStability = 2 }])!;
            var saved = store.Load(input);
            Check(response.Ok && saved.AngleMinDeg == 67 && saved.AngleMaxDeg == 82 && saved.HasAngleGoal && saved.RearGrip == 2, "Remote handling save erased the chosen angle target");
        });
        test("fresh angle runs persist raw direction, goal context and reanalyze without rewriting history", () =>
        {
            var s = Session(); var saved = Saved(s); var store = new TelemetrySessionStore();
            Set(store, "<RootDirectory>k__BackingField", Path.Combine(root, "angle-sessions"));
            var (path, csv) = store.Save(s, saved.Analysis); var bytes = File.ReadAllBytes(path);
            var loaded = store.TryLoad(path)!;
            Check(loaded.Analysis.Diagnosis.AngleGoal.RecoveredAttempts == 6 && loaded.Session.Samples[100].LongitudinalVelocityMs > 0, "Reopened run lost direction or recovery");
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Reanalysis modified historical raw recording");
            Check(File.ReadLines(csv).First().Split(',').Contains("longitudinal_velocity_ms"), "CSV direction channel absent");
        });
        test("matching angle goals compare longer holds while retaining speed and control tradeoffs", () =>
        {
            var first = Session(50, 2); var next = Session(50, 5);
            next.Context = RunHistoryStore.Clone(first.Context); next.StartedUtc = first.StartedUtc.AddMinutes(5);
            var before = Saved(first); var after = Saved(next); var engine = new RunComparisonEngine();
            Check(after.Analysis.AverageDriftAngleDeg - before.Analysis.AverageDriftAngleDeg > 10, "Fixture did not exercise changing angle exposure");
            var c = engine.Compare(before, after);
            Check(c.Comparable && c.Verdict == "Closer to goals", $"Longer controlled hold not comparable: {c.Summary} {string.Join("; ", c.Limitations)}");
            after.Analysis.Diagnosis.Metric("angle-speed")!.Value = 60;
            after.Analysis.Diagnosis.Metric("angle-recovery")!.Value = 40;
            Check(engine.Compare(before, after).Verdict == "Tradeoff", "Longer angle erased loss of speed/recovery");
            next.Context!.Tune!.DesiredBehavior.CustomAngleMinDeg = 70; next.Context.Tune.DesiredBehavior.CustomAngleMaxDeg = 85;
            Check(!engine.Compare(before, after).Comparable, "Changed target accepted against an old baseline");
        });
        test("angle goals leave existing car-setup and gearing generation values intact", () =>
        {
            var path = Path.Combine(root, "angle-setup.ini");
            File.WriteAllText(path, "[PRESSURE_LF]\nVALUE=25\n[CAMBER_LF]\nVALUE=-4\n[DIFF_POWER]\nVALUE=60\n[FINAL_RATIO]\nVALUE=3.9\n");
            var service = (AssettoCorsaSetupService)RuntimeHelpers.GetUninitializedObject(typeof(AssettoCorsaSetupService));
            var input = Input(); var normal = service.LoadBaseline(path, input.Car); var extreme = service.LoadBaseline(path, input.Car);
            var target = new CarBehaviorTarget { RearGrip = 1, TransitionSpeed = -1 }; var angle = RunHistoryStore.Clone(target); angle.SustainedAngle = SustainedAnglePreference.ExtremeAngle;
            var engine = new CarSetupTuningEngine(); engine.Generate(input, normal, SetupAggressiveness.Balanced, target); engine.Generate(input, extreme, SetupAggressiveness.Balanced, angle);
            Check(normal.Parameters.Select(p => p.RecommendedValue).SequenceEqual(extreme.Parameters.Select(p => p.RecommendedValue)), "Selecting angle modified setup generation");
            Check(angle.SustainedAngle == SustainedAnglePreference.ExtremeAngle, "Generation mutated caller goal");
        });
        test("simple next step uses saved goals and refuses to reinterpret earlier runs", () =>
        {
            var s = Saved(Session()); var builder = new DriftAssistantReportBuilder();
            var r = builder.Build(Input(), Goal(), s, null);
            Check(r.NextStep.Goal.Contains("extreme") && r.NextStep.Noticed.Contains("4.0s") && r.NextStep.Confidence != "Control needs review", "Simple summary lost the explicit goal or successful hold");
            var current = Goal(); current.SustainedAngle = SustainedAnglePreference.MoreAngle;
            var json = JsonSerializer.Serialize(s.Session);
            r = builder.Build(Input(), current, s, null);
            Check(r.NextStep.Confidence == "New baseline needed" && r.NextStep.Goal.Contains("extreme") && r.NextStep.Instruction.Contains("45–65"), "Changed goal reused earlier run as a new baseline");
            Check(JsonSerializer.Serialize(s.Session) == json, "Summary changed historical goals");
            var shortRun = Session(); shortRun.Samples.RemoveAll(f => f.TimeSeconds > 3);
            r = builder.Build(Input(), Goal(), Saved(shortRun), null);
            Check(r.NextStep.Confidence == "Another clean run needed" && r.NextStep.Recommendation is null, "Low data presented a testable tuning recommendation");
        });
        test("simple result prioritizes comparison review and driver feedback", () =>
        {
            var baseline = Session(50, 2); var after = Session(50, 5); after.Context = RunHistoryStore.Clone(baseline.Context); after.StartedUtc = baseline.StartedUtc.AddMinutes(5);
            var builder = new DriftAssistantReportBuilder();
            var r = builder.Build(Input(), Goal(), Saved(after), Saved(baseline));
            Check(r.NextStep.Action == "Review" && r.NextStep.Instruction.Contains("how the car felt"), "Useful comparison did not lead to driver feedback");
            after.Context!.Conditions = "different section";
            r = builder.Build(Input(), Goal(), Saved(after), Saved(baseline));
            Check(r.NextStep.Action == "Compare" && r.NextStep.Why.Contains("Track/layout"), "Unfair comparison not explained in the next step");
        });
        test("repeated goal-relevant pedal patterns earn one next step while isolated activity stays in details", () =>
        {
            var session = IntelligenceChecks.Session();
            foreach (var f in session.Samples)
            {
                var p = f.TimeSeconds % 10; f.SlipAngleDeg = p >= 2.15 && p < 5.2 ? 42 : 30;
                f.Throttle = p >= 2 && p < 5 ? .9 : .15; f.SteeringRateDegPerSec = 0; f.YawRateDegPerSec = f.SlipAngleDeg * .7;
            }
            session.Context!.Tune!.DesiredBehavior = new() { RearGrip = 2 };
            var saved = Saved(session); var r = new DriftAssistantReportBuilder().Build(Input(), new() { RearGrip = 2 }, saved, null);
            Check(r.Recommendations.Count(x => x.Priority == "Repeat inputs") == 1 && r.NextStep.Recommendation?.Priority == "Repeat inputs", "Repeated rear-grip pattern did not yield one actionable next step");
            Check(saved.Analysis.Diagnosis.Pedals.Events.Count >= 10 && r.Assessments.Any(x => x.Behavior == "Throttle and rear response"), "Detailed pedal evidence lost");
        });
    }
    private static CarBehaviorTarget Goal() => new() { SustainedAngle = SustainedAnglePreference.ExtremeAngle };
    private static TuneInput Input() => new() { Hardware = BuiltInProfiles.Hardware()[3], Wheel = BuiltInProfiles.Wheels()[0], DriftPack = BuiltInProfiles.DriftPacks()[0], Car = BuiltInProfiles.Cars()[0], Intent = BuiltInProfiles.Intents()[1] };
    private static SavedTelemetrySession Saved(TelemetrySession session) => new() { Session = session, Analysis = new TelemetryAnalyzer().Analyze(session) };
    private static TelemetrySession Session(int hz = 50, double hold = 4)
    {
        var s = IntelligenceChecks.Session(hz); s.Samples.RemoveAll(f => f.TimeSeconds >= 60); s.Context!.Tune!.DesiredBehavior = Goal();
        foreach (var f in s.Samples)
        {
            var t = f.TimeSeconds % 10; f.SlipAngleDeg = t >= 2 && t < 2 + hold ? 75 : 30;
            f.LongitudinalVelocityMs = f.SpeedKmh / 3.6 * Math.Cos(f.SlipAngleDeg * Math.PI / 180);
            f.SteeringAngleDeg = -2 * f.SlipAngleDeg; f.SteeringRateDegPerSec = 0; f.YawRateDegPerSec = .7 * f.SlipAngleDeg;
            f.Throttle = .75; f.Brake = 0; f.Clutch = 1; f.Gear = 3; f.Rpm = 5000;
        }
        return s;
    }
    private static CarBehaviorProfileStore BehaviorStore(string dir)
    {
        var store = (CarBehaviorProfileStore)RuntimeHelpers.GetUninitializedObject(typeof(CarBehaviorProfileStore));
        Set(store, "_directory", dir); Set(store, "_path", Path.Combine(dir, "car-behavior-targets.json")); return store;
    }
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static void Near(double actual, double expected, double tolerance, string why) => Check(Math.Abs(actual - expected) <= tolerance, $"{why}: expected {expected}, actual {actual}");
}
