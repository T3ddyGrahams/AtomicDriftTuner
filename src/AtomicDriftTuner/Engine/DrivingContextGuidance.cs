using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

internal static class DrivingContextGuidance
{
    internal static void Add(DriftDiagnosis diagnosis, CarBehaviorTarget goal, TuningAssistantReport report, bool displayMph)
    {
        var context = diagnosis.DrivingContext;
        if (context.Observations.Count == 0) return;
        bool reliable = report.OverallConfidence != AssistantConfidence.Low;
        report.DrivingContextSummary = $"{context.Observations.Count(o => o.Confidence != "LOW")} condition-specific measurements meet the duration/event thresholds. " +
            "Timing needs three complete events per condition; continuous measures need ten accumulated clean seconds. " +
            (reliable ? "" : "This run's overall evidence is insufficient or unreliable; these details are for inspection only. ") +
            (displayMph ? "Speed bands use 50 and 90 km/h boundaries; displayed mph equivalents are rounded. " : "") + context.Limitations;
        foreach (var group in context.Observations.GroupBy(o => o.MetricKey))
        {
            var metric = diagnosis.Metric(group.Key);
            if (metric is null) continue;
            var assessment = report.Assessments.FirstOrDefault(a => a.Behavior == metric.Name);
            var target = group.Key == "initiation" ? RunComparisonEngine.TimingTarget(goal.InitiationSharpness) : RunComparisonEngine.TimingTarget(goal.TransitionSpeed);
            bool timing = group.Key is "initiation" or "transition";
            foreach (var row in group)
                report.ContextAssessments.Add(new() {
                    Behavior = metric.Name + " — " + row.DisplayContext(displayMph),
                    Desired = assessment?.Desired ?? "Inspect against the saved goal",
                    Observed = row.Value is double v ? $"{v:0.###} {metric.Unit}" : "More evidence needed in this condition",
                    Status = row.Value is null ? "MORE MATCHING EVIDENCE NEEDED" : timing ?
                        row.Value > target * 1.2 ? "SLOWER THAN PROVISIONAL REFERENCE" : row.Value < target * .8 ? "FASTER THAN PROVISIONAL REFERENCE" : "NEAR PROVISIONAL REFERENCE" : "DESCRIPTIVE CONTEXT",
                    Confidence = reliable ? row.Confidence : "LOW", Evidence = row.Evidence + " " + metric.Evidence +
                        (group.Key == "oscillation" ? " Clusters are assigned by speed and direction at detection; a cluster may start in another speed band." : "") });
            // Cross-band events remain visible but cannot establish a speed-specific setup test.
            var usable = group.Where(o => o.Value is not null && o.Confidence != "LOW" && o.SpeedBand < 3).ToArray();
            var recommendation = report.Recommendations.FirstOrDefault(r => r.MetricKey == group.Key && r.Area == RecommendationArea.CarSetup);
            bool conflicting = timing && usable.Any(o => o.Value > target * 1.2) && usable.Any(o => o.Value < target * .8);
            int requestedDirection = Math.Sign((metric.Value ?? target) - target);
            bool supportsRequest = !timing || usable.Any(o => requestedDirection > 0 ? o.Value > target * 1.2 : o.Value < target * .8);
            bool opposesRequest = timing && recommendation is not null && usable.Any(o => requestedDirection > 0 ? o.Value < target * .8 : o.Value > target * 1.2);
            bool differentInputs = usable.Any(a => usable.Any(b => a != b && a.ThrottlePct is double at && b.ThrottlePct is double bt &&
                (Math.Abs(at - bt) > 20 || Math.Abs((a.BrakePct ?? 0) - (b.BrakePct ?? 0)) > 15 || Math.Abs((a.ClutchSignalPct ?? 0) - (b.ClutchSignalPct ?? 0)) > 20)));
            bool inputsRelevant = timing || group.Key is "front-slip-share" or "rear-slip-share" or "throttle-rotation";
            bool hold = recommendation is not null && (usable.Length == 0 || !supportsRequest || inputsRelevant && differentInputs) || conflicting || opposesRequest;
            if (!reliable && recommendation is not null) recommendation.Confidence = "LOW";
            if (hold && reliable)
            {
                report.Recommendations.RemoveAll(r => r.MetricKey == group.Key && r.Area == RecommendationArea.CarSetup);
                if (group.Key == "initiation") report.SuggestedBehaviorTarget.InitiationSharpness = goal.InitiationSharpness;
                if (group.Key == "transition") report.SuggestedBehaviorTarget.TransitionSpeed = goal.TransitionSpeed;
                var observed = conflicting || opposesRequest ? "Timing differs by condition, so a whole-car response change could work against part of this run." :
                    differentInputs ? "Pedal use differs substantially between the measured conditions." : "The whole-run observation lacks enough repeated evidence in a matching speed and direction.";
                var evidence = string.Join(" ", usable.Take(6).Select(o => $"{o.DisplayContext(displayMph)}: {o.Value:0.###} {metric.Unit}; {o.Evidence}"));
                if (assessment is not null) { assessment.Status = "MATCHED REPEAT NEEDED"; assessment.Evidence += " " + observed; }
                report.Recommendations.Add(new() { MetricKey = group.Key, Domain = metric.Name, Area = RecommendationArea.General,
                    Priority = "Repeat inputs", Confidence = "MEDIUM", DrivingContext = observed,
                    Change = observed + " Keep this setup and repeat the same section at similar speed and with similar pedal inputs. Check each direction before changing the whole car.",
                    Why = $"Saved goal: {assessment?.Desired}. " + evidence + " " + context.Limitations });
            }
            else if (reliable && recommendation is not null && usable.Length > 0)
            {
                var selected = timing ? usable.OrderByDescending(o => Math.Abs(o.Value!.Value - target)).ThenByDescending(o => o.Events).First() :
                    usable.OrderByDescending(o => o.EvidenceSeconds).First();
                recommendation.DrivingContext = $"{metric.Name}: {selected.Value:0.###} {metric.Unit} during {selected.DisplayContext(displayMph).ToLowerInvariant()}.";
                recommendation.Change = "Repeat " + selected.DisplayContext(displayMph).ToLowerInvariant() + " on the same section when testing. " + recommendation.Change;
                recommendation.Why += " Condition evidence: " + selected.Evidence + " A whole-car change may affect other conditions too.";
            }
        }
    }
}
