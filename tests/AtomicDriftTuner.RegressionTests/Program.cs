using AtomicDriftTuner.Data;
using AtomicDriftTuner.Engine;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.CompilerServices;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

var failures = 0;
var root = Path.Combine(Path.GetTempPath(), "adt-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Run("telemetry failure creates a reconnectable reader", () =>
{
    using var hub = new TelemetryHubService();
    Invoke(hub, "HandleReadFailureLocked", "simulated read failure");
    var reader = (AssettoCorsaTelemetryReader)Get(hub, "_reader")!;
    Assert(!(bool)Get(reader, "_disposed")!, "Recovery retained a permanently disposed reader.");
});
Run("unchanged physics packet does not refresh telemetry freshness", () =>
{
    using var hub = new TelemetryHubService();
    var reader = (AssettoCorsaTelemetryReader)Get(hub, "_reader")!;
    using var map = MemoryMappedFile.CreateNew(null, 4096);
    Set(reader, "_physicsMap", map);
    Invoke(hub, "ReadOnceLocked");
    var first = Get(hub, "_updatedUtc");
    Thread.Sleep(30);
    Invoke(hub, "ReadOnceLocked");
    Assert(Equals(first, Get(hub, "_updatedUtc")), "Frozen packet was reported as fresh.");
});
Run("failed session save preserves an existing unrelated folder", () =>
{
    var store = new TelemetrySessionStore();
    var sessions = Path.Combine(root, "sessions");
    Set(store, "<RootDirectory>k__BackingField", sessions);
    var session = new TelemetrySession { CarName = "Test Car", StartedUtc = DateTime.UtcNow };
    var existing = Path.Combine(sessions, session.StartedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_Test Car");
    Directory.CreateDirectory(existing);
    var sentinel = Path.Combine(existing, "notes.txt");
    File.WriteAllText(sentinel, "preserve me");
    session.Samples.Add(new TelemetrySample { SpeedKmh = double.NaN });
    try { store.Save(session, new TelemetryAnalysis()); } catch (Exception) { }
    Assert(File.Exists(sentinel), "Failed save deleted pre-existing notes.");
});
Run("zero-byte behavior storage is not silently reset", () =>
{
    var dir = Path.Combine(root, "behavior");
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "car-behavior-targets.json");
    File.WriteAllText(path, "");
    var store = (CarBehaviorProfileStore)RuntimeHelpers.GetUninitializedObject(typeof(CarBehaviorProfileStore));
    Set(store, "_directory", dir);
    Set(store, "_path", path);
    var input = new TuneInput();
    input.DriftPack.Id = "pack";
    input.Car.Id = "car";
    var refused = false;
    try { store.Save(input, new CarBehaviorTarget()); } catch (InvalidDataException) { refused = true; }
    Assert(refused && new FileInfo(path).Length == 0, "Corrupted storage was overwritten as an empty database.");
});
Run("stale mapping is released for game restart recovery", () =>
{
    using var hub = new TelemetryHubService();
    var reader = (AssettoCorsaTelemetryReader)Get(hub, "_reader")!;
    using var map = MemoryMappedFile.CreateNew(null, 4096);
    Set(reader, "_physicsMap", map);
    Invoke(hub, "ReadOnceLocked");
    Set(hub, "_updatedUtc", DateTimeOffset.UtcNow.AddSeconds(-1));
    Invoke(hub, "ReadOnceLocked");
    Assert(!ReferenceEquals(reader, Get(hub, "_reader")), "Stale mapping prevented reconnect.");
});
Run("behavior keys cannot collide through embedded separators", () =>
{
    var input = ValidInput();
    input.Car.Id = "other|car";
    var refused = false;
    try { CarBehaviorProfileStore.BuildKey(input); } catch (InvalidDataException) { refused = true; }
    Assert(refused, "An embedded key separator was accepted.");
});
Run("valid behavior profile saves and reloads", () =>
{
    var dir = Path.Combine(root, "behavior-valid");
    var store = (CarBehaviorProfileStore)RuntimeHelpers.GetUninitializedObject(typeof(CarBehaviorProfileStore));
    Set(store, "_directory", dir);
    Set(store, "_path", Path.Combine(dir, "car-behavior-targets.json"));
    var input = ValidInput();
    store.Save(input, new CarBehaviorTarget { FrontEndBite = 1, RearGrip = -2 });
    var target = store.Load(input);
    Assert(target.FrontEndBite == 1 && target.RearGrip == -2, "Behavior round trip failed.");
});
Run("valid telemetry saves twice without overwriting and reloads", () =>
{
    var store = new TelemetrySessionStore();
    Set(store, "<RootDirectory>k__BackingField", Path.Combine(root, "valid-sessions"));
    var session = new TelemetrySession { CarName = "Car", EndedUtc = DateTime.UtcNow };
    for (var i = 0; i < 100; i++) session.Samples.Add(new TelemetrySample { PacketId = i, TimeSeconds = i * .02, SpeedKmh = 50, SlipAngleDeg = 25 });
    var analysis = new TelemetryAnalyzer().Analyze(session);
    var first = store.Save(session, analysis);
    var second = store.Save(session, analysis);
    Assert(first.JsonPath != second.JsonPath && File.Exists(first.CsvPath), "Session save overwrote a prior run.");
    Assert(store.TryLoad(first.JsonPath)?.Session.Samples.Count == 100, "Telemetry round trip failed.");
});
Run("saved tune round trip and corrupt envelope rejection", () =>
{
    var input = ValidInput();
    var result = new TuningEngine().Generate(input);
    var store = new ProfileStore();
    var path = Path.Combine(root, "valid.adt.json");
    store.Save(new SavedTune { Input = input, Result = result }, path);
    Assert(store.Load(path).Input.Car.Id == input.Car.Id, "Tune identity changed.");
    File.WriteAllText(path, "{}");
    var refused = false;
    try { store.Load(path); } catch (InvalidDataException) { refused = true; }
    Assert(refused, "Missing profile fields were accepted.");
});
Run("all built-in hardware/car/intent combinations stay in output ranges", () =>
{
    var count = 0;
    foreach (var hardware in BuiltInProfiles.Hardware())
    foreach (var wheel in BuiltInProfiles.Wheels())
    foreach (var car in BuiltInProfiles.Cars())
    foreach (var intent in BuiltInProfiles.Intents())
    {
        var input = new TuneInput { Hardware = hardware, Wheel = wheel, Car = car, DriftPack = BuiltInProfiles.DriftPacks().Single(p => p.Id == car.PackId), Intent = intent };
        var result = new TuningEngine().Generate(input);
        Assert(result.Ac.GainPct is >= 35 and <= 90, "AC gain outside engine contract.");
        Assert(result.Azom.Core.BaseTorqueOutputPct is >= 50 and <= 100, "Torque outside engine contract.");
        Assert(double.IsFinite(result.EstimatedPeakWheelTorqueNm), "Nonfinite torque estimate.");
        count++;
    }
    Console.WriteLine($"  Validated {count} built-in combinations.");
});
Run("portable share round trip preserves car identity", () =>
{
    var input = ValidInput();
    var service = new ShareCodeService();
    var payload = service.Create(input, new TuningEngine().Generate(input), new CarBehaviorTarget());
    var restored = service.ToTuneInput(service.Decode(service.Encode(payload)));
    Assert(restored.Car.Id == input.Car.Id, "Share car identity changed.");
});
Run("setup generation refuses a changed baseline", () =>
{
    var path = Path.Combine(root, "baseline.ini");
    File.WriteAllText(path, "[PRESSURE_LF]\nVALUE=25\n");
    var service = (AssettoCorsaSetupService)RuntimeHelpers.GetUninitializedObject(typeof(AssettoCorsaSetupService));
    var analysis = service.LoadBaseline(path, ValidInput().Car);
    analysis.Parameters[0].RecommendedValue = 26;
    File.WriteAllText(path, "[PRESSURE_LF]\nVALUE=30\n");
    var refused = false;
    try { service.WriteGenerated(analysis, Path.Combine(root, "stale.ini")); } catch (InvalidDataException) { refused = true; }
    Assert(refused, "A recommendation was saved against a changed baseline.");
});
Run("share import refuses ambiguous calibration identities", () =>
{
    var input = ValidInput();
    var service = new ShareCodeService();
    var payload = service.Create(input, new TuningEngine().Generate(input), new CarBehaviorTarget());
    payload.Input.Car.Id = "car|other";
    var refused = false;
    try { service.Encode(payload); } catch (InvalidDataException) { refused = true; }
    Assert(refused, "Share accepted an ID that the calibration engine rejects.");
});
Run("telemetry history refuses unsupported schema", () =>
{
    var path = Path.Combine(root, "unsupported-session.json");
    File.WriteAllText(path, "{\"session\":{\"Schema\":\"future-format\",\"Samples\":[]}}");
    Assert(new TelemetrySessionStore().TryLoad(path) is null, "Unknown session format was accepted.");
});
Run("reopening a frozen packet does not mark it fresh", () =>
{
    using var hub = new TelemetryHubService();
    var reader = (AssettoCorsaTelemetryReader)Get(hub, "_reader")!;
    using var map = MemoryMappedFile.CreateNew(null, 4096);
    Set(reader, "_physicsMap", map);
    Invoke(hub, "ReadOnceLocked");
    Invoke(hub, "HandleReadFailureLocked", "simulated restart");
    using var reopened = MemoryMappedFile.CreateNew(null, 4096);
    Set(Get(hub, "_reader")!, "_physicsMap", reopened);
    Invoke(hub, "ReadOnceLocked");
    Assert(Get(hub, "_latest") is null, "Reopened frozen packet was accepted as new telemetry.");
});
Run("unchanged setup generates a new file and preserves baseline", () =>
{
    var path = Path.Combine(root, "unchanged.ini");
    const string original = "[PRESSURE_LF]\nVALUE=25\n[OTHER]\nVALUE=7\n";
    File.WriteAllText(path, original);
    var service = (AssettoCorsaSetupService)RuntimeHelpers.GetUninitializedObject(typeof(AssettoCorsaSetupService));
    var analysis = service.LoadBaseline(path, ValidInput().Car);
    analysis.Parameters[0].RecommendedValue = 26;
    var output = service.WriteGenerated(analysis, Path.Combine(root, "generated.ini"));
    Assert(File.ReadAllText(path) == original && File.ReadAllText(output).Contains("VALUE=26"), "Valid setup save failed.");
});
Run("calibration backup recovers after primary corruption", () =>
{
    var dir = Path.Combine(root, "calibration");
    Directory.CreateDirectory(dir);
    var store = (CalibrationStore)RuntimeHelpers.GetUninitializedObject(typeof(CalibrationStore));
    var path = Path.Combine(dir, "calibrations.json");
    Set(store, "_directory", dir);
    Set(store, "_path", path);
    Set(store, "_backupPath", Path.Combine(dir, "calibrations.backup.json"));
    var key = new CalibrationEngine().BuildKey(ValidInput());
    store.Upsert(new CalibrationProfile { Key = key, WheelSpeedDelta = 2 });
    store.Upsert(new CalibrationProfile { Key = key, WheelSpeedDelta = 4 });
    File.WriteAllText(path, "broken-json");
    Assert(store.Get(key) is not null, "Valid calibration backup was not recovered.");
    Assert(File.ReadAllText(path) == "broken-json", "Read unexpectedly overwrote corruption evidence.");
});
Run("AZOM source guard rejects stale values and accepts target no-op", () =>
{
    var method = typeof(AzomLiveController).GetMethod("ValidateAuthoritativeSourceState", BindingFlags.NonPublic | BindingFlags.Static)!;
    var plan = new List<AzomApplyPlanItem> { new() { PropertyName = "AZOM.Torque", Kind = AzomApplyItemKind.Numeric, CurrentInt = 80, TargetInt = 70 } };
    var refused = false;
    try { method.Invoke(null, new object[] { plan, new AzomLiveSnapshot { Torque = 90 } }); }
    catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { refused = true; }
    Assert(refused, "Stale AZOM source state was accepted.");
    method.Invoke(null, new object[] { plan, new AzomLiveSnapshot { Torque = 70 } });
});
IntelligenceChecks.Run(Run, root);
Console.WriteLine($"Failures: {failures}. Isolated fixtures: {root}");
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
static object? Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

static TuneInput ValidInput() => new()
{
    Hardware = BuiltInProfiles.Hardware()[3],
    Wheel = BuiltInProfiles.Wheels()[0],
    DriftPack = BuiltInProfiles.DriftPacks()[0],
    Car = BuiltInProfiles.Cars()[0],
    Intent = BuiltInProfiles.Intents()[1]
};
