using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;

namespace AtomicDriftTuner.Engine;

internal static class AssistantNextStepBuilder
{
    internal static AssistantNextStep Build(TuningAssistantReport report, CarBehaviorTarget goal, SavedTelemetrySession selected, SavedTelemetrySession? baseline)
    {
        var a = selected.Analysis;
        var angle = a.Diagnosis.AngleGoal;
        var next = new AssistantNextStep { Goal = DescribeGoal(goal), Why = report.ConfidenceReason,
            Noticed = "No clear, repeated issue stands out from this run.",
            Instruction = "Keep this setup. If you test a change, change one thing and record the same section again.",
            Confidence = "No change justified yet", ActionLabel = "Back to dashboard" };
        if (!string.IsNullOrEmpty(selected.Session.Context?.SetupCaptureIssue))
        {
            next.Noticed = "The current setup changed or could no longer be monitored during this recording.";
            next.Instruction = "Keep the setup fixed, restore automatic capture or attach the setup manually, then record a fresh run.";
            next.Confidence = "Fresh setup evidence needed";
            next.Why = selected.Session.Context.SetupCaptureIssue;
            return next;
        }
        if (report.OverallConfidence == AssistantConfidence.Low)
        {
            next.Noticed = "This run does not yet give us enough reliable evidence for a setup recommendation.";
            next.Instruction = "Keep your setup and save a clean run with several entries, transitions and sustained drifts.";
            next.Confidence = "Another clean run needed";
            return next;
        }
        if (goal.HasAngleGoal && !angle.Enabled)
        {
            next.Noticed = "This run was not recorded with your angle goal, so it cannot tell us how well that goal was met.";
            next.Instruction = "Save the angle goal for this car, then record a new baseline.";
            next.Confidence = "New baseline needed";
            return next;
        }
        if (baseline is not null && !report.Outcome.Comparable)
        {
            bool pedals = report.Outcome.Limitations.Any(l => l.Contains("pedal", StringComparison.OrdinalIgnoreCase) || l.Contains("Throttle exposure"));
            next.Noticed = pedals ? "Pedal use differs or could not be verified between these runs. The effect of the tune is uncertain." :
                "These runs do not yet provide a fair comparison.";
            next.Instruction = "Review the comparison reason, then choose a matching baseline or record a comparable repeat.";
            next.Confidence = "Comparison needs a check";
            next.Why = string.Join("\n", report.Outcome.Limitations);
            next.Action = "Compare"; next.ActionLabel = "Review comparison";
            return next;
        }
        if (angle.Enabled)
        {
            next.Noticed = $"You held your {angle.TargetMinDeg:0}–{angle.TargetMaxDeg:0}° band for {angle.LongestHoldSeconds:0.0}s at its longest.";
            next.Why = angle.Summary + " " + report.ConfidenceReason;
            if (angle.CompletedAttempts < 3 || angle.IncompleteAttempts > angle.CompletedAttempts)
            {
                next.Instruction = "Keep this setup. Record several attempts and continue recording through the return from each one.";
                next.Confidence = "More complete attempts needed";
                return next;
            }
            if (a.SpinEvents > 0 || a.OscillationEvents > 0)
            {
                next.Noticed += " Some attempts also showed control or recovery concerns.";
                next.Instruction = "Inspect those attempts before requesting more rotation. Repeat with this setup to check whether the concern recurs.";
                next.Confidence = "Control needs review";
                next.Action = "Evidence"; next.ActionLabel = "Review the attempts";
                return next;
            }
            next.Noticed += $" A settled return was observed after {angle.RecoveredAttempts} of {angle.CompletedAttempts} complete attempts.";
            next.Confidence = "Useful pattern; verify with another run";
        }
        if (baseline is not null && report.Outcome.Comparable && report.Outcome.Verdict != "Inconclusive")
        {
            next.Noticed = report.Outcome.Verdict switch {
                "Closer to goals" => "The measured result moved closer to your saved goals.",
                "Farther from goals" => "The measured result moved farther from your saved goals.",
                "Tradeoff" => "Some measured areas improved while others got worse.",
                _ => "The comparison did not show a clear overall change." };
            next.Instruction = "Add how the car felt and save your run review before deciding what to keep.";
            next.Confidence = report.Outcome.RecommendationTestTracked ? "Recorded test; driver feedback still needed" : "Observed difference; tune effect is not confirmed";
            next.Why = report.Outcome.Summary + "\n" + string.Join("\n", report.Outcome.Limitations);
            next.Action = "Review"; next.ActionLabel = "Rate and save this comparison";
            return next;
        }
        var focus = selected.Session.Context?.Focus ?? TuningFocus.Both;
        var recommendation = report.Recommendations.Where(r => r.Confidence != "LOW" && TuningFocusOptions.Allows(focus, r))
            .OrderBy(r => r.Domain == "AC FFB" ? 0 : r.Priority == "Repeat inputs" ? 1 : r.Priority == "Review" ? 2 : 3).FirstOrDefault();
        if (recommendation is not null)
        {
            next.Recommendation = recommendation;
            next.Noticed = recommendation.Priority == "Repeat inputs" ? recommendation.Change.Split(". ")[0] + "." :
                $"A possible improvement is worth testing in {recommendation.Domain.ToLowerInvariant()}.";
            next.Instruction = recommendation.Change;
            next.Confidence = "Worth testing; improvement is not confirmed";
            next.Why = recommendation.Why;
            if (RunHistoryStore.ValidContext(selected.Session.Context))
            { next.Action = "Plan"; next.ActionLabel = "Plan this test"; }
        }
        return next;
    }
    internal static string DescribeGoal(CarBehaviorTarget goal)
    {
        var parts = new List<string>();
        if (goal.HasAngleGoal) parts.Add(goal.AngleGoalLabel);
        if (goal.AngleStability > 0) parts.Add("Predictable handling at angle");
        if (goal.RearGrip > 0) parts.Add("A more planted rear");
        if (goal.RearGrip < 0) parts.Add("A looser rear");
        if (goal.TransitionSpeed > 0) parts.Add("Quicker transitions");
        if (goal.TransitionSpeed < 0) parts.Add("Smoother transitions");
        if (goal.ThrottleSteering > 0) parts.Add("More rotation on power");
        return parts.Count > 0 ? string.Join(" · ", parts.Take(3)) : "Controlled, repeatable drifting with your saved handling goals";
    }
}
