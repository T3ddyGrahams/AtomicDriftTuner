using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Engine;

public sealed class DriftAssistantReportBuilder
{
    public TuningAssistantReport Build(TuneInput input, CarBehaviorTarget behavior, SavedTelemetrySession selected, SavedTelemetrySession? previous)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(behavior); ArgumentNullException.ThrowIfNull(selected);
        var recorded = RunHistoryStore.ValidContext(selected.Session.Context) ? selected.Session.Context!.Tune!.DesiredBehavior : null;
        var goal = RunHistoryStore.Clone(recorded ?? behavior); goal.Normalize();
        var a = selected.Analysis;
        var r = new TuningAssistantReport { SuggestedBehaviorTarget = RunHistoryStore.Clone(goal),
            OverallConfidence = a.DriftTimeSeconds >= 20 && DriftDiagnosisEngine.Reliable(selected.Session, a) ? AssistantConfidence.Medium : AssistantConfidence.Low,
            ConfidenceReason = $"{a.DriftTimeSeconds:0.0}s usable drift; {a.DriftEntries} entries; {a.TransitionCount} transitions. Grip and self-steer are proxies. " +
                (recorded is null ? "Legacy run: evaluated against current goals, not historical goals." : "Using the Desired Behavior snapshot recorded with this run."),
            ProposedCalibration = RunHistoryStore.Clone(a.CalibrationSuggestion) };
        if (recorded is not null && !RunHistoryStore.SameBehavior(recorded, behavior)) r.ConfidenceReason += " Your current Desired Behavior differs from this run's saved goals.";
        foreach (var m in a.Diagnosis.Metrics.Where(m => m.Key != "throttle"))
        {
            var axis = m.Key switch { "initiation" => goal.InitiationSharpness, "transition" => goal.TransitionSpeed, "front-response" or "front-slip-share" => goal.FrontEndBite,
                "rear-slip-share" => goal.RearGrip, "self-steer" => goal.SelfSteerSpeed, "stability" => goal.AngleStability, "throttle-rotation" => goal.ThrottleSteering, _ => 0 };
            var desired = m.Key switch { "initiation" => axis > 0 ? "Sharper initiation" : axis < 0 ? "Progressive initiation" : "Balanced initiation",
                "transition" => axis > 0 ? "Quicker transitions" : axis < 0 ? "Smoother transitions" : "Balanced transitions",
                "front-response" or "front-slip-share" => axis > 0 ? "More front bite" : axis < 0 ? "Calmer front response" : "Neutral front target",
                "rear-slip-share" => axis > 0 ? "More planted rear" : axis < 0 ? "Looser rear" : "Neutral rear target",
                "self-steer" => axis > 0 ? "Faster self-steer" : axis < 0 ? "Slower self-steer" : "Neutral response target",
                "stability" => axis < 0 ? "Livelier at angle" : "Stable / forgiving",
                "throttle-rotation" => axis > 0 ? "More powered rotation" : axis < 0 ? "Less powered rotation" : "Neutral powered rotation", _ => "Controlled, without saturation/loss" };
            string status = m.Value is null ? "INSUFFICIENT DATA" : "OBSERVED PROXY";
            string guidance = "";
            if (m.Value is double value && m.Key is "initiation" or "transition")
            {
                var target = RunComparisonEngine.TimingTarget(axis);
                status = Math.Abs(value - target) <= target * .2 ? "NEAR TARGET" : "NEEDS WORK";
                var requested = Math.Clamp(axis + Math.Sign(value - target), -2, 2);
                if (m.Confidence != "LOW" && r.OverallConfidence != AssistantConfidence.Low && status == "NEEDS WORK" &&
                    (requested <= axis || a.SpinEvents == 0 && a.OscillationEvents == 0))
                {
                    if (m.Key == "transition") r.SuggestedBehaviorTarget.TransitionSpeed = requested;
                    else r.SuggestedBehaviorTarget.InitiationSharpness = requested;
                    guidance = value > target ? "Test one small response increase using AC Setup with Guidance; compare on the same line. Faster response may reduce forgiveness." :
                        "Test a more progressive AC setup response. This may improve control but make initiation or transitions feel slower.";
                }
                desired += $" (provisional {target:0.0}s reference, not a universal optimum)";
            }
            if (m.Value is double control && m.Key is "oscillation" or "extreme-angle" or "clipping")
            {
                bool problem = control > (m.Key == "clipping" ? 4 : 1);
                status = problem ? "NEEDS WORK" : "CONTROLLED IN THIS RUN";
                if (problem) guidance = m.Key == "clipping" ? "Review the small AC gain reduction below, then record another run." :
                    "Prioritize control before requesting faster response. Inspect the event times and check whether driver corrections, technique or the tune explain them.";
            }
            if (m.Key == "front-slip-share" && m.Value is double share && share > 60)
                guidance = "Front wheel slip dominates these sustained-drift samples. Inspect front response and steering technique; compare a small front-bite change on this car before concluding front grip is deficient.";
            if (m.Key == "rear-slip-share" && m.Value is not null && axis != 0)
                guidance = axis > 0 ? "For a more planted rear, compare a small rear-grip change using the range-safe AC setup tuner; reduced rotation can be a tradeoff." :
                    "For a looser rear, test one small rear-grip change and watch extreme-angle events; extra rotation can reduce recovery margin.";
            r.Assessments.Add(new AssistantBehaviorAssessment { Behavior = m.Name, Desired = desired, Observed = m.DisplayValue,
                Status = status, Confidence = m.Confidence, Evidence = $"{m.EvidenceSeconds:0.0}s evidence" + (m.Events >= 0 ? $", {m.Events} events. " : ". ") + m.Evidence });
            if (guidance.Length > 0) r.Recommendations.Add(new AssistantRecommendation { Domain = m.Name, Priority = status == "NEEDS WORK" ? "Review" : "Explore",
                Change = guidance, Why = $"Goal: {desired}. Observed: {m.DisplayValue}. {m.Evidence}", Confidence = m.Confidence });
        }
        // Do not speed up an unstable run or turn proxy evidence into automatic wheelbase writes.
        if (a.SpinEvents > 0 || a.OscillationEvents > 0)
        {
            r.SuggestedBehaviorTarget.TransitionSpeed = Math.Min(goal.TransitionSpeed, r.SuggestedBehaviorTarget.TransitionSpeed);
            r.SuggestedBehaviorTarget.InitiationSharpness = Math.Min(goal.InitiationSharpness, r.SuggestedBehaviorTarget.InitiationSharpness);
        }
        r.HasSuggestedBehaviorChange = !RunHistoryStore.SameBehavior(goal, r.SuggestedBehaviorTarget);
        r.SuggestedBehaviorSummary = r.HasSuggestedBehaviorChange ?
            $"Temporary guidance: initiation {goal.InitiationSharpness:+0;-0;0} → {r.SuggestedBehaviorTarget.InitiationSharpness:+0;-0;0}; transitions {goal.TransitionSpeed:+0;-0;0} → {r.SuggestedBehaviorTarget.TransitionSpeed:+0;-0;0}. Recorded goals remain unchanged." :
            "Keep the recorded Desired Behavior. Inspect phase evidence and test one supported setup change at a time.";
        if (!r.ProposedCalibration.IsNeutral) r.Recommendations.Add(new AssistantRecommendation { Domain = "AC FFB", Priority = "Review",
            Change = $"AC gain calibration {r.ProposedCalibration.AcGainDelta:+0;-0;0}", Why = string.Join(" ", r.ProposedCalibration.Reasons), Confidence = "MEDIUM" });
        r.OverallAssessment = a.Assessment;
        r.PreserveNotes.Add("No Desired Behavior profile or hardware setting changes merely by analyzing a run.");
        if (previous is not null) { r.Outcome = new RunComparisonEngine().Compare(previous, selected); r.Comparison = r.Outcome.Metrics; }
        return r;
    }
}
