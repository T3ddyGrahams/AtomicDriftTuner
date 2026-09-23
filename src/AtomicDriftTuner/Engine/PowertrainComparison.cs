using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Engine;

public sealed record PowertrainComparisonReport(string Summary, IReadOnlyList<AssistantComparisonRow> Rows);

/// <summary>Descriptive differences only; never feeds an improvement verdict or recommendation credit.</summary>
public static class PowertrainComparison
{
    public static PowertrainComparisonReport Build(SavedTelemetrySession current, SavedTelemetrySession? baseline, RunComparison? conditions = null)
    {
        if (baseline is null) return new("Choose an earlier baseline in Before / After to compare observed RPM by gear. To separate gearing from ECU effects, change one at a time.", []);
        var rows = new List<AssistantComparisonRow>();
        var before = baseline.Analysis.Diagnosis.Powertrain;
        var after = current.Analysis.Diagnosis.Powertrain;
        foreach (var gear in before.Gears.Select(g => g.Gear).Intersect(after.Gears.Select(g => g.Gear)).Order())
        {
            var a = before.Gears.Single(g => g.Gear == gear); var b = after.Gears.Single(g => g.Gear == gear);
            rows.Add(new() { Metric = $"Gear {gear}: median RPM", Previous = $"{a.MedianRpm:0}", Current = $"{b.MedianRpm:0}",
                Change = $"{b.MedianRpm - a.MedianRpm:+0;-0;0}", Interpretation = $"Descriptive only: {a.Seconds:0.0}s vs {b.Seconds:0.0}s; typical speed {a.SpeedBand} vs {b.SpeedBand} km/h. Different exposure, line and slip can change RPM." });
        }
        var oldTune = RunHistoryStore.ValidContext(baseline.Session.Context) ? baseline.Session.Context!.Tune : null;
        var newTune = RunHistoryStore.ValidContext(current.Session.Context) ? current.Session.Context!.Tune : null;
        var notes = new List<string> { "RPM differences are observations, not an improvement score or proof that gearing or ECU caused the change." };
        if (conditions?.Comparable != true) notes.Add("The main before/after conditions are not met; inspect its limitations before comparing setup effects.");
        var oldFinal = oldTune?.Powertrain?.FinalDrive; var newFinal = newTune?.Powertrain?.FinalDrive;
        bool gearingChanged = false, mapChanged = false;
        if (oldFinal is double oldRatio && newFinal is double newRatio)
        {
            gearingChanged = Math.Abs(oldRatio - newRatio) > .000001;
            rows.Add(new() { Metric = "Recorded final drive", Previous = $"{oldRatio:0.####}:1", Current = $"{newRatio:0.####}:1",
                Change = gearingChanged ? "Changed" : "Same", Interpretation = "Verified file mapping; confirmation in game is separate." });
        }
        else notes.Add("One or both runs lack a recorded final-drive mapping; current car files are not used to fill it in.");
        if (oldTune?.Powertrain is { } oldBox && newTune?.Powertrain is { } newBox)
        {
            foreach (var gear in oldBox.Gears.Select(g => g.Gear).Intersect(newBox.Gears.Select(g => g.Gear)).Order())
            {
                var a = oldBox.Gears.Single(g => g.Gear == gear).Ratio; var b = newBox.Gears.Single(g => g.Gear == gear).Ratio;
                bool changed = Math.Abs(a - b) > .000001; gearingChanged |= changed;
                rows.Add(new() { Metric = $"Recorded gear {gear} ratio", Previous = $"{a:0.####}:1", Current = $"{b:0.####}:1",
                    Change = changed ? "Changed" : "Same", Interpretation = "Verified file mapping; confirmation in game is separate." });
            }
            if (!oldBox.Gears.Select(g => g.Gear).Order().SequenceEqual(newBox.Gears.Select(g => g.Gear).Order()))
                notes.Add("Gearbox mapping coverage differs. A missing ratio does not establish a gearing change.");
        }
        var oldMaps = oldTune?.DecodedSetup.Where(d => d.Section.Equals("ENGINE_MAPS", StringComparison.OrdinalIgnoreCase)).ToList();
        var newMaps = newTune?.DecodedSetup.Where(d => d.Section.Equals("ENGINE_MAPS", StringComparison.OrdinalIgnoreCase)).ToList();
        if (oldMaps?.Count == 1 && newMaps?.Count == 1 && oldMaps[0].EngineMap is not null && newMaps[0].EngineMap is not null && oldMaps[0].Status == DecodedSetupSetting.Verified && newMaps[0].Status == DecodedSetupSetting.Verified &&
            oldTune!.HasUnassignedSetupValues == false && newTune!.HasUnassignedSetupValues == false)
        {
            var a = oldMaps[0]; var b = newMaps[0]; mapChanged = a.EngineMap!.Index != b.EngineMap!.Index;
            rows.Add(new() { Metric = "Recorded ECU mapping", Previous = a.Value, Current = b.Value, Change = mapChanged ? "Selection changed" : "Same selection",
                Interpretation = "Configured mapping, not horsepower, active engine output or a verdict that one map is better." });
        }
        else notes.Add("ECU selection is not verified on both runs. An unnamed value or a label such as Race Fuel cannot establish a power change.");
        if (oldTune?.BasePhysicsFingerprint != newTune?.BasePhysicsFingerprint || string.IsNullOrEmpty(oldTune?.BasePhysicsFingerprint))
            notes.Add("The recorded physics definitions differ or are unknown; setup selections alone cannot isolate the effect.");
        if (gearingChanged && mapChanged) notes.Add("Both gearing and ECU selections changed. Separate them with baseline, gearing-only and ECU-only tests before trying the combined change.");
        else notes.Add("Keep the other settings and inputs consistent, and repeat the same section. Review speed retained and control alongside RPM.");
        return new(string.Join("\n", notes), rows.AsReadOnly());
    }
}
