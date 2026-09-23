using System.Globalization;
using System.IO;
using System.Text.Json;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class SetupComparisonChecks
{
    public static void Run(Action<string, Action> run, string root)
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        run("setup comparison preserves recommendation values, engine output and the baseline", () =>
        {
            var fixture = new CarTestFixture(root);
            var baseline = new AssettoCorsaSetupService().LoadBaseline(fixture.Baseline, fixture.Input.Car);
            var generated = new CarSetupTuningEngine().Generate(fixture.Input, baseline, SetupAggressiveness.Balanced, fixture.Report.SuggestedBehaviorTarget);
            var before = JsonSerializer.Serialize(generated); var report = JsonSerializer.Serialize(fixture.Report); var file = File.ReadAllText(fixture.Baseline);
            var rows = SetupComparisonPresentation.Proposed(generated, true);
            Check(rows.Count == generated.Parameters.Count && rows.Count(r => r.Changed) == generated.ChangedCount, "Comparison omitted or invented a proposed change");
            foreach (var parameter in generated.Parameters)
            {
                var row = rows.Single(r => r.Key == parameter.Section);
                Check(row.Changed == parameter.Changed && row.Explanation.Contains(parameter.Reason), "Recommendation rationale or status changed");
            }
            Check(JsonSerializer.Serialize(generated) == before && JsonSerializer.Serialize(fixture.Report) == report && File.ReadAllText(fixture.Baseline) == file, "Rendering mutated inputs");
            var pressure = rows.Single(r => r.Key == "PRESSURE_LR");
            Check(pressure.Setting == "Tyre pressure — rear left" && pressure.Before == "28 psi" && pressure.After.EndsWith(" psi"), "Verified direct units missing");
        });
        run("setup comparison includes all unchanged controls and distinguishes an ungenerated baseline", () =>
        {
            var fixture = new CarTestFixture(root);
            var a = new AssettoCorsaSetupService().LoadBaseline(fixture.Baseline, fixture.Input.Car);
            var rows = SetupComparisonPresentation.Proposed(a, false);
            Check(rows.Count == a.Parameters.Count && rows.All(r => !r.Changed && r.After == "Not generated"), "Ungenerated setup claimed a recommendation");
            Check(rows.Any(r => r.Key == "ECU_MAP") && rows.Any(r => r.Key == "FUEL"), "Unknown/unchanged controls hidden from full list");
        });
        run("setup comparison uses verified camber units and keeps unsupported ranges raw", () =>
        {
            var a = new CarSetupAnalysis { Parameters = [new() { Section = "CAMBER_LF", CurrentRaw = "20", CurrentValue = 20, RecommendedValue = 21,
                Range = new() { Section = "CAMBER_LF", CamberValueMode = 2, Min = -5, Max = 0 } },
                new() { Section = "MOD_CONTROL", CurrentRaw = "1", CurrentValue = 1, RecommendedValue = 2, Range = new() { Units = "Nm", ShowClicks = true } }] };
            var rows = SetupComparisonPresentation.Proposed(a, true);
            Check(rows.Single(r => r.Key == "CAMBER_LF").Before == "-3 setup units", "Camber serialized count displayed as degrees");
            Check(rows.Single(r => r.Key == "MOD_CONTROL").After == "2 (saved value)" && !rows.Single(r => r.Key == "MOD_CONTROL").Difference.Contains("Nm"), "Unsupported units inferred");
        });
        run("recorded setup comparison uses union of settings with explicit missing evidence", () =>
        {
            var a = new TuneVersion { Settings = new() { ["ACSetup.PRESSURE_LR"] = 28, ["ACSetup.FUEL"] = 30, ["ACSetup.OLD"] = 7 } };
            var b = new TuneVersion { Settings = new() { ["ACSetup.PRESSURE_LR"] = 26, ["ACSetup.FUEL"] = 30, ["ACSetup.NEW"] = 9 } };
            var rows = SetupComparisonPresentation.Recorded(a, b);
            Check(rows.Count == 4 && rows.Count(r => r.Changed) == 1 && rows.Count(r => r.Unavailable) == 2, "Lost unchanged/missing settings or fabricated deltas");
            Check(rows.Single(r => r.Key == "ACSetup.NEW").Before == "Not captured" && rows.Single(r => r.Key == "ACSetup.OLD").Difference == "Cannot compare", "Missing value treated as zero");
            Check(SetupComparisonPresentation.Recorded(null, b).All(r => r.Unavailable && !r.Changed), "Legacy missing snapshot guessed");
            Check(SetupComparisonPresentation.Recorded(null, null).Count == 0, "Empty history invented values");
        });
        run("recorded setup comparison uses each snapshot's own verified mapping", () =>
        {
            var a = new TuneVersion { Settings = new() { ["ACSetup.FINAL_RATIO"] = 0, ["ACSetup.ENGINE_MAPS"] = 1 } };
            var b = new TuneVersion { Settings = new() { ["ACSetup.FINAL_RATIO"] = 1, ["ACSetup.ENGINE_MAPS"] = 2 } };
            a.DecodedSetup.Add(new("FINAL_RATIO", "0", DecodedSetupSetting.Verified, "3.9:1", "old list", ""));
            b.DecodedSetup.Add(new("FINAL_RATIO", "1", DecodedSetupSetting.Verified, "4.3:1", "new list", ""));
            b.DecodedSetup.Add(new("ENGINE_MAPS", "1", DecodedSetupSetting.Verified, "Race fuel", "stale map", ""));
            var original = JsonSerializer.Serialize(new[] { a, b });
            var rows = SetupComparisonPresentation.Recorded(a, b);
            Check(rows.Single(r => r.Key == "ACSetup.FINAL_RATIO").Before.Contains("3.9:1") && rows.Single(r => r.Key == "ACSetup.FINAL_RATIO").After.Contains("4.3:1"), "Snapshot mappings mixed");
            Check(!rows.Single(r => r.Key == "ACSetup.ENGINE_MAPS").After.Contains("Race fuel"), "Stale decoded label reused for a different saved value");
            Check(JsonSerializer.Serialize(new[] { a, b }) == original, "Snapshots mutated");
            b.DecodedSetup.Add(b.DecodedSetup[0]);
            Check(!SetupComparisonPresentation.Recorded(a, b).Single(r => r.Key == "ACSetup.FINAL_RATIO").After.Contains("4.3:1"), "Ambiguous duplicate decode accepted");
        });
        run("partial ECU interpretations and proposed changed indices never become verified labels", () =>
        {
            var partial = new DecodedSetupSetting("ENGINE_MAPS", "1", DecodedSetupSetting.Partial, "Maybe race fuel", "mod", "");
            var a = new CarSetupAnalysis { Physics = new() { DecodedSettings = [partial with { Status = DecodedSetupSetting.Verified }] },
                Parameters = [new() { Section = "ENGINE_MAPS", CurrentRaw = "1", CurrentValue = 1, RecommendedValue = 2 }] };
            var row = SetupComparisonPresentation.Proposed(a, true).Single();
            Check(row.Before.Contains("Maybe race fuel") && !row.After.Contains("Maybe race fuel"), "Baseline map applied to proposed index");
            var tune = new TuneVersion { Settings = new() { ["ACSetup.ENGINE_MAPS"] = 1 }, DecodedSetup = [partial] };
            Check(!SetupComparisonPresentation.Recorded(tune, tune).Single().Before.Contains("Maybe"), "Partial map represented as verified");
        });
        run("same saved index with a different verified ratio remains visible for review", () =>
        {
            var a = new TuneVersion { Settings = new() { ["ACSetup.FINAL_RATIO"] = 0 }, DecodedSetup = [new("FINAL_RATIO", "0", DecodedSetupSetting.Verified, "3.9:1", "file", "", 3.9)] };
            var b = new TuneVersion { Settings = new() { ["ACSetup.FINAL_RATIO"] = 0 }, DecodedSetup = [new("FINAL_RATIO", "0", DecodedSetupSetting.Verified, "4.3:1", "file", "", 4.3)] };
            var row = SetupComparisonPresentation.Recorded(a, b).Single();
            Check(row.Emphasized && !row.Changed && row.Status == "Mapping differs", "Changed physics meaning hidden as unchanged");
            b.Settings["ACSetup.FINAL_RATIO"] = 1; b.DecodedSetup[0] = b.DecodedSetup[0] with { SavedValue = "1" };
            Check(SetupComparisonPresentation.Recorded(a, b).Single().Difference == "+0.4 ratio", "Verified ratio difference shown as index delta");
        });
        run("FFB targets and invalid snapshot values are not described as measured setup changes", () =>
        {
            var a = new TuneVersion { Settings = new() { ["Generated.PitHouse.ffbStrength"] = 70, ["ACSetup.FUEL"] = double.NaN } };
            var b = new TuneVersion { Settings = new() { ["Generated.PitHouse.ffbStrength"] = 80, ["ACSetup.FUEL"] = 30 } };
            var rows = SetupComparisonPresentation.Recorded(a, b);
            var ffb = rows.Single(r => r.Key.StartsWith("Generated"));
            Check(ffb.Category == "FFB targets" && ffb.AfterHeading == "After target" && ffb.Explanation.Contains("not wheelbase"), "FFB target called readback");
            Check(rows.Single(r => r.Key == "ACSetup.FUEL").Unavailable, "Nonfinite value treated as usable evidence");
        });
        run("setup comparison is culture-safe without modifying stored numbers", () =>
        {
            var savedCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                var tune = new TuneVersion { Settings = new() { ["ACSetup.FINAL_RATIO"] = 1 }, DecodedSetup = [new("FINAL_RATIO", "1.0", DecodedSetupSetting.Verified, "4.3:1", "file", "")] };
                Check(SetupComparisonPresentation.Recorded(tune, tune).Single().Before.Contains("4.3:1"), "Invariant snapshot parsing broke on comma locale");
            }
            finally { CultureInfo.CurrentCulture = savedCulture; }
        });
    }
}
