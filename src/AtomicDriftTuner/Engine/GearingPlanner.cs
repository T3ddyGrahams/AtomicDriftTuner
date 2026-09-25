using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

/// <summary>Static road-speed gearing estimate, with no tyre slip or clutch slip.</summary>
public static class GearingPlanner
{
    public const double KmhPerMph = 1.609344;
    public static double Rpm(double speedKmh, double gear, double finalDrive, double radius) =>
        speedKmh / 3.6 / (2 * Math.PI * radius) * 60 * gear * finalDrive;

    public static GearingPlan Plan(GearingData data, GearingTarget target, GearingData? sweeperData = null)
    {
        target.Validate();
        var goals = target.Goals().ToArray();
        var sources = target.Sweeper is null ? new[] { data } : new[] { data, sweeperData ?? throw new InvalidDataException("Read both selected gears before calculating.") };
        for (int i = 0; i < goals.Length; i++)
        {
            var source = sources[i]; var t = goals[i].Target;
            if (t.Gear != source.Gear) throw new InvalidDataException("The selected gear changed. Calculate again.");
            if (t.MaximumRpm > source.LimiterRpm) throw new InvalidDataException($"The target RPM exceeds this baseline's setup rev limit of {source.LimiterRpm:N0} RPM. Use Read car and suggest RPM range, or lower the upper RPM under Advanced. Active ECU/script overrides are not verified.");
            if (i > 0 && (source.CarPath != data.CarPath || source.BaselinePath != data.BaselinePath || source.CurrentIndex != data.CurrentIndex ||
                source.CarDataEvidence?.Fingerprint != data.CarDataEvidence?.Fingerprint || !source.FinalDrives.SequenceEqual(data.FinalDrives) ||
                source.TyreRadius != data.TyreRadius || source.LimiterRpm != data.LimiterRpm || !source.Fingerprints.OrderBy(p => p.Key).SequenceEqual(data.Fingerprints.OrderBy(p => p.Key))))
                throw new InvalidDataException("The two corner targets must use the same car, baseline and unchanged definitions.");
            if (t.RpmSourceFingerprint is not null && (source.CarDataEvidence?.Fingerprint != t.RpmSourceFingerprint ||
                source.RpmEstimate.MinimumRpm != t.MinimumRpm || source.RpmEstimate.MaximumRpm != t.MaximumRpm))
                throw new InvalidDataException("The engine data behind the suggested RPM band changed. Read the car's RPM range again, or choose a manual target.");
        }
        var options = data.FinalDrives.Select(ratio =>
        {
            double score = 0;
            var results = goals.Select((goal, i) =>
            {
                var t = goal.Target; var source = sources[i];
                var low = Rpm(t.MinimumSpeedKmh, source.GearRatio, ratio.Ratio, source.TyreRadius);
                var high = Rpm(t.MaximumSpeedKmh, source.GearRatio, ratio.Ratio, source.TyreRadius);
                score += Math.Pow((low - t.MinimumRpm) / t.MinimumRpm, 2) + Math.Pow((high - t.MaximumRpm) / t.MaximumRpm, 2);
                return new GearingGoalEstimate(goal.Label, t.Gear, low, high, t.MaximumSpeedKmh * source.LimiterRpm / high,
                    low >= t.MinimumRpm && high <= t.MaximumRpm, high < source.LimiterRpm);
            }).ToArray();
            return new GearingOption(ratio, results[0].LowRpm, results[0].HighRpm, results[0].LimiterSpeedKmh, score / goals.Length, results.All(g => g.FitsTarget)) { Goals = results };
        }).ToArray();
        var current = options.Single(x => x.FinalDrive.Index == data.CurrentIndex);
        // A preset must avoid the selected baseline's setup rev limit in every requested section.
        var eligible = options.Where(x => x.BelowLimiter).ToArray();
        if (eligible.Length == 0) throw new InvalidDataException("No supported final drive stays below this baseline's setup rev limit across all requested speed ranges. Try a higher gear or lower maximum speed for the affected section. No setup was changed.");
        var best = eligible.OrderByDescending(x => x.FitsTarget).ThenBy(x => x.Score)
            .ThenBy(x => x.FinalDrive.Index == data.CurrentIndex ? 0 : 1).ThenBy(x => x.FinalDrive.Index).First();
        if (current.BelowLimiter && current.FitsTarget == best.FitsTarget && current.Score - best.Score < .0001) best = current;
        return new(data, target, current, best, options) { SweeperData = sweeperData };
    }
}
