using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class CarSetupTuningEngine
{
    public CarSetupAnalysis Generate(
        TuneInput input,
        CarSetupAnalysis analysis,
        SetupAggressiveness aggressiveness,
        CarBehaviorTarget? behavior = null)
    {
        double scale = aggressiveness switch
        {
            SetupAggressiveness.Conservative => 0.55,
            SetupAggressiveness.Aggressive => 1.45,
            _ => 1.0
        };

        behavior ??= new CarBehaviorTarget();
        behavior.Normalize();

        var blendReport = new BehaviorBlendReport
        {
            ActiveBiasCount = behavior.ActiveBiasCount
        };

        foreach (var p in analysis.Parameters)
        {
            p.RecommendedValue = p.CurrentValue;
            p.Reason = "Left unchanged; no safe drift adjustment for this parameter and behavior target.";
            p.BlendStatus = "—";

            if (p.CurrentValue is null)
                continue;

            double styleDelta =
                CalculateDelta(
                    input,
                    p,
                    scale,
                    out var styleReason);

            var behaviorResult =
                CalculateBehaviorDelta(
                    p,
                    behavior,
                    scale);

            double behaviorDelta = behaviorResult.Delta;
            string behaviorReason = behaviorResult.Reason;

            if (behaviorResult.HasInfluence)
            {
                blendReport.ParametersAffected++;

                if (behaviorResult.HasConflict)
                {
                    blendReport.BehaviorConflictCount++;
                    AddBlendNotice(
                        blendReport,
                        p.Section,
                        "Behavior compromise",
                        behaviorResult.BlendExplanation);
                }

                if (behaviorResult.HasAlignedStack)
                {
                    blendReport.AlignedStackCount++;
                    if (!behaviorResult.HasConflict)
                    {
                        AddBlendNotice(
                            blendReport,
                            p.Section,
                            "Aligned goals",
                            behaviorResult.BlendExplanation);
                    }
                }

                p.BlendStatus = behaviorResult.Status;
            }

            // Session intent remains the higher-level request. If the per-car
            // desired behavior pulls a recognized parameter in the opposite
            // direction, keep the intent dominant and soften the behavior layer.
            bool intentConflict =
                Math.Abs(styleDelta) > 0.000001 &&
                Math.Abs(behaviorDelta) > 0.000001 &&
                Math.Sign(styleDelta) != Math.Sign(behaviorDelta);

            if (intentConflict)
            {
                behaviorDelta *= 0.65;

                var intentNote =
                    "Session-intent compromise: the selected driving intent and the per-car behavior target pull this parameter in opposite directions; " +
                    "Atomic keeps session intent as the priority and reduces the behavior influence.";

                behaviorReason = string.IsNullOrWhiteSpace(behaviorReason)
                    ? intentNote
                    : behaviorReason.Trim() + " " + intentNote;

                blendReport.IntentConflictCount++;
                AddBlendNotice(
                    blendReport,
                    p.Section,
                    "Intent compromise",
                    intentNote);

                p.BlendStatus = behaviorResult.HasConflict
                    ? "Behavior + intent compromise"
                    : "Intent compromise";
            }

            double delta = styleDelta + behaviorDelta;

            if (Math.Abs(delta) < 0.000001)
            {
                if (Math.Abs(styleDelta) > 0.000001 || behaviorResult.HasInfluence)
                {
                    p.Reason =
                        MergeReasons(styleReason, behaviorReason) +
                        " Blend result: the competing requests resolve to no net saved-value change." +
                        RangeNote(p.Range);

                    if (p.BlendStatus == "—")
                        p.BlendStatus = "Balanced out";
                }

                continue;
            }

            var proposed =
                p.CurrentValue.Value + delta;

            proposed =
                SnapAndClamp(
                    proposed,
                    p.Range,
                    p.CurrentValue.Value,
                    p.CurrentRaw);

            p.RecommendedValue = proposed;
            p.Reason =
                MergeReasons(
                    styleReason,
                    behaviorReason) +
                RangeNote(p.Range);
        }

        analysis.BehaviorBlend = blendReport;
        return analysis;
    }

    public BehaviorBlendPreview PreviewBehaviorBlend(
        CarBehaviorTarget? behavior)
    {
        behavior ??= new CarBehaviorTarget();
        behavior.Normalize();

        var preview = new BehaviorBlendPreview
        {
            ActiveBiasCount = behavior.ActiveBiasCount
        };

        if (behavior.IsNeutral)
            return preview;

        var groups =
            new Dictionary<string, List<PreviewContribution>>(
                StringComparer.OrdinalIgnoreCase);

        void Add(
            string group,
            string source,
            double value)
        {
            if (Math.Abs(value) < 0.000001)
                return;

            if (!groups.TryGetValue(group, out var list))
            {
                list = [];
                groups[group] = list;
            }

            list.Add(
                new PreviewContribution
                {
                    Source = source,
                    Value = value
                });
        }

        Add("front pressure", "Front-end bite", -0.45 * behavior.FrontEndBite);
        Add("front pressure", "Angle stability", -0.20 * behavior.AngleStability);

        Add("rear pressure", "Rear grip", -0.65 * behavior.RearGrip);
        Add("rear pressure", "Throttle steering", 0.40 * behavior.ThrottleSteering);
        Add("rear pressure", "Angle stability", -0.30 * behavior.AngleStability);

        Add("front toe response", "Front-end bite", 0.45 * behavior.FrontEndBite);
        Add("front toe response", "Self-steer speed", 0.25 * behavior.SelfSteerSpeed);
        Add("front toe response", "Initiation", 0.30 * behavior.InitiationSharpness);

        Add("rear roll stiffness", "Rear grip", -0.55 * behavior.RearGrip);
        Add("rear roll stiffness", "Transition speed", 0.55 * behavior.TransitionSpeed);
        Add("rear roll stiffness", "Angle stability", -0.45 * behavior.AngleStability);
        Add("rear roll stiffness", "Throttle steering", 0.30 * behavior.ThrottleSteering);

        Add("rear spring response", "Rear grip", -0.45 * behavior.RearGrip);
        Add("rear spring response", "Transition speed", 0.35 * behavior.TransitionSpeed);
        Add("rear spring response", "Angle stability", -0.35 * behavior.AngleStability);

        Add("rear rebound", "Transition speed", 0.55 * behavior.TransitionSpeed);
        Add("rear rebound", "Self-steer speed", 0.30 * behavior.SelfSteerSpeed);
        Add("rear rebound", "Angle stability", -0.35 * behavior.AngleStability);

        Add("power differential", "Throttle steering", 2.00 * behavior.ThrottleSteering);
        Add("power differential", "Rear grip", 1.20 * behavior.RearGrip);
        Add("power differential", "Angle stability", 0.80 * behavior.AngleStability);

        Add("coast differential", "Angle stability", 1.80 * behavior.AngleStability);
        Add("coast differential", "Initiation", -1.40 * behavior.InitiationSharpness);
        Add("coast differential", "Transition speed", -0.80 * behavior.TransitionSpeed);

        Add("brake bias", "Initiation", -0.45 * behavior.InitiationSharpness);
        Add("brake bias", "Angle stability", 0.25 * behavior.AngleStability);

        foreach (var pair in groups)
        {
            var active =
                pair.Value
                    .Where(x => Math.Abs(x.Value) > 0.000001)
                    .ToList();

            if (active.Count < 2)
                continue;

            bool hasPositive =
                active.Any(x => x.Value > 0);

            bool hasNegative =
                active.Any(x => x.Value < 0);

            if (hasPositive && hasNegative)
            {
                preview.PotentialConflictGroups++;

                var positive =
                    string.Join(
                        ", ",
                        active
                            .Where(x => x.Value > 0)
                            .Select(x => x.Source)
                            .Distinct());

                var negative =
                    string.Join(
                        ", ",
                        active
                            .Where(x => x.Value < 0)
                            .Select(x => x.Source)
                            .Distinct());

                preview.Details.Add(
                    $"{pair.Key}: {positive} and {negative} request opposite setup directions.");
            }
            else
            {
                preview.PotentialAlignedGroups++;

                var sources =
                    string.Join(
                        ", ",
                        active
                            .Select(x => x.Source)
                            .Distinct());

                preview.Details.Add(
                    $"{pair.Key}: {sources} align; Atomic will damp the combined stack.");
            }
        }

        return preview;
    }

    private static double CalculateDelta(TuneInput input, CarSetupParameter p, double scale, out string reason)
    {
        string s = p.Section.ToUpperInvariant();
        bool front = s.EndsWith("_LF") || s.EndsWith("_RF") || s.Contains("FRONT");
        bool rear = s.EndsWith("_LR") || s.EndsWith("_RR") || s.Contains("REAR");
        var style = input.Intent.Kind;
        reason = "";

        if (s.StartsWith("PRESSURE_"))
        {
            double d = style switch
            {
                DriftStyleKind.Training => -1,
                DriftStyleKind.FastSelfSteer => front ? -1 : 1,
                DriftStyleKind.Tandem => front ? -1 : 0,
                DriftStyleKind.Competition => -1,
                _ => 0
            };
            reason = front ? "Front pressure trimmed for front-end response/grip." : "Rear pressure adjusted for the selected rotation/traction target.";
            return d * scale;
        }

        if (s.StartsWith("CAMBER_"))
        {
            if (front && (style is DriftStyleKind.FastSelfSteer or DriftStyleKind.Competition))
            {
                reason = "Adds a small amount of front negative-camber bias for response; verify tire temperatures/feel.";
                return p.CurrentValue <= 0 ? -1 * scale : 1 * scale;
            }
            if (rear && (style is DriftStyleKind.Tandem or DriftStyleKind.Competition))
            {
                reason = "Moves rear camber slightly toward a traction-oriented setting.";
                return p.CurrentValue < 0 ? 1 * scale : -1 * scale;
            }
        }

        if (s.StartsWith("TOE_OUT_") && front && (style is DriftStyleKind.FastSelfSteer or DriftStyleKind.Competition))
        {
            reason = "Small front toe-out increase for initial steering response.";
            return 1 * scale;
        }

        if (s.Contains("ARB"))
        {
            if (rear && style == DriftStyleKind.FastSelfSteer)
            {
                reason = "One-step rear roll-stiffness bias to help rotation; keep small to avoid snap oversteer.";
                return 1 * scale;
            }
            if (rear && style == DriftStyleKind.Training)
            {
                reason = "Softer rear roll-stiffness bias for a wider stability window.";
                return -1 * scale;
            }
        }

        if (s.Contains("SPRING_RATE"))
        {
            if (rear && style == DriftStyleKind.Training)
            {
                reason = "Small softer-rear bias for progressive breakaway.";
                return -1 * scale;
            }
            if (rear && style == DriftStyleKind.FastSelfSteer)
            {
                reason = "Small stiffer-rear bias for faster rotation/transition.";
                return 1 * scale;
            }
        }

        if (s.Contains("DAMP_REBOUND") && rear && (style is DriftStyleKind.FastSelfSteer or DriftStyleKind.Competition))
        {
            reason = "Small rear rebound increase to sharpen transition response.";
            return 1 * scale;
        }

        if (s == "DIFF_POWER")
        {
            reason = "Raises power-side locking to keep both rear tires driving consistently in drift.";
            return PercentLikeStep(p.CurrentValue.Value, 4 * scale);
        }
        if (s == "DIFF_COAST")
        {
            double d = style == DriftStyleKind.Training ? 3 : style == DriftStyleKind.Tandem ? 2 : 1;
            reason = "Adds a modest coast-lock bias for entry/transition stability.";
            return PercentLikeStep(p.CurrentValue.Value, d * scale);
        }

        if (s is "FRONT_BIAS" or "BRAKE_BIAS")
        {
            if (style is DriftStyleKind.FastSelfSteer or DriftStyleKind.Competition)
            {
                reason = "Moves brake bias slightly rearward to help rotation under braking; test carefully.";
                return -1 * scale;
            }
        }

        if (s.Contains("FINAL_RATIO") || s.StartsWith("GEAR"))
        {
            reason = "Gearing left unchanged because saved values may be ratio-list indexes whose direction varies by car.";
            return 0;
        }

        if (s == "FUEL" || s == "TYRES" || (s is "ABS" or "TC"))
        {
            reason = "Driver/session choice left unchanged.";
            return 0;
        }

        return 0;
    }

    private static BehaviorDeltaResult CalculateBehaviorDelta(
        CarSetupParameter p,
        CarBehaviorTarget behavior,
        double aggressivenessScale)
    {
        var result = new BehaviorDeltaResult();

        if (behavior.IsNeutral || p.CurrentValue is null)
            return result;

        string s = p.Section.ToUpperInvariant();

        bool front =
            s.EndsWith("_LF") ||
            s.EndsWith("_RF") ||
            s.Contains("FRONT");

        bool rear =
            s.EndsWith("_LR") ||
            s.EndsWith("_RR") ||
            s.Contains("REAR");

        // Desired behavior remains deliberately weaker than the base
        // Training/Tandem/etc. session intent.
        double scale = aggressivenessScale * 0.55;

        var contributions =
            new List<BehaviorContribution>();

        void Add(
            double value,
            string source,
            string note)
        {
            if (Math.Abs(value) < 0.000001)
                return;

            contributions.Add(
                new BehaviorContribution
                {
                    Delta = value,
                    Source = source,
                    Note = note
                });
        }

        if (s.StartsWith("PRESSURE_"))
        {
            if (front)
            {
                Add(
                    -0.45 * behavior.FrontEndBite * scale,
                    "Front-end bite",
                    "front-end bite biases front pressure");

                Add(
                    -0.20 * behavior.AngleStability * scale,
                    "Angle stability",
                    "angle stability preserves front grip");
            }

            if (rear)
            {
                Add(
                    -0.65 * behavior.RearGrip * scale,
                    "Rear grip",
                    "rear-grip target biases rear pressure");

                Add(
                    0.40 * behavior.ThrottleSteering * scale,
                    "Throttle steering",
                    "throttle-steering target biases rear rotation");

                Add(
                    -0.30 * behavior.AngleStability * scale,
                    "Angle stability",
                    "angle stability adds rear traction margin");
            }
        }

        if (s.StartsWith("CAMBER_"))
        {
            if (front && behavior.FrontEndBite != 0)
            {
                double direction =
                    p.CurrentValue.Value <= 0
                        ? -1
                        : 1;

                Add(
                    direction * 0.35 * behavior.FrontEndBite * scale,
                    "Front-end bite",
                    "front-end bite biases front camber");
            }

            if (rear && behavior.RearGrip != 0)
            {
                double towardZero =
                    p.CurrentValue.Value < 0
                        ? 1
                        : p.CurrentValue.Value > 0
                            ? -1
                            : 0;

                Add(
                    towardZero * 0.30 * behavior.RearGrip * scale,
                    "Rear grip",
                    "rear-grip target biases rear camber toward/away from its traction window");
            }
        }

        if (s.StartsWith("TOE_OUT_") && front)
        {
            Add(
                0.45 * behavior.FrontEndBite * scale,
                "Front-end bite",
                "front-end bite biases front toe response");

            Add(
                0.25 * behavior.SelfSteerSpeed * scale,
                "Self-steer speed",
                "self-steer target biases initial steering response");

            Add(
                0.30 * behavior.InitiationSharpness * scale,
                "Initiation",
                "initiation target biases turn-in sharpness");
        }

        if (s.Contains("ARB") && rear)
        {
            Add(
                -0.55 * behavior.RearGrip * scale,
                "Rear grip",
                "rear-grip target biases rear roll stiffness");

            Add(
                0.55 * behavior.TransitionSpeed * scale,
                "Transition speed",
                "transition-speed target biases rear roll response");

            Add(
                -0.45 * behavior.AngleStability * scale,
                "Angle stability",
                "angle-stability target widens the rear stability window");

            Add(
                0.30 * behavior.ThrottleSteering * scale,
                "Throttle steering",
                "throttle-steering target biases rear rotation");
        }

        if (s.Contains("SPRING_RATE") && rear)
        {
            Add(
                -0.45 * behavior.RearGrip * scale,
                "Rear grip",
                "rear-grip target biases rear spring support");

            Add(
                0.35 * behavior.TransitionSpeed * scale,
                "Transition speed",
                "transition-speed target biases rear spring response");

            Add(
                -0.35 * behavior.AngleStability * scale,
                "Angle stability",
                "angle-stability target biases a more progressive rear platform");
        }

        if (s.Contains("DAMP_REBOUND") && rear)
        {
            Add(
                0.55 * behavior.TransitionSpeed * scale,
                "Transition speed",
                "transition-speed target biases rear rebound");

            Add(
                0.30 * behavior.SelfSteerSpeed * scale,
                "Self-steer speed",
                "self-steer target biases transition response");

            Add(
                -0.35 * behavior.AngleStability * scale,
                "Angle stability",
                "angle-stability target tempers rear rebound response");
        }

        if (s == "DIFF_POWER")
        {
            Add(
                PercentLikeStep(
                    p.CurrentValue.Value,
                    2.00 * behavior.ThrottleSteering * scale),
                "Throttle steering",
                "throttle-steering target biases power-side differential locking");

            Add(
                PercentLikeStep(
                    p.CurrentValue.Value,
                    1.20 * behavior.RearGrip * scale),
                "Rear grip",
                "rear-grip target biases power-side differential locking");

            Add(
                PercentLikeStep(
                    p.CurrentValue.Value,
                    0.80 * behavior.AngleStability * scale),
                "Angle stability",
                "angle-stability target biases power-side differential locking");
        }

        if (s == "DIFF_COAST")
        {
            Add(
                PercentLikeStep(
                    p.CurrentValue.Value,
                    1.80 * behavior.AngleStability * scale),
                "Angle stability",
                "angle-stability target biases coast-side differential locking");

            Add(
                PercentLikeStep(
                    p.CurrentValue.Value,
                    -1.40 * behavior.InitiationSharpness * scale),
                "Initiation",
                "initiation target biases coast-side differential locking");

            Add(
                PercentLikeStep(
                    p.CurrentValue.Value,
                    -0.80 * behavior.TransitionSpeed * scale),
                "Transition speed",
                "transition-speed target biases coast-side differential locking");
        }

        if (s is "FRONT_BIAS" or "BRAKE_BIAS")
        {
            Add(
                -0.45 * behavior.InitiationSharpness * scale,
                "Initiation",
                "initiation target biases brake balance for entry rotation");

            Add(
                0.25 * behavior.AngleStability * scale,
                "Angle stability",
                "angle-stability target biases brake balance toward stability");
        }

        return BlendBehaviorContributions(
            contributions);
    }

    private static BehaviorDeltaResult BlendBehaviorContributions(
        List<BehaviorContribution> contributions)
    {
        var result =
            new BehaviorDeltaResult
            {
                HasInfluence = contributions.Count > 0
            };

        if (contributions.Count == 0)
            return result;

        var positive =
            contributions
                .Where(x => x.Delta > 0)
                .ToList();

        var negative =
            contributions
                .Where(x => x.Delta < 0)
                .ToList();

        double positiveEffective =
            DampedMagnitude(
                positive.Select(x => Math.Abs(x.Delta)));

        double negativeEffective =
            DampedMagnitude(
                negative.Select(x => Math.Abs(x.Delta)));

        bool hasPositive = positive.Count > 0;
        bool hasNegative = negative.Count > 0;

        var notes =
            contributions
                .Select(x => x.Note)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        string desiredReason =
            "Desired behavior: " +
            string.Join("; ", notes) +
            ".";

        if (hasPositive && hasNegative)
        {
            result.HasConflict = true;
            result.Status = "Compromise";

            double strongest =
                Math.Max(
                    positiveEffective,
                    negativeEffective);

            double overlap =
                Math.Min(
                    positiveEffective,
                    negativeEffective);

            double conflictRatio =
                strongest <= 0.000001
                    ? 0
                    : overlap / strongest;

            // Cancellation already resolves much of the conflict. The extra
            // factor softens the remaining net request as the overlap grows.
            double compromiseScale =
                1.0 - (0.25 * conflictRatio);

            result.Delta =
                (positiveEffective - negativeEffective) *
                compromiseScale;

            var positiveSources =
                string.Join(
                    ", ",
                    positive
                        .Select(x => x.Source)
                        .Distinct());

            var negativeSources =
                string.Join(
                    ", ",
                    negative
                        .Select(x => x.Source)
                        .Distinct());

            int reducedPct =
                (int)Math.Round(
                    (1.0 - compromiseScale) * 100.0);

            result.BlendExplanation =
                $"{positiveSources} and {negativeSources} push this parameter in opposite directions. " +
                $"Atomic cancels the overlap, then softens the remaining net request by {reducedPct}% so one goal does not overpower the other.";

            result.Reason =
                desiredReason +
                " Blend compromise: " +
                result.BlendExplanation;

            return result;
        }

        bool alignedStack =
            contributions.Count > 1;

        result.HasAlignedStack =
            alignedStack;

        result.Delta =
            hasPositive
                ? positiveEffective
                : -negativeEffective;

        if (alignedStack)
        {
            result.Status = "Aligned / damped";

            var sources =
                string.Join(
                    ", ",
                    contributions
                        .Select(x => x.Source)
                        .Distinct());

            result.BlendExplanation =
                $"{sources} push this parameter in the same direction. Atomic applies diminishing returns to the stack so multiple goals do not overcorrect the setup.";

            result.Reason =
                desiredReason +
                " Blend alignment: " +
                result.BlendExplanation;
        }
        else
        {
            result.Status = "Single goal";
            result.BlendExplanation =
                "One desired-behavior goal influences this parameter.";
            result.Reason = desiredReason;
        }

        return result;
    }

    private static double DampedMagnitude(
        IEnumerable<double> magnitudes)
    {
        var ordered =
            magnitudes
                .Where(x => x > 0.000001)
                .OrderByDescending(x => x)
                .ToList();

        if (ordered.Count == 0)
            return 0;

        double total = 0;

        for (int i = 0; i < ordered.Count; i++)
        {
            double weight =
                i == 0
                    ? 1.0
                    : Math.Max(
                        0.35,
                        Math.Pow(0.70, i));

            total +=
                ordered[i] * weight;
        }

        return total;
    }

    private static void AddBlendNotice(
        BehaviorBlendReport report,
        string parameter,
        string kind,
        string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return;

        // Keep the in-memory audit useful without letting very large setup
        // files create an unbounded wall of duplicate notices.
        if (report.Notices.Count >= 24)
            return;

        report.Notices.Add(
            new BehaviorBlendNotice
            {
                Parameter = parameter,
                Kind = kind,
                Summary = summary
            });
    }

    private sealed class BehaviorContribution
    {
        public string Source { get; set; } = "";
        public string Note { get; set; } = "";
        public double Delta { get; set; }
    }

    private sealed class BehaviorDeltaResult
    {
        public double Delta { get; set; }
        public string Reason { get; set; } = "";
        public string Status { get; set; } = "—";
        public string BlendExplanation { get; set; } = "";
        public bool HasInfluence { get; set; }
        public bool HasConflict { get; set; }
        public bool HasAlignedStack { get; set; }
    }

    private sealed class PreviewContribution
    {
        public string Source { get; set; } = "";
        public double Value { get; set; }
    }

    private static string MergeReasons(string styleReason, string behaviorReason)
    {
        bool hasStyle = !string.IsNullOrWhiteSpace(styleReason);
        bool hasBehavior = !string.IsNullOrWhiteSpace(behaviorReason);

        if (hasStyle && hasBehavior)
            return styleReason.Trim() + " " + behaviorReason.Trim();
        if (hasStyle)
            return styleReason.Trim();
        if (hasBehavior)
            return behaviorReason.Trim();
        return "Conservative drift recommendation.";
    }

    private static double PercentLikeStep(double current, double requested)
    {
        // Common AC differential values are percent-like. Very small values are more likely indexes/clicks.
        return Math.Abs(current) >= 10 ? requested : Math.Sign(requested) * Math.Max(1, Math.Round(Math.Abs(requested) / 4));
    }

    private static double SnapAndClamp(double value, SetupRangeDefinition? range, double current, string currentRaw)
    {
        // AC can store raw setup values differently from what the setup screen displays.
        // Trust file MIN/MAX/STEP only when the baseline raw value is already compatible with that range.
        bool compatible = range is not null && !range.ShowClicks &&
                          (range.Min is null || current >= range.Min.Value - 0.0001) &&
                          (range.Max is null || current <= range.Max.Value + 0.0001);

        if (compatible)
        {
            if (range!.Step is > 0)
            {
                double origin = range.Min ?? 0;
                value = origin + Math.Round((value - origin) / range.Step.Value) * range.Step.Value;
            }
            if (range.Min is not null) value = Math.Max(value, range.Min.Value);
            if (range.Max is not null) value = Math.Min(value, range.Max.Value);
        }
        else if (!currentRaw.Contains('.'))
        {
            // Unknown/click-index mappings are kept to whole saved-value steps when the baseline was integral.
            value = Math.Round(value, MidpointRounding.AwayFromZero);
        }
        return value;
    }

    private static string RangeNote(SetupRangeDefinition? range) => range switch
    {
        null => " Range metadata unavailable; change is baseline-relative.",
        { ShowClicks: true } => " setup.ini uses click display; physical MIN/MAX mapping is not forced onto the saved raw value.",
        _ => " Numeric setup.ini metadata is used only when the baseline raw value is compatible with that range."
    };
}
