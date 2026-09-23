using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public sealed partial class CarSetupDecoder
{
    public PowertrainContext ReadPowertrain(IReadOnlyList<CarSetupParameter> baseline, Func<string, string?> readText)
    {
        var result = new PowertrainContext();
        var source = new Source(readText);
        var saved = baseline.GroupBy(p => p.Section, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single() : null, StringComparer.OrdinalIgnoreCase);
        CarSetupParameter Selected(string key) => saved.TryGetValue(key, out var value) && value is not null ? value :
            throw new InvalidDataException($"Saved [{key}] selection is missing or ambiguous.");
        void Read(string label, Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { result.Limitations.Add(label + ": " + ex.Message); }
        }
        Read("Final drive", () =>
        {
            var setup = source.Ini("setup.ini");
            if (setup.Contains("FINAL_GEAR_RATIO") || saved.ContainsKey("FINAL_RATIO"))
            {
                var ratio = Ratio(Selected("FINAL_RATIO"), source, "FINAL_GEAR_RATIO", "Final drive");
                result.FinalDrive = ratio.NumericValue; result.FinalDriveSource = ratio.Source;
            }
            else
            {
                result.FinalDrive = Number(source.Ini("drivetrain.ini").Required("GEARS", "FINAL"), .1, 30, "fixed final drive");
                result.FinalDriveSource = "drivetrain.ini [GEARS] FINAL (fixed base definition)";
            }
        });
        Read("Forward gears", () =>
        {
            var drive = source.Ini("drivetrain.ini");
            var setup = source.Ini("setup.ini");
            int count = Index(drive.Required("GEARS", "COUNT"), 11, "forward gear count");
            if (count < 1) throw new InvalidDataException("No forward gears are defined.");
            var mode = setup.Optional("GEARS", "USE_GEARSET") ?? "0";
            if (mode is not ("0" or "1")) throw new InvalidDataException("Unknown gearbox selection mode.");
            for (int gear = 1; gear <= count; gear++)
            {
                var selectedGear = gear;
                Read($"Gear {gear}", () =>
                {
                    double ratio; string evidence;
                    if (mode == "1" && setup.ContainsPrefix("GEAR_SET_"))
                    {
                        var index = Index(Selected("GEARSET").CurrentRaw, 256, "saved gearbox selection");
                        var section = $"GEAR_SET_{index}";
                        ratio = Number(setup.Required(section, $"GEAR_{selectedGear}"), .05, 20, "selected gear ratio");
                        evidence = $"setup.ini [{section}] GEAR_{selectedGear}";
                    }
                    else if (mode == "0" && setup.Contains($"GEAR_{selectedGear}"))
                    {
                        var decoded = Ratio(Selected($"INTERNAL_GEAR_{selectedGear + 1}"), source, $"GEAR_{selectedGear}", $"Gear {selectedGear}");
                        ratio = decoded.NumericValue!.Value; evidence = decoded.Source;
                    }
                    else
                    {
                        if (saved.ContainsKey("GEARSET") || saved.ContainsKey($"INTERNAL_GEAR_{selectedGear + 1}") || saved.ContainsKey($"GEAR_{selectedGear}"))
                            throw new InvalidDataException("Saved gearbox override has no verified mapping.");
                        ratio = Number(drive.Required("GEARS", $"GEAR_{selectedGear}"), .05, 20, "fixed gear ratio");
                        evidence = $"drivetrain.ini [GEARS] GEAR_{selectedGear}";
                    }
                    result.Gears.Add(new(selectedGear, ratio, evidence));
                });
            }
        });
        Read("RPM limiter", () =>
        {
            // Assume adjustment may exist until the definitions can be read successfully.
            result.AdjustableLimiter = true;
            var setup = source.Ini("setup.ini");
            result.AdjustableLimiter = new[] { "ENGINE_LIMITER", "LIMITER" }.Any(key => setup.Contains(key) || saved.ContainsKey(key));
            result.BaseLimiterRpm = Number(source.Ini("engine.ini").Required("ENGINE_DATA", "LIMITER"), 1000, 30000, "base limiter");
            if (result.AdjustableLimiter) result.Limitations.Add("The RPM limiter is adjustable; the base definition is not a verified active limit. Limiter-proximity guidance is withheld.");
        });
        result.Limitations.Add("Recorded file definitions do not verify the active physics. ECU, turbo, controllers and scripts can change engine behavior; no horsepower or usable power band is inferred.");
        return result;
    }
}
