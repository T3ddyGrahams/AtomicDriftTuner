using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Engine;

public sealed class RunComparisonEngine
{
    public RunComparison Compare(SavedTelemetrySession before, SavedTelemetrySession after)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        var result = new RunComparison();
        var a = RunHistoryStore.ValidContext(before.Session.Context) ? before.Session.Context : null;
        var b = RunHistoryStore.ValidContext(after.Session.Context) ? after.Session.Context : null;
        var x = before.Analysis; var y = after.Analysis;
        void Require(bool valid, string why) { if (!valid) result.Limitations.Add(why); }
        Require(before.Session.Id != after.Session.Id && before.Session.StartedUtc < after.Session.StartedUtc, "Choose a distinct, earlier baseline run.");
        Require(a?.Schema == "adt/run-context/1" && b?.Schema == "adt/run-context/1", "Legacy or unknown run context: driver, track, conditions and recorded goals are required.");
        Require(KnownSame(a?.DriverId, b?.DriverId), "Driver identity is missing or different.");
        Require(KnownSame(before.Session.CarFolder, after.Session.CarFolder) && KnownSame(before.Session.DriftPack, after.Session.DriftPack), "Car or drift pack differs, or its exact identity is missing.");
        Require(KnownSame(before.Session.Wheelbase, after.Session.Wheelbase) && KnownSame(before.Session.SteeringWheel, after.Session.SteeringWheel), "Wheelbase or rim differs, or its identity is missing.");
        Require(KnownSame(a?.TrackId, b?.TrackId) && KnownSame(a?.Conditions, b?.Conditions), "Track/layout, conditions or driving task is missing or different.");
        Require(KnownSame(before.Session.DriftTarget, after.Session.DriftTarget), "Session intent changed or is missing.");
        Require(a?.CarIdentityVerified == true && b?.CarIdentityVerified == true, "The recorded car was not verified against the active AC session.");
        Require(a?.Interrupted == false && b?.Interrupted == false, "A recording was interrupted.");
        Require(a?.Tune is not null && b?.Tune is not null && KnownSame(a.Tune.ContextKey, b.Tune.ContextKey), "A tune snapshot is missing or belongs to a different rig/car.");
        Require(a?.Tune is not null && b?.Tune is not null && RunHistoryStore.SameBehavior(a.Tune.DesiredBehavior, b.Tune.DesiredBehavior), "Desired Behavior changed or was not captured; the goalposts must stay fixed for an improvement verdict.");
        Require(x.DriftTimeSeconds >= 20 && y.DriftTimeSeconds >= 20, "Both runs need at least 20 seconds of clean drift evidence.");
        Require(x.EffectiveSampleRateHz >= 15 && y.EffectiveSampleRateHz >= 15, "Sampling is too sparse for reliable phase comparison (minimum 15 Hz).");
        Require(!x.Diagnosis.TimelineReset && !y.Diagnosis.TimelineReset, "A recording timeline restarted; use fresh uninterrupted recordings.");
        Require(x.Diagnosis.InvalidSamples <= before.Session.Samples.Count * .1 && y.Diagnosis.InvalidSamples <= after.Session.Samples.Count * .1 &&
            x.Diagnosis.Discontinuities <= Math.Max(1, x.DurationSeconds / 10) && y.Diagnosis.Discontinuities <= Math.Max(1, y.DurationSeconds / 10), "Too many invalid frames or continuity breaks.");
        Require(Math.Abs(x.AverageSpeedWhileDriftingKmh - y.AverageSpeedWhileDriftingKmh) <= Math.Max(8, x.AverageSpeedWhileDriftingKmh * .2), "Drift speeds differ substantially.");
        Require(Math.Abs(x.AverageDriftAngleDeg - y.AverageDriftAngleDeg) <= 10, "Average drift angle differs by more than 10°; the driving tasks may not match.");
        double Share(double seconds, double total) => total > 0 ? seconds / total : 0;
        Require(Math.Abs(Share(x.Diagnosis.LeftDriftSeconds, x.DriftTimeSeconds) - Share(y.Diagnosis.LeftDriftSeconds, y.DriftTimeSeconds)) <= .25, "Left/right drift exposure differs substantially.");
        Require(new[] { (x.Diagnosis.LowSpeedSeconds, y.Diagnosis.LowSpeedSeconds), (x.Diagnosis.MediumSpeedSeconds, y.Diagnosis.MediumSpeedSeconds),
            (x.Diagnosis.HighSpeedSeconds, y.Diagnosis.HighSpeedSeconds) }.All(p => Math.Abs(Share(p.Item1, x.DriftTimeSeconds) - Share(p.Item2, y.DriftTimeSeconds)) <= .25), "The runs spend substantially different time in speed bands.");
        Require(x.Diagnosis.Metric("throttle")?.Value is double tx && y.Diagnosis.Metric("throttle")?.Value is double ty && Math.Abs(tx - ty) <= .15, "Throttle exposure differs or cannot be compared.");
        result.Comparable = result.Limitations.Count == 0;
        var desired = a?.Tune?.DesiredBehavior ?? new CarBehaviorTarget();
        int improved = 0, worsened = 0, scored = 0;
        bool controlWorse = false;
        foreach (var first in x.Diagnosis.Metrics.Where(m => m.Key != "throttle"))
        {
            var second = y.Diagnosis.Metric(first.Key);
            var row = new AssistantComparisonRow { Metric = first.Name, Previous = first.DisplayValue,
                Current = second?.DisplayValue ?? "Insufficient data", Change = "—", Interpretation = "Insufficient evidence in one or both runs." };
            if (first.Value is double old && second?.Value is double current)
            {
                var change = current - old;
                row.Change = $"{change:+0.###;-0.###;0} {first.Unit}";
                var direction = GoalDirection(first.Key, desired);
                var tolerance = Math.Max(Math.Abs(old) * .15, NoiseFloor(first.Key));
                var gain = first.Key is "initiation" or "transition"
                    ? Math.Abs(old - TimingTarget(first.Key == "transition" ? desired.TransitionSpeed : desired.InitiationSharpness)) -
                      Math.Abs(current - TimingTarget(first.Key == "transition" ? desired.TransitionSpeed : desired.InitiationSharpness))
                    : change * direction;
                if (!result.Comparable) row.Interpretation = "Descriptive change only: comparison conditions were not met.";
                else if (first.Confidence == "LOW" || second.Confidence == "LOW") row.Interpretation = "Descriptive change only: this metric has too little evidence to score.";
                else if (direction == 0) row.Interpretation = "Context/proxy only; no directional goal was recorded for this axis.";
                else
                {
                    scored++;
                    row.Interpretation = Math.Abs(gain) <= tolerance ? "No clear change beyond the comparison tolerance." : gain > 0 ? "Closer to the recorded goal (proxy where noted)." : "Farther from the recorded goal (proxy where noted).";
                    if (gain > tolerance) improved++;
                    if (gain < -tolerance) { worsened++; if (first.Key is "oscillation" or "extreme-angle") controlWorse = true; }
                }
            }
            result.Metrics.Add(row);
        }
        if (a?.Tune is not null && b?.Tune is not null)
        {
            foreach (var key in a.Tune.Settings.Keys.Union(b.Tune.Settings.Keys).Order())
            {
                var hasOld = a.Tune.Settings.TryGetValue(key, out var old);
                var hasNew = b.Tune.Settings.TryGetValue(key, out var current);
                if (hasOld && hasNew && Math.Abs(old - current) < .000001) continue;
                result.TuneChanges.Add(new AssistantComparisonRow { Metric = key, Previous = hasOld ? $"{old:0.###}" : "Not captured",
                    Current = hasNew ? $"{current:0.###}" : "Not captured", Change = hasOld && hasNew ? $"{current - old:+0.###;-0.###;0}" : "Added / removed",
                    Interpretation = key.StartsWith("ACSetup.", StringComparison.Ordinal) ? "Captured setup-file value; use is driver-confirmed." : "Generated ADT target; not live hardware readback." });
            }
            if (!string.Equals(a.Tune.SetupSha256, b.Tune.SetupSha256, StringComparison.Ordinal) && result.TuneChanges.All(c => !c.Metric.StartsWith("ACSetup.")))
                result.TuneChanges.Add(new AssistantComparisonRow { Metric = "AC setup file fingerprint", Previous = a.Tune.SetupFileName, Current = b.Tune.SetupFileName,
                    Change = "File changed", Interpretation = "The file changed outside the captured numeric VALUE fields, or attachment coverage differs." });
        }
        if (result.Comparable && scored >= 3)
            result.Verdict = improved > 0 && (worsened > 0 || controlWorse) ? "Tradeoff" : worsened > 0 ? "Farther from goals" : improved > 0 ? "Closer to goals" : "No clear change";
        else if (result.Comparable) result.Limitations.Add("Too few goal-related metrics have usable evidence for an overall verdict.");
        result.Summary = result.Comparable ? $"{result.Verdict}: {improved} measured area(s) closer, {worsened} farther, across {scored} scored metrics. Changes within 15% or the metric's minimum tolerance are treated as uncertain." : "Inconclusive: these runs are not comparable. Metric differences remain visible for inspection.";
        if (a?.TuneConfirmedInUse != true || b?.TuneConfirmedInUse != true)
            result.Limitations.Add("Tune use was not confirmed for both runs; this cannot establish whether the recorded recommendation helped.");
        if (b?.RecommendationSessionId != before.Session.Id || b.TestedRecommendations.Count == 0)
            result.Limitations.Add("The after-run does not identify this baseline's recommendation and the change actually tested.");
        if (result.TuneChanges.Count == 0) result.Limitations.Add("No captured tune setting changed; driving practice or an unrecorded change may explain the result.");
        if (result.TuneChanges.Count > 1) result.Limitations.Add("Multiple captured settings changed; an individual setting's effect cannot be isolated.");
        result.RecommendationTestTracked = result.Comparable && scored >= 3 && a?.TuneConfirmedInUse == true && b?.TuneConfirmedInUse == true &&
            b.RecommendationSessionId == before.Session.Id && b.TestedRecommendations.Count > 0 && result.TuneChanges.Count > 0 &&
            !string.IsNullOrWhiteSpace(a.Tune?.SetupSha256) && !string.IsNullOrWhiteSpace(b.Tune?.SetupSha256);
        if (string.IsNullOrWhiteSpace(a?.Tune?.SetupSha256) || string.IsNullOrWhiteSpace(b?.Tune?.SetupSha256))
            result.Limitations.Add("AC setup contents were not captured for both runs; unrecorded car-setup changes prevent recommendation attribution.");
        result.Limitations.Add("A before/after association is not proof of causation. Confirm the result with repeated comparable runs and driver feedback; tire wear, temperatures and track conditions may still differ.");
        return result;
    }

    public static double TimingTarget(int bias) => .9 - .2 * Math.Clamp(bias, -2, 2);
    private static bool KnownSame(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    private static double NoiseFloor(string key) => key switch { "initiation" or "transition" => .08, "front-response" => .02, "rear-slip-share" => 3, "self-steer" => 30, "stability" => 1, "oscillation" or "extreme-angle" => .5, "clipping" => 1, "throttle-rotation" => 3, _ => 1 };
    private static int GoalDirection(string key, CarBehaviorTarget b) => key switch
    {
        "initiation" or "transition" => 1,
        "front-response" => Math.Sign(b.FrontEndBite), "rear-slip-share" => -Math.Sign(b.RearGrip),
        "self-steer" => Math.Sign(b.SelfSteerSpeed), "throttle-rotation" => Math.Sign(b.ThrottleSteering),
        "stability" => b.AngleStability < 0 ? 0 : -1,
        "oscillation" or "extreme-angle" or "clipping" => -1, _ => 0
    };
}
