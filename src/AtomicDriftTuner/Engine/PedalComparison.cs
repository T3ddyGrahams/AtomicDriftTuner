using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

internal static class PedalComparison
{
    internal static void Add(PedalDiagnosis before, PedalDiagnosis after, RunComparison result)
    {
        bool coverage = before.DriftSeconds >= 20 && after.DriftSeconds >= 20 &&
            before.KnownSignalSeconds >= before.DriftSeconds * .9 && after.KnownSignalSeconds >= after.DriftSeconds * .9;
        if (!coverage) result.Limitations.Add("Pedal signal coverage is insufficient or unverified. Record fresh runs with at least 20 seconds of clean drift.");
        foreach (var (key, tolerance) in new (string, double)[] { ("throttle-low", 20), ("throttle-high", 20), ("throttle-variation", 15),
                     ("brake-mean", 10), ("brake-active", 15), ("pedal-overlap", 10), ("clutch-mean", 15), ("clutch-variation", 15),
                     ("throttle-applications", 6), ("throttle-lifts", 6), ("clutch-cycles", 6) })
        {
            var a = before.Metric(key); var b = after.Metric(key);
            var row = new AssistantComparisonRow { Metric = "Pedals: " + (a?.Name ?? b?.Name ?? key),
                Previous = a?.DisplayValue ?? "Insufficient data", Current = b?.DisplayValue ?? "Insufficient data",
                Change = "—", Interpretation = "Input context only; insufficient evidence to compare." };
            if (coverage && a?.Value is double old && b?.Value is double current && double.IsFinite(old) && double.IsFinite(current))
            {
                var difference = current - old;
                var limit = a.Unit == "/min" ? Math.Max(tolerance, Math.Abs(old) * .5) : tolerance;
                bool differs = Math.Abs(difference) > limit;
                var unit = a.Unit == "%" ? "pp" : a.Unit;
                row.Change = $"{difference:+0.0;-0.0;0.0} {unit}";
                row.Interpretation = differs ? "Pedal use changed substantially; repeat with similar inputs before crediting the tune." :
                    "Within the provisional input tolerance; this is context, not an improvement score.";
                row.Interpretation += $" Tolerance: {limit:0.#} {unit}.";
                if (differs) result.Limitations.Add($"{a.Name} differs by {Math.Abs(difference):0.0} {unit} (pedal comparison tolerance {limit:0.#}). The result may reflect different inputs.");
            }
            else if (coverage) result.Limitations.Add($"Pedal context '{a?.Name ?? key}' is missing; reopen the raw runs to reanalyze them.");
            result.Metrics.Add(row);
        }
    }
}
