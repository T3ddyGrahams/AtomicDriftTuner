using System.Globalization;
using System.IO;
using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

internal static class SetupDecodingChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static CarSetupParameter Param(string section, string value) => new()
    {
        Section = section, CurrentRaw = value,
        CurrentValue = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null
    };
    private static IReadOnlyList<DecodedSetupSetting> Decode(Dictionary<string, string> files, params CarSetupParameter[] baseline) =>
        new CarSetupDecoder().Decode(baseline, name => files.GetValueOrDefault(name));
    internal static Dictionary<string, string> Fixture() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["setup.ini"] = "[FINAL_GEAR_RATIO]\nRATIOS=final.rto\nHELP=\n[GEARS]\nUSE_GEARSET=0\n[GEAR_3]\nRATIOS=third.rto\n[ENGINE_MAPS]\nNAME=ECU tune\nLUT=maps.lut\n",
        ["final.rto"] = "Long|3.9\n4.30 label|4.3\nMisleading 6.00 label|3.7\n",
        ["third.rto"] = "Tall|1.3\nShort|1.8\n",
        ["maps.lut"] = "Stock|0\nTuned|1\nRace Fuel|2\n",
        ["engine.ini"] = "[MAP]\nDEFAULT=0\nMAP_0=stock.lut\nMAP_1=tuned.lut\nMAP_2=race.lut\n",
        ["stock.lut"] = "0|1\n8000|1\n",
        ["tuned.lut"] = "0|1\n4000|1.1\n8000|1.1\n",
        ["race.lut"] = "0|1\n4000|1.2\n8000|1.3\n"
    };
    public static void Run(Action<string, Action> test, string root)
    {
        test("setup decoding resolves final drive by file order and preserves the saved value", () =>
        {
            var p = Param("FINAL_RATIO", "2"); var rows = Decode(Fixture(), p); var row = rows.Single();
            Check(row.Status == DecodedSetupSetting.Verified && row.NumericValue == 3.7 && row.Value.Contains("6.00 label"), "Ratio sorted or label mistaken for value");
            Check(row.Source.Contains("final.rto") && row.Explanation.Contains("file order") && row.SavedValue == "2", "Source or saved index missing");
            Check(p.CurrentRaw == "2" && p.CurrentValue == 2 && p.RecommendedValue is null, "Decode mutated setup");
        });
        test("setup decoding maps AC internal forward gear indexes and refuses a guessed alias", () =>
        {
            var rows = Decode(Fixture(), Param("INTERNAL_GEAR_4", "1"), Param("GEAR_3", "1"), Param("INTERNAL_GEAR_1", "0"));
            Check(rows[0].Status == DecodedSetupSetting.Verified && rows[0].NumericValue == 1.8 && rows[0].Source.Contains("GEAR_3"), "Reverse offset lost");
            Check(rows.Skip(1).All(r => r.Status == DecodedSetupSetting.Unsupported), "Unknown gear alias inferred");
        });
        test("setup decoding identifies explicitly enabled gearsets independently of final drive", () =>
        {
            var files = Fixture();
            files["setup.ini"] = files["setup.ini"].Replace("USE_GEARSET=0", "USE_GEARSET=1") + "[GEAR_SET_1]\nNAME=Close box\nGEAR_1=3.1\nGEAR_2=2.1\nGEAR_3=1.7\nGEAR_6=0\n";
            var rows = Decode(files, Param("GEARSET", "1"), Param("FINAL_RATIO", "1"), Param("INTERNAL_GEAR_4", "1"));
            Check(rows[0].Status == DecodedSetupSetting.Verified && rows[0].Value.Contains("Close box") && rows[0].Value.Contains("1.7:1"), "Gearset not mapped");
            Check(rows[1].NumericValue == 4.3 && rows[2].Status == DecodedSetupSetting.Unsupported, "Gearset/individual gear ambiguity accepted");
        });
        test("setup decoding maps named engine selection and reports configured multipliers without power claims", () =>
        {
            var row = Decode(Fixture(), Param("ENGINE_MAPS", "2")).Single();
            Check(row.Status == DecodedSetupSetting.Verified && row.Value.Contains("Race Fuel") && row.Value.Contains("1–1.3×") && row.Value.Contains("8000 RPM"), "Explicit map not decoded");
            Check(row.NumericValue is null && row.Source.Contains("MAP_2") && row.Source.Contains("race.lut") && row.Explanation.Contains("not horsepower"), "Multiplier presented as engine output");
        });
        test("setup decoding supports documented direct engine map indexing and declared bounds", () =>
        {
            var files = Fixture(); files["setup.ini"] = "[ENGINE_MAPS]\nMIN=0\nMAX=2\n";
            Check(Decode(files, Param("ENGINE_MAPS", "1")).Single().Status == DecodedSetupSetting.Verified, "Direct map selection unavailable");
            files["setup.ini"] = "[ENGINE_MAPS]\nMIN=0\nMAX=1\n";
            Check(Decode(files, Param("ENGINE_MAPS", "2")).Single().Status == DecodedSetupSetting.Unsupported, "Out-of-range direct map accepted");
            files["setup.ini"] = "[ENGINE_MAPS]\nMIN=0\nMAX=2\nSTEP=2\n";
            Check(Decode(files, Param("ENGINE_MAPS", "1")).Single().Status == DecodedSetupSetting.Unsupported, "Out-of-step direct map accepted");
            files["setup.ini"] = "[ENGINE_MAPS]\nSHOW_CLICKS=1\n";
            Check(Decode(files, Param("ENGINE_MAPS", "1")).Single().Status == DecodedSetupSetting.Unsupported, "Unverified clicks mapped to direct index");
        });
        test("setup decoding does not guess nonidentity named engine LUT serialization", () =>
        {
            var files = Fixture(); files["maps.lut"] = "Stock|1\nTuned|2\nRace Fuel|0\n";
            var row = Decode(files, Param("ENGINE_MAPS", "2")).Single();
            Check(row.Status == DecodedSetupSetting.Partial && row.Value.Contains("Race Fuel") && row.Value.Contains("Tuned") && row.NumericValue is null, "Ambiguous row-vs-map value guessed");
            Check(row.Explanation.Contains("candidate interpretations"), "Candidate uncertainty hidden");
        });
        test("setup decoding treats numeric ECU LUT inputs as unverified and never interpolates", () =>
        {
            var files = Fixture(); files["maps.lut"] = "0|0\n10|1\n20|2\n";
            var rows = Decode(files, Param("ENGINE_MAPS", "10"));
            Check(rows[0].Status == DecodedSetupSetting.Partial && rows[0].Value.Contains("10 → map 1"), "Exact entry missed or overclaimed");
            Check(Decode(files, Param("ENGINE_MAPS", "15")).Single().Status == DecodedSetupSetting.Unsupported, "Numeric LUT interpolated");
            files["maps.lut"] = "1|0\n1.0|1\n";
            Check(Decode(files, Param("ENGINE_MAPS", "1")).Single().Status == DecodedSetupSetting.Unsupported, "Equivalent duplicate numeric keys accepted");
        });
        test("setup decoding keeps known map selection partial when its curve is missing", () =>
        {
            var files = Fixture(); files.Remove("race.lut");
            var row = Decode(files, Param("ENGINE_MAPS", "2")).Single();
            Check(row.Status == DecodedSetupSetting.Partial && row.Value.Contains("Race Fuel") && row.Explanation.Contains("missing"), "Missing curve invented or known selection lost");
        });
        test("setup decoding keeps unsupported custom controls explicit without reading or running scripts", () =>
        {
            int reads = 0;
            var rows = new CarSetupDecoder().Decode([Param("ECU_MODE", "2"), Param("", "2"), Param("SPRING_RATE_LF", "10")], name => { reads++; throw new Exception("Must not read custom code"); });
            Check(reads == 0 && rows.All(row => row.Status == DecodedSetupSetting.Unsupported) && rows[0].Value == "Saved value 2", "Custom setting guessed or data accessed");
        });
        test("setup decoding isolates malformed sections and unsafe references from valid controls", () =>
        {
            var files = Fixture(); files["setup.ini"] += "[GEAR_3]\nRATIOS=bad.rto\n";
            var rows = Decode(files, Param("INTERNAL_GEAR_4", "0"), Param("FINAL_RATIO", "1"), Param("ENGINE_MAPS", "2"));
            Check(rows[0].Status == DecodedSetupSetting.Unsupported && rows.Skip(1).All(row => row.Status == DecodedSetupSetting.Verified), "One duplicate section blocked other settings");
            foreach (var path in new[] { "../secret.lut", "C:\\secret.lut", "https://host/file.lut", "nested/file.lut", "script.lua", "(|0=0|1=1|)", "file.lut:secret" })
            {
                var f = Fixture(); f["setup.ini"] = f["setup.ini"].Replace("RATIOS=final.rto", "RATIOS=" + path);
                var requested = new List<string>();
                var result = new CarSetupDecoder().Decode([Param("FINAL_RATIO", "0"), Param("ENGINE_MAPS", "2")], name => { requested.Add(name); return f.GetValueOrDefault(name); });
                Check(!requested.Contains(path) && result[0].Status == DecodedSetupSetting.Unsupported && result[1].Status == DecodedSetupSetting.Verified, "Unsafe reference followed or independent decoding lost");
            }
        });
        test("setup decoding contains empty physics headers without inheriting unnamed fields", () =>
        {
            foreach (var header in new[] { "[]", "[ ]", "[\t]" })
            {
                var files = Fixture();
                files["setup.ini"] += header + "\nVALUE=1\nLUT=unknown.lut\n";
                var rows = Decode(files, Param("FINAL_RATIO", "1"), Param("ENGINE_MAPS", "2"));
                Check(rows[0].Status == DecodedSetupSetting.Verified && rows[1].Status == DecodedSetupSetting.Unsupported,
                    "Empty setup header crashed or unnamed values inherited the previous control");

                files = Fixture();
                files["engine.ini"] += header + "\nVALUE=1\nMAP_2=unknown.lut\n";
                rows = Decode(files, Param("FINAL_RATIO", "1"), Param("ENGINE_MAPS", "2"));
                Check(rows[0].Status == DecodedSetupSetting.Verified && rows[1].Status == DecodedSetupSetting.Partial,
                    "Empty engine header crashed or malformed map became verified");

                files = Fixture();
                files["setup.ini"] = header + "\nVALUE=999\n" + files["setup.ini"];
                files["engine.ini"] = header + "\nMAP_2=unknown.lut\n" + files["engine.ini"];
                rows = Decode(files, Param("FINAL_RATIO", "1"), Param("ENGINE_MAPS", "2"));
                Check(rows.All(row => row.Status == DecodedSetupSetting.Verified),
                    "Leading unnamed fields prevented independent well-formed sections from decoding");
            }
        });
        test("setup decoding refuses malformed ratios without changing valid engine interpretation", () =>
        {
            foreach (var text in new[] { "", "Same|4\nSame|5", "One|NaN", "One|Infinity", "One|-4", "One|0", "One|4|extra", "One|4\nbad row", "One|4,30" })
            {
                var files = Fixture(); files["final.rto"] = text;
                var rows = Decode(files, Param("FINAL_RATIO", "0"), Param("ENGINE_MAPS", "2"));
                Check(rows[0].Status == DecodedSetupSetting.Unsupported && rows[1].Status == DecodedSetupSetting.Verified, "Bad ratio accepted or independently valid map lost: " + text);
            }
        });
        test("setup decoding refuses fractional nonfinite negative and out-of-range saved indexes", () =>
        {
            foreach (var value in new[] { "-1", "0.5", "NaN", "Infinity", "3", "999999", "not a number" })
                Check(Decode(Fixture(), Param("FINAL_RATIO", value)).Single().Status == DecodedSetupSetting.Unsupported, "Invalid saved index accepted: " + value);
        });
        test("setup decoding rejects duplicate baseline controls and preserves input order", () =>
        {
            var rows = Decode(Fixture(), Param("FINAL_RATIO", "0"), Param("ENGINE_MAPS", "2"), Param("final_ratio", "1"));
            Check(rows.Count == 3 && rows[0].Status == DecodedSetupSetting.Unsupported && rows[2].Status == DecodedSetupSetting.Unsupported && rows[1].Status == DecodedSetupSetting.Verified, "Repeated baseline control guessed");
        });
        test("setup decoding invalid torque LUT remains partial and cannot become a horsepower estimate", () =>
        {
            foreach (var text in new[] { "4000|1\n4000|1.2", "8000|1\n0|1", "0|NaN", "0|-0.5", "0|Infinity", "not a curve", "" })
            {
                var files = Fixture(); files["race.lut"] = text;
                var row = Decode(files, Param("ENGINE_MAPS", "2")).Single();
                Check(row.Status == DecodedSetupSetting.Partial && row.NumericValue is null && row.Value.Contains("effect unavailable"), "Invalid curve got verified: " + text);
            }
        });
        test("setup decoding contains read failures and bounds text without discarding independent results", () =>
        {
            var files = Fixture();
            var rows = new CarSetupDecoder().Decode([Param("FINAL_RATIO", "0"), Param("ENGINE_MAPS", "2")], name => name == "final.rto" ? throw new IOException("Fixture read unavailable") : files.GetValueOrDefault(name));
            Check(rows[0].Status == DecodedSetupSetting.Unsupported && rows[1].Status == DecodedSetupSetting.Verified, "Read error escaped or poisoned other control");
            files["final.rto"] = new string('x', 1024 * 1024 + 1);
            Check(Decode(files, Param("FINAL_RATIO", "0")).Single().Status == DecodedSetupSetting.Unsupported, "Oversized reference accepted");
            files["final.rto"] = "Label|4.3\0";
            Check(Decode(files, Param("FINAL_RATIO", "0")).Single().Status == DecodedSetupSetting.Unsupported, "Binary reference accepted");
        });
        test("setup decoding handles invariant numbers and ignores unassigned values", () =>
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var files = Fixture(); files["setup.ini"] = "VALUE=999\n" + files["setup.ini"];
                var row = Decode(files, Param("FINAL_RATIO", "1.0")).Single();
                Check(row.NumericValue == 4.3 && row.Value.Contains("4.3:1"), "Locale or root field corrupted mapping");
            }
            finally { CultureInfo.CurrentCulture = original; }
        });
    }
}
