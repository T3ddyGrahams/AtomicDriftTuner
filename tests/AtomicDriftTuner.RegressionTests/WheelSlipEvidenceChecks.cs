using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Services;

internal static class WheelSlipEvidenceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        foreach (var kind in new[] { "front-nan", "rear-infinity", "rear-range", "front-range", "source-flag" })
            test("invalid optional wheel-slip preserves phases without fabricating grip: " + kind, () =>
            {
                var s = IntelligenceChecks.Session();
                foreach (var f in s.Samples)
                {
                    if (kind == "front-nan") f.FrontWheelSlipAvg = double.NaN;
                    if (kind == "rear-infinity") f.RearWheelSlipAvg = double.PositiveInfinity;
                    if (kind == "rear-range") f.RearWheelSlipAvg = 10001;
                    if (kind == "front-range") f.FrontWheelSlipAvg = -10001;
                    if (kind == "source-flag") { f.InvalidWheelSlipSignals = true; f.FrontWheelSlipAvg = f.RearWheelSlipAvg = 0; }
                }
                var a = new TelemetryAnalyzer().Analyze(s);
                Check(a.DriftEntries == 4 && a.TransitionCount == 8, "Wheel-slip corrupted motion phases");
                Check(a.Diagnosis.InvalidSamples == 0 && a.Diagnosis.InvalidWheelSlipSamples == s.Samples.Count, "Wrong quality counters");
                Check(a.Diagnosis.Metric("front-slip-share")!.Value is null && a.Diagnosis.Metric("rear-slip-share")!.Value is null, "Missing grip became a score");
                Check(double.IsFinite(a.AverageFrontWheelSlipWhileDrifting) && double.IsFinite(a.AverageRearWheelSlipWhileDrifting), "Nonfinite output escaped");
                JsonSerializer.Serialize(a); // Saved reports must remain serializable.
            });
        test("wheel-slip spikes at initiation confirmation retain complete entries and valid axle evidence", () =>
        {
            var s = IntelligenceChecks.Session();
            foreach (var f in s.Samples.Where(f => f.TimeSeconds % 25 is >= 5.68 and <= 5.9)) f.RearWheelSlipAvg = 25000;
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.DriftEntries == 4 && a.TransitionCount == 8 && a.Diagnosis.Metric("initiation")!.Value > 0, "Entry lost at confirmation");
            Check(Math.Abs(a.Diagnosis.Metric("front-slip-share")!.Value!.Value - 25) < .001, "Bad slip leaked into grip ratio");
            Check(Math.Abs(a.AverageRearWheelSlipWhileDrifting - 3) < .000001 && a.Diagnosis.QualityNotes.Any(n => n.Contains("unusable wheel-slip")), "Bad slip average or missing explanation");
        });
        test("motion failure at initiation confirmation still breaks evidence", () =>
        {
            var s = IntelligenceChecks.Session();
            foreach (var f in s.Samples.Where(f => f.TimeSeconds % 25 is >= 5.68 and <= 5.9)) f.InvalidSourceSignals = true;
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.DriftEntries == 0 && a.Diagnosis.InvalidSamples > 0, "Invalid motion was bridged");
        });
        test("one detected initiation remains insufficient for timing", () =>
        {
            var s = IntelligenceChecks.Session(); s.Samples.RemoveAll(f => f.TimeSeconds >= 25);
            var a = new TelemetryAnalyzer().Analyze(s);
            Check(a.DriftEntries == 1 && a.Diagnosis.Metric("initiation")!.Value is null, "Evidence requirement weakened");
            Check(a.Diagnosis.QualityNotes.Any(n => n.Contains("three complete entries; 1 detected")), "Missing actionable count");
        });
        test("reader separates sanitized wheel-slip failure from invalid motion and preserves flag", () =>
        {
            using var reader = new AssettoCorsaTelemetryReader(); using var map = MemoryMappedFile.CreateNew(null, 4096);
            typeof(AssettoCorsaTelemetryReader).GetField("_physicsMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(reader, map);
            var type = typeof(AssettoCorsaTelemetryReader).GetNestedType("AcPhysics", BindingFlags.NonPublic)!;
            using var view = map.CreateViewAccessor();
            view.Write(Marshal.OffsetOf(type, "WheelSlip").ToInt64() + 8, float.NaN);
            var frame = reader.Read(1);
            Check(frame.InvalidWheelSlipSignals && !frame.InvalidSourceSignals, "Optional bad signal poisons motion");
            var clone = JsonSerializer.Deserialize<AtomicDriftTuner.Models.TelemetrySample>(JsonSerializer.Serialize(frame))!;
            Check(clone.InvalidWheelSlipSignals && clone.Copy().InvalidWheelSlipSignals, "Sanitization flag lost on save/copy");
            view.Write(Marshal.OffsetOf(type, "SpeedKmh").ToInt64(), float.NaN);
            Check(reader.Read(2).InvalidSourceSignals, "Invalid core signal accepted");
        });
    }
}
