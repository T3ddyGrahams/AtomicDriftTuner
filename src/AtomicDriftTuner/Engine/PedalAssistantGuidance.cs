using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

internal static class PedalAssistantGuidance
{
    internal static void Add(PedalDiagnosis pedals, CarBehaviorTarget goal, TuningAssistantReport report)
    {
        foreach (var (domain, kinds, desired, next) in new[]
        {
            ("Throttle and rear response", new[] { "Throttle application", "Throttle lift" },
                goal.RearGrip > 0 ? "More planted rear" : goal.ThrottleSteering > 0 || goal.RearGrip < 0 ? "More rotation on power" : "Predictable response to throttle",
                goal.RearGrip > 0 ? "For your planted-rear goal, check whether angle or rear slip rises around throttle applications. Repeat with similar throttle timing before attributing this to rear grip." :
                goal.ThrottleSteering > 0 || goal.RearGrip < 0 ? "For your rotation goal, check whether applications accompany useful angle build-up without extreme-angle events. Repeat the same application before trying more rotation in the setup." :
                "Compare applications and lifts at the same part of the track. Look for repeatable angle and yaw response before changing the car."),
            ("Braking and balance", new[] { "Brake application", "Brake release", "Throttle / brake overlap" },
                goal.AngleStability > 0 ? "Stable / forgiving at angle" : goal.TransitionSpeed > 0 ? "Quicker, controlled transitions" : "Controlled initiation and transitions",
                "Inspect brake application, release and overlap around entries/transitions. Braking can coincide with speed and rotation changes; overlap may be intentional. Keep braking points and overlap similar when testing a setup change."),
            ("Clutch and initiation", new[] { "Clutch signal cycle" },
                goal.InitiationSharpness > 0 ? "Sharper initiation" : goal.InitiationSharpness < 0 ? "Progressive initiation" : "Repeatable initiation",
                "Use the event's RPM and gear evidence to distinguish a possible initiation technique from a shift. Keep clutch timing and assists consistent when testing initiation response; a signal cycle does not confirm a clutch kick.")
        })
        {
            var events = pedals.Events.Where(e => kinds.Contains(e.Kind)).ToList();
            var complete = events.Count(e => e.ResponseComplete && e.Confidence == "MEDIUM");
            var repeated = events.Where(e => e.ResponseComplete && e.Confidence == "MEDIUM").GroupBy(e => e.Kind).Any(g => g.Count() >= 3);
            var phases = events.Count(e => e.Phase is "Initiation" or "Transition");
            var rotation = events.Count(e => e.ResponseComplete && e.AngleChangeDeg >= 5);
            var confidence = repeated && report.OverallConfidence != AssistantConfidence.Low ? "MEDIUM" : "LOW";
            report.Assessments.Add(new AssistantBehaviorAssessment { Behavior = domain, Desired = desired,
                Observed = $"{events.Count} event(s); {complete} usable response window(s)",
                Status = repeated ? "INPUT / RESPONSE CONTEXT" : "INSUFFICIENT REPEATED EVIDENCE", Confidence = confidence,
                Evidence = $"{phases} event(s) near initiation/transition; {rotation} with ≥5° higher absolute angle afterward. " +
                    "Timing association only, not a technique score. Open Pedal Evidence for timestamps, other inputs and limitations. " + next });
            if (events.Count > 0)
                report.Recommendations.Add(new AssistantRecommendation { Domain = domain, Priority = "Repeat inputs", Area = RecommendationArea.General,
                    Change = next, Why = $"Recorded goal: {desired}. {complete} usable response window(s); pedal use can confound setup, self-steer and stability observations.",
                    Confidence = confidence });
        }
        report.PreserveNotes.Add("Pedal evidence provides context and repeat-run guidance; it does not change generated FFB, gearing, calibration or car setup values.");
    }
}
