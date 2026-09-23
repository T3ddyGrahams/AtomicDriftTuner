using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class GearingRunSpeeds
{
    public static IReadOnlyList<GearingCornerTarget> Read(SavedTelemetrySession run, TuneInput input, IReadOnlyList<int> gears)
    {
        if (gears.Count is < 1 or > 2 || gears.Any(g => g is < 1 or > 10)) throw new InvalidDataException("Choose one or two forward gears first.");
        if (!RunHistoryStore.ValidContext(run.Session.Context) || run.Session.Context is not { CarIdentityVerified: true } context ||
            context.Tune?.ContextKey != RunHistoryStore.ContextKey(input))
            throw new InvalidDataException("Choose a run with verified identity for this car and profile. No speed targets were changed.");
        var result = new List<GearingCornerTarget>();
        foreach (int gear in gears)
        {
            var matches = run.Analysis.Diagnosis.Powertrain.Gears.Where(g => g.Gear == gear).ToArray();
            if (matches.Length != 1 || matches[0] is not { Confidence: "MEDIUM", Seconds: >= 10 } row ||
                !double.IsFinite(row.LowSpeedKmh) || !double.IsFinite(row.HighSpeedKmh) || row.LowSpeedKmh < 5 || row.HighSpeedKmh > 500 || row.HighSpeedKmh - row.LowSpeedKmh < 1)
                throw new InvalidDataException($"Gear {gear} needs at least 10 seconds of reliable drift evidence with a usable speed range. Record that section or enter speeds manually. No targets were changed.");
            result.Add(new(gear, row.LowSpeedKmh, row.HighSpeedKmh));
        }
        return result;
    }
}
