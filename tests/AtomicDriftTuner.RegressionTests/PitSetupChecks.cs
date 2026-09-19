using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class PitSetupChecks
{
    public static void Run(Action<string, Action> test, string root)
    {
        test("pit plan snapshots only approved numeric changes without writing files", () =>
        {
            var f = Fixture(root);
            var original = File.ReadAllBytes(f.Path);
            var files = Directory.GetFiles(f.Directory, "*", SearchOption.AllDirectories);
            var plan = f.Create();
            Check(plan.ProtocolVersion == 1 && Guid.TryParseExact(plan.PlanId, "N", out _) && plan.CarId == "test_car", "Invalid protocol identity");
            Check(plan.Label == "Pit test" && plan.Changes.Count == 1 && plan.Changes[0].Section == "PRESSURE_LF" &&
                plan.Changes[0].Before == 24 && plan.Changes[0].After == 25, "Incorrect approved change");
            Check(plan.BaselineValues.Count == 3 && plan.BaselineValues["CAMBER_LF"] == -3.5 &&
                plan.BaselineValues["FUEL"] == 25, "Unchanged baseline values omitted");
            Check(File.ReadAllBytes(f.Path).SequenceEqual(original) &&
                Directory.GetFiles(f.Directory, "*", SearchOption.AllDirectories).SequenceEqual(files), "Staging wrote a setup file");
            var json = JsonSerializer.Serialize(plan);
            using var parsed = JsonDocument.Parse(json);
            Check(parsed.RootElement.GetProperty("protocolVersion").GetInt32() == 1 &&
                parsed.RootElement.GetProperty("baselineValues").GetProperty("PRESSURE_LF").GetDouble() == 24 &&
                !json.Contains("BaselinePath", StringComparison.Ordinal) && !json.Contains("PRIVATE", StringComparison.Ordinal) &&
                !json.Contains(f.Directory, StringComparison.Ordinal), "Plan leaked paths or did not use the wire contract");
            Check(f.Create().PlanId != plan.PlanId, "New plan reused a prior proposal ID");
            using var apiJson = JsonDocument.Parse(JsonSerializer.Serialize(plan, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            }));
            Check(apiJson.RootElement.GetProperty("baselineValues").GetProperty("PRESSURE_LF").GetDouble() == 24,
                "API dictionary naming policy changed a raw INI section identifier");
        });

        test("pit plan keeps an immutable snapshot after analysis and source mutation", () =>
        {
            var f = Fixture(root); var plan = f.Create();
            f.Analysis.Parameters[0].CurrentValue = 99;
            f.Analysis.Parameters[0].RecommendedValue = 100;
            f.Analysis.Parameters[0].Range!.Min = 99;
            f.Analysis.Parameters.Clear();
            f.Analysis.CarFolderName = "other_car";
            File.WriteAllText(f.Path, "[FUEL]\nVALUE=99");
            Check(plan.CarId == "test_car" && plan.Changes[0].Before == 24 && plan.Changes[0].After == 25 &&
                plan.BaselineValues["FUEL"] == 25, "Mutable desktop state changed a staged proposal");
            var refused = false;
            try { ((IDictionary<string, double>)plan.BaselineValues)["FUEL"] = 99; }
            catch (NotSupportedException) { refused = true; }
            Check(refused, "Plan baseline is mutable");
            refused = false;
            try { ((IList<PitSetupChange>)plan.Changes).Clear(); }
            catch (NotSupportedException) { refused = true; }
            Check(refused, "Plan changes are mutable");
        });

        test("pit plan rejects a stale source including unrelated numeric changes", () =>
        {
            foreach (var change in new Func<string, string>[]
            {
                s => s.Replace("VALUE=24", "VALUE=26", StringComparison.Ordinal),
                s => s.Replace("[FUEL]\nVALUE=25", "[FUEL]\nVALUE=26", StringComparison.Ordinal),
                s => s.Replace("[FUEL]\nVALUE=25\n", "", StringComparison.Ordinal),
                s => s + "\n[ABS]\nVALUE=1\n"
            })
            {
                var f = Fixture(root);
                File.WriteAllText(f.Path, change(File.ReadAllText(f.Path)));
                Reject(() => f.Create());
            }
        });

        test("pit plan canonicalizes sections and compares values independently of formatting", () =>
        {
            var f = Fixture(root);
            File.WriteAllText(f.Path, "[car]\nMODEL=TEST_CAR\n[fuel]\nvalue = 2.5e1\n[camber_lf]\nVALUE=-3.50\n[pressure_lf]\nVALUE=24.000\n; different label\n");
            foreach (var parameter in f.Analysis.Parameters)
            {
                parameter.Section = parameter.Section.ToLowerInvariant();
                if (parameter.Range is not null) parameter.Range.Section = parameter.Section;
            }
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Check(f.Create().Changes[0].Section == "PRESSURE_LF", "Culture or raw formatting changed plan semantics");
            }
            finally { CultureInfo.CurrentCulture = culture; }
        });

        test("pit plan rejects duplicate malformed and unsafe source sections", () =>
        {
            foreach (var ini in new[]
            {
                "[PRESSURE_LF]\nVALUE=24\n[pressure_lf]\nVALUE=24",
                "[PRESSURE_LF]\nVALUE=24\nvalue=24", "[PRESSURE_LF]\nVALUE=24\n[PRESSURE_LF]\nNAME=x",
                "[../PRESSURE_LF]\nVALUE=24", "[PRESSURE_LF] trailing\nVALUE=24", "VALUE=24",
                "[PRESSURE_LF]\nVALUE=NaN", "[PRESSURE_LF]\nVALUE=1e-999",
                "[CAR]\nMODEL=test_car\nMODEL=test_car\n[PRESSURE_LF]\nVALUE=24"
            })
            {
                var f = Fixture(root); File.WriteAllText(f.Path, ini); Reject(() => f.Create());
            }
        });

        test("pit plan rejects mismatched unknown or unsafe car identity", () =>
        {
            foreach (var identity in new[] { "other_car", "unknown-car", "../car", "car/part", "car\n", " test_car" })
            {
                var f = Fixture(root); Reject(() => f.Service.Create(f.Analysis, identity, "Pit test"));
            }
            var source = Fixture(root);
            File.WriteAllText(source.Path, File.ReadAllText(source.Path).Replace("MODEL=test_car", "MODEL=other_car", StringComparison.Ordinal));
            Reject(() => source.Create());
            var noMetadata = Fixture(root);
            File.WriteAllText(noMetadata.Path, File.ReadAllText(noMetadata.Path).Replace("[CAR]\nMODEL=test_car\n", "", StringComparison.Ordinal));
            noMetadata.Create();
        });

        test("pit plan rejects mutated nonfinite and colliding analysis values", () =>
        {
            foreach (var mutate in new Action<CarSetupAnalysis>[]
            {
                a => a.Parameters[0].CurrentValue = 23,
                a => a.Parameters[0].CurrentRaw = "23",
                a => a.Parameters[0].CurrentRaw = "NaN",
                a => a.Parameters[0].CurrentValue = double.NaN,
                a => a.Parameters[0].RecommendedValue = double.PositiveInfinity,
                a => a.Parameters[2].RecommendedValue = null,
                a => a.Parameters[0].RecommendedValue = 1e13,
                a => a.Parameters[0].Section = "PRESSURE-LF",
                a => a.Parameters[0].Section = "PRÉSSURE_LF",
                a => a.Parameters[0].Section = new string('A', 129),
                a => a.Parameters[1].Section = "pressure_lf",
                a => a.Parameters[1] = null!,
                a => a.Parameters.RemoveAt(2),
                a => a.CarFolderName = "other_car"
            })
            {
                var f = Fixture(root); mutate(f.Analysis); Reject(() => f.Create());
            }
        });

        test("pit plan requires supported range and legal raw VALUE steps", () =>
        {
            foreach (var mutate in new Action<CarSetupParameter>[]
            {
                p => p.Range = null,
                p => p.Range!.ShowClicks = true,
                p => p.Range!.Section = "PRESSURE_RF",
                p => p.Range!.Min = null,
                p => p.Range!.Max = null,
                p => p.Range!.Step = null,
                p => p.Range!.Min = double.NaN,
                p => p.Range!.Max = double.PositiveInfinity,
                p => p.Range!.Min = 41,
                p => p.Range!.Step = 0,
                p => p.Range!.Step = -1,
                p => p.Range!.Step = double.NaN,
                p => p.Range!.Min = 24.5,
                p => p.Range!.Step = 3,
                p => p.RecommendedValue = 41,
                p => p.RecommendedValue = 25.5,
                p => { p.Range!.Step = .000001; p.RecommendedValue = 25.123456; }
            })
            {
                var f = Fixture(root); mutate(f.Analysis.Parameters[0]); Reject(() => f.Create());
            }
            var legal = Fixture(root);
            legal.Analysis.Parameters[1].RecommendedValue = -3.2;
            var plan = legal.Create();
            Check(plan.Changes.Count == 2 && plan.Changes[0].After == -3.2, "A legal fractional step was rejected");
        });

        test("pit plan bounds baseline bytes parameters and change count", () =>
        {
            var f = Fixture(root);
            var text = new StringBuilder(File.ReadAllText(f.Path));
            while (Encoding.UTF8.GetByteCount(text.ToString()) < PitSetupPlanService.MaximumBaselineBytes)
            {
                var remaining = PitSetupPlanService.MaximumBaselineBytes - Encoding.UTF8.GetByteCount(text.ToString());
                if (remaining == 1) text.Append('\n');
                else text.Append(';').Append('x', Math.Min(remaining - 2, 1000)).Append('\n');
            }
            File.WriteAllText(f.Path, text.ToString()); f.Create();
            File.AppendAllText(f.Path, "\n"); Reject(() => f.Create());
            f = Fixture(root); File.WriteAllBytes(f.Path, [0xff, 0xfe, 0xff]); Reject(() => f.Create());

            f = Fixture(root); f.Analysis.Parameters.Clear();
            var source = new StringBuilder("[CAR]\nMODEL=test_car\n");
            for (var i = 0; i < 512; i++)
            {
                var section = "ITEM_" + i;
                source.Append('[').Append(section).Append("]\nVALUE=10\n");
                f.Analysis.Parameters.Add(new CarSetupParameter
                {
                    Section = section, CurrentRaw = "10", CurrentValue = 10, RecommendedValue = i < 64 ? 11 : 10,
                    Range = new SetupRangeDefinition { Section = section, Min = 0, Max = 20, Step = 1 }
                });
            }
            File.WriteAllText(f.Path, source.ToString());
            var bounded = f.Create();
            Check(bounded.BaselineValues.Count == 512 && bounded.Changes.Count == 64, "Valid protocol boundary refused");
            f.Analysis.Parameters[64].RecommendedValue = 11; Reject(() => f.Create());
            f.Analysis.Parameters[64].RecommendedValue = 10;
            f.Analysis.Parameters.Add(new CarSetupParameter { Section = "EXTRA", CurrentRaw = "10", CurrentValue = 10, RecommendedValue = 10 });
            File.AppendAllText(f.Path, "[EXTRA]\nVALUE=10\n"); Reject(() => f.Create());
        });

        test("pit plan rejects empty proposals and invalid labels", () =>
        {
            var f = Fixture(root);
            foreach (var label in new[] { "", " ", "Pit\nlabel", new string('L', 161) })
                Reject(() => f.Service.Create(f.Analysis, "test_car", label));
            f.Analysis.Parameters[0].RecommendedValue = 24; Reject(() => f.Create());
            f.Analysis.Parameters.Clear(); Reject(() => f.Create());
            Check(PitSetupPlanService.NumbersEqual(24, 24.0000005) &&
                !PitSetupPlanService.NumbersEqual(24, 24.000002) &&
                !PitSetupPlanService.NumbersEqual(double.NaN, double.NaN), "Protocol numeric comparison changed");
        });
    }

    private static FixtureData Fixture(string root)
    {
        var directory = Path.Combine(root, "pit-plan-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(directory, "car", "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "setup.ini"),
            "[PRESSURE_LF]\nMIN=20\nMAX=40\nSTEP=1\n[CAMBER_LF]\nMIN=-5\nMAX=0\nSTEP=0.1\n");
        var path = Path.Combine(directory, "baseline.ini");
        File.WriteAllText(path, "[CAR]\nMODEL=test_car\n[PRESSURE_LF]\nVALUE=24\n[CAMBER_LF]\nVALUE=-3.5\n[FUEL]\nVALUE=25\n[METADATA]\nNAME=PRIVATE_LABEL\n");
        var analysis = new AssettoCorsaSetupService().LoadBaseline(path,
            new CarProfile { SourceFolderName = "test_car", SourceFolderPath = Path.Combine(directory, "car") });
        analysis.Parameters[0].RecommendedValue = 25;
        return new FixtureData(directory, path, analysis, new PitSetupPlanService());
    }

    private sealed record FixtureData(string Directory, string Path, CarSetupAnalysis Analysis, PitSetupPlanService Service)
    {
        public PitSetupPlan Create() => Service.Create(Analysis, "test_car", "Pit test");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Unsafe or stale pit proposal was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
