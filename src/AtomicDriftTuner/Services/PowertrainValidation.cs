using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

internal static class PowertrainValidation
{
    internal static bool Valid(TuneVersion tune)
    {
        if (tune.GearingTargetStatus is null || tune.GearingTargetStatus.Length > 1000) return false;
        if (tune.GearingTarget is not null)
            try { tune.GearingTarget.Validate(); } catch (InvalidDataException) { return false; }
        if (tune.Powertrain is not { } p) return true; // Older snapshots have no powertrain context.
        return p.Version == "powertrain-context/1" && p.PhysicsFingerprint == tune.BasePhysicsFingerprint &&
            p.PhysicsFingerprint is { Length: 64 } && p.PhysicsFingerprint.All(Uri.IsHexDigit) &&
            Optional(p.FinalDrive, .001, 100) && Optional(p.BaseLimiterRpm, 1000, 30000) &&
            Text(p.FinalDriveSource) && p.Gears is not null && p.Gears.Count <= 10 &&
            p.Gears.All(g => g is not null && g.Gear is >= 1 and <= 10 && Optional(g.Ratio, .001, 100) && Text(g.Source)) &&
            p.Gears.Select(g => g.Gear).Distinct().Count() == p.Gears.Count &&
            p.Limitations is not null && p.Limitations.Count <= 30 && p.Limitations.All(Text);
    }

    internal static bool Valid(DecodedSetupSetting setting)
    {
        if (setting.EngineMap is not { } map) return true;
        if (!string.Equals(setting.Section, "ENGINE_MAPS", StringComparison.OrdinalIgnoreCase) || setting.Status != DecodedSetupSetting.Verified || map.Index is < 0 or > 4095 ||
            !Text(map.Name) || map.Points is null || map.Points.Count is < 1 or > 4096) return false;
        double previous = -1;
        foreach (var point in map.Points)
        {
            if (point is null || !Optional(point.Rpm, 0, 100000) || !Optional(point.Multiplier, 0, 100) || point.Rpm <= previous) return false;
            previous = point.Rpm;
        }
        return true;
    }

    private static bool Optional(double? number, double min, double max) => number is null || double.IsFinite(number.Value) && number >= min && number <= max;
    private static bool Text(string? text) => text is not null && text.Length <= 8000;
}
