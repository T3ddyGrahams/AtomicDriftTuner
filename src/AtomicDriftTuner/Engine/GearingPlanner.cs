using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

/// <summary>Static road-speed gearing estimate, with no tyre slip or clutch slip.</summary>
public static class GearingPlanner
{
    public const double KmhPerMph = 1.609344;

    public static double Rpm(double speedKmh, double gear, double finalDrive, double radius) =>
        speedKmh / 3.6 / (2 * Math.PI * radius) * 60 * gear * finalDrive;

    public static GearingPlan Plan(GearingData data, GearingTarget target)
    {
        target.Validate();
        if (target.Gear != data.Gear)
            throw new InvalidDataException("The selected gear changed. Calculate again.");
        if (target.MaximumRpm > data.LimiterRpm)
            throw new InvalidDataException($"The target RPM exceeds the base engine.ini limiter of {data.LimiterRpm:N0} RPM. Lower the RPM target; active ECU/script overrides are not verified.");

        var options = data.FinalDrives.Select(ratio =>
        {
            var low = Rpm(target.MinimumSpeedKmh, data.GearRatio, ratio.Ratio, data.TyreRadius);
            var high = Rpm(target.MaximumSpeedKmh, data.GearRatio, ratio.Ratio, data.TyreRadius);
            // Fit both endpoints equally in proportional terms. The ranking does not
            // infer engine torque, wheelspin or driver preference from these inputs.
            var score = Math.Pow((low - target.MinimumRpm) / target.MinimumRpm, 2) +
                        Math.Pow((high - target.MaximumRpm) / target.MaximumRpm, 2);
            return new GearingOption(ratio, low, high, target.MaximumSpeedKmh * data.LimiterRpm / high,
                score, low >= target.MinimumRpm && high <= target.MaximumRpm);
        }).ToArray();
        var current = options.Single(x => x.FinalDrive.Index == data.CurrentIndex);
        // Never recommend a ratio that reaches the limiter within the requested speed range.
        var eligible = options.Where(x => x.HighRpm < data.LimiterRpm).ToArray();
        if (eligible.Length == 0)
            throw new InvalidDataException("Every supported final drive reaches the base engine.ini limiter within this speed range. Try a higher drift gear or lower your maximum speed; active ECU/script overrides are not verified.");
        var ranked = eligible.OrderByDescending(x => x.FitsTarget).ThenBy(x => x.Score)
            .ThenBy(x => x.FinalDrive.Index == data.CurrentIndex ? 0 : 1).ThenBy(x => x.FinalDrive.Index).ToArray();
        var best = ranked[0];
        // Avoid changes that merely select a duplicate ratio or offer negligible improvement.
        if (current.HighRpm < data.LimiterRpm && current.FitsTarget == best.FitsTarget &&
            current.Score - best.Score < 0.0001) best = current;
        return new GearingPlan(data, target, current, best, options);
    }
}
