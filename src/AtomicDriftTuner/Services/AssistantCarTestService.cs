using System.Globalization;
using AtomicDriftTuner.Engine;
using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Turns existing tuner output into one reviewed experiment. Does not diagnose or invent new values.</summary>
public sealed class AssistantCarTestService
{
    public sealed class Choice
    {
        internal CarSetupAnalysis Analysis { get; }
        public string Name { get; }
        public string Changes { get; }
        public string Details { get; }
        public string Explanation { get; }
        public string TestDescription { get; }
        public RecommendationTest Test { get; }
        public override string ToString() => Name;
        internal Choice(string name, CarSetupAnalysis analysis, AssistantRecommendation recommendation, string expectation, SavedTelemetrySession run)
        {
            Name = name; Analysis = analysis;
            var changed = analysis.Parameters.Where(p => p.Changed).ToArray();
            Changes = string.Join("\n", changed.Select(p => $"{Label(p.Section)}: {Value(p, false)} → {Value(p, true)}"));
            Details = string.Join("\n", changed.Select(p => $"{p.Section}: saved VALUE {p.CurrentRaw} → {p.RecommendedRaw}. {p.RangeText} {p.Reason}"));
            Explanation = expectation;
            TestDescription = recommendation.Domain + ": test " + name + ". " +
                string.Join("; ", changed.Select(p => $"{p.Section} {p.CurrentRaw} → {p.RecommendedRaw}"));
            Test = RecommendationTestService.Create(run, TestDescription, recommendation.MetricKey,
                changed.Select(p => new ExpectedSettingChange("ACSetup." + p.Section, p.CurrentValue!.Value, p.RecommendedValue!.Value)));
        }
    }

    public static string UnavailableReason(TuneInput input, SavedTelemetrySession? run, TuningAssistantReport? report)
    {
        if (run is null || report is null) return "Select a saved run to review what to do next.";
        var next = report.NextStep;
        if (next.Action != "Plan" || next.Recommendation?.Area != RecommendationArea.CarSetup ||
            next.Recommendation.Confidence is not ("MEDIUM" or "HIGH") || report.OverallConfidence == AssistantConfidence.Low)
            return "No car setup change recommended yet. Follow the run's next step above; it may ask for another run, a technique check, an FFB change or comparison feedback.";
        var c = run.Session.Context;
        if (!RunHistoryStore.ValidContext(c) || !c!.CarIdentityVerified || !c.TuneConfirmedInUse || c.Interrupted || c.SetupCaptureIssue.Length > 0)
            return "No verified car setup test yet. Record a clean baseline with the car and loaded setup confirmed.";
        if (!input.Car.IsInstalled || !TuningFocusOptions.IncludesCar(c.Focus) || c.Tune!.ContextKey != RunHistoryStore.ContextKey(input))
            return "Select this run's installed car and matching tuning workflow before preparing its setup test.";
        if (c.Tune.HasUnassignedSetupValues || !c.Tune.Settings.Keys.Any(k => k.StartsWith("ACSetup.", StringComparison.Ordinal)) || c.Tune.BasePhysicsFingerprint.Length == 0)
            return "No exact setup change yet. This run needs named setup values and readable car definitions. Attach or capture the loaded setup and record a fresh baseline.";
        if (Groups(next.Recommendation.MetricKey).Length == 0)
            return "No verified setting mapping is available for this finding. Keep the setup and use the evidence review.";
        return "";
    }

    public IReadOnlyList<Choice> Build(TuneInput input, SavedTelemetrySession run, TuningAssistantReport report, string baselinePath)
    {
        var unavailable = UnavailableReason(input, run, report);
        if (unavailable.Length > 0) throw new InvalidDataException(unavailable);
        var service = new AssettoCorsaSetupService();
        var baseline = service.LoadBaseline(baselinePath, input.Car);
        MatchRecordedSetup(run, baseline);
        var recommendation = report.NextStep.Recommendation!;
        // The existing engine and the report's temporary guidance produce all numbers.
        // Limit this view to one relevant setting family; do not export the full generated tune.
        var generated = new CarSetupTuningEngine().Generate(input, baseline, SetupAggressiveness.Balanced, report.SuggestedBehaviorTarget);
        var choices = new List<Choice>();
        foreach (var (name, sections) in Groups(recommendation.MetricKey))
        {
            var members = generated.Parameters.Where(p => sections.Contains(p.Section, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (members.Length != sections.Length || !members.Any(p => p.Changed) ||
                members.Any(p => p.Range?.UnavailableReason is not null ||
                    !SetupValueMapping.TryCreate(p.Section, p.Range, out _))) continue;
            var isolated = service.LoadBaseline(baselinePath, input.Car);
            MatchRecordedSetup(run, isolated);
            foreach (var p in isolated.Parameters)
            {
                var candidate = members.FirstOrDefault(m => m.Section.Equals(p.Section, StringComparison.OrdinalIgnoreCase));
                if (candidate is not null) { p.RecommendedValue = candidate.RecommendedValue; p.Reason = candidate.Reason; }
            }
            try { new PitSetupPlanService().Create(isolated, isolated.CarFolderName, "Reviewed setup test"); }
            catch (InvalidDataException) { continue; }
            choices.Add(new Choice(name, isolated, recommendation, Expectation(run, report), run));
        }
        CarDataSource.EnsureUnchanged(generated.SourceEvidence!);
        if (choices.Count == 0)
            throw new InvalidDataException("No verified car setup change is available from this baseline. The existing suggestion either rounds to the same legal value, reaches a limit, or uses an unsupported mapping. Keep this setup and record a comparable repeat; ADT has not invented a setting change.");
        return choices.AsReadOnly();
    }

    public void Validate(SavedTelemetrySession run, Choice choice)
    {
        MatchRecordedSetup(run, choice.Analysis);
        new PitSetupPlanService().Create(choice.Analysis, choice.Analysis.CarFolderName, "Reviewed setup test");
    }
    public string Save(SavedTelemetrySession run, Choice choice, string output)
    {
        Validate(run, choice);
        return new AssettoCorsaSetupService().WriteGenerated(choice.Analysis, output);
    }
    public string Stage(SavedTelemetrySession run, Choice choice, Func<CarSetupAnalysis, string, string> stage)
    {
        Validate(run, choice);
        return stage(choice.Analysis, "ADT test: " + choice.Name);
    }
    private static void MatchRecordedSetup(SavedTelemetrySession run, CarSetupAnalysis analysis)
    {
        var tune = run.Session.Context!.Tune!;
        if (!analysis.BaselineIdentityVerified || analysis.HasUnassignedValues || analysis.SourceEvidence is null ||
            !analysis.SourceEvidence.Fingerprint.Equals(tune.BasePhysicsFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("This file or the car definitions do not match the recorded baseline. Select the setup used in that run, or record a fresh baseline for the current car data.");
        var recorded = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in tune.Settings.Where(p => p.Key.StartsWith("ACSetup.", StringComparison.Ordinal)))
            if (!recorded.TryAdd(pair.Key[8..], pair.Value)) throw new InvalidDataException("The run has ambiguous setup values; record a fresh baseline.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (recorded.Count != analysis.Parameters.Count || analysis.Parameters.Any(p => !seen.Add(p.Section) || p.CurrentValue is not double value ||
            !recorded.TryGetValue(p.Section, out var saved) || !PitSetupPlanService.NumbersEqual(value, saved)))
            throw new InvalidDataException("This setup's values differ from the selected run. Browse to the matching saved setup. If that setup is unavailable, save the current in-game setup and record a new baseline.");
    }

    private static (string Name, string[] Sections)[] Groups(string key) => key switch
    {
        "rear-slip-share" => [("Rear tyre pressures", ["PRESSURE_LR", "PRESSURE_RR"]), ("Rear anti-roll bar", ["ARB_REAR"]), ("Rear camber", ["CAMBER_LR", "CAMBER_RR"]), ("Rear springs", ["SPRING_RATE_LR", "SPRING_RATE_RR"])],
        "front-slip-share" => [("Front tyre pressures", ["PRESSURE_LF", "PRESSURE_RF"]), ("Front camber", ["CAMBER_LF", "CAMBER_RF"]), ("Front toe", ["TOE_OUT_LF", "TOE_OUT_RF"])],
        "initiation" => [("Front toe", ["TOE_OUT_LF", "TOE_OUT_RF"])],
        "transition" => [("Rear anti-roll bar", ["ARB_REAR"]), ("Rear rebound damping", ["DAMP_REBOUND_LR", "DAMP_REBOUND_RR"])],
        _ => []
    };
    private static string Expectation(SavedTelemetrySession run, TuningAssistantReport report)
    {
        var recorded = run.Session.Context!.Tune!.DesiredBehavior;
        return report.NextStep.Recommendation!.MetricKey switch
        {
            "rear-slip-share" => recorded.RearGrip > 0
                ? "Aim: a more planted rear. Check whether it is easier to hold your line. Watch for less rotation or a harder time starting the drift."
                : "Aim: a looser rear. Check whether it rotates as you want. Watch for overshooting the angle or harder recovery.",
            "front-slip-share" => "Aim: a front end that follows your intended line. Check whether steering response feels clearer. Watch for twitchiness; wheel slip alone does not prove a front-grip problem.",
            "initiation" => run.Analysis.Diagnosis.Metric("initiation")?.Value > RunComparisonEngine.TimingTarget(recorded.InitiationSharpness)
                ? "Aim: a sharper start to the drift. Check whether the car reaches angle more readily. Watch for overshoot and reduced forgiveness."
                : "Aim: a more progressive start to the drift. Check whether it is easier to control. Watch for slower response.",
            _ => run.Analysis.Diagnosis.Metric("transition")?.Value > RunComparisonEngine.TimingTarget(recorded.TransitionSpeed)
                ? "Aim: quicker changes of drift direction. Check whether the car changes sides more easily. Watch for overshoot or extra corrections."
                : "Aim: smoother changes of drift direction. Check whether the car settles more easily. Watch for slower response."
        };
    }
    public static string FindingSummary(SavedTelemetrySession run, TuningAssistantReport report) => report.NextStep.Recommendation?.MetricKey switch
    {
        "rear-slip-share" => "ADT recorded enough rear wheel-slip evidence to review a small test toward your rear-grip goal. Wheel slip by itself does not prove a grip problem.",
        "front-slip-share" => "Front wheel slip dominated the drift samples. Review steering technique as well as a small front-response test; the setup may not be the cause.",
        "initiation" => "The time taken to start the drift differed from ADT's provisional reference for your goal. A small response adjustment is worth testing alongside your technique.",
        "transition" => "The time taken to change drift direction differed from ADT's provisional reference for your goal. A small response adjustment is worth testing on the same line.",
        _ => report.NextStep.Noticed
    };
    private static string Label(string section) => section.ToUpperInvariant().Replace("PRESSURE_", "Tyre pressure ").Replace("CAMBER_", "Camber ")
        .Replace("TOE_OUT_", "Toe ").Replace("SPRING_RATE_", "Spring ").Replace("DAMP_REBOUND_", "Rebound damping ")
        .Replace("ARB_REAR", "Rear anti-roll bar").Replace("LF", "front left").Replace("RF", "front right").Replace("LR", "rear left").Replace("RR", "rear right");
    private static string Value(CarSetupParameter p, bool after)
    {
        var raw = after ? p.RecommendedValue!.Value : p.CurrentValue!.Value;
        if (CamberSetupValues.TrySetupValue(p.Range, raw, out var camber)) return camber.ToString("0.###", CultureInfo.CurrentCulture) + " setup units";
        if (SetupValueMapping.TryCreate(p.Section, p.Range, out var mapping) && mapping.IsLegal(raw))
            return mapping.SetupValue(raw).ToString("0.###", CultureInfo.CurrentCulture) +
                (string.IsNullOrWhiteSpace(p.Range?.Units) ? " setup units" : " " + p.Range.Units);
        return raw.ToString("0.###", CultureInfo.CurrentCulture) + " (saved value)";
    }
}
