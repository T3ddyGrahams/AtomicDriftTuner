using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Engine;

public sealed class CarSetupTuningEngine
{
    private const double Epsilon =
        0.000001;

    private const double DesiredBehaviorStrength =
        0.55;

    private const int MaximumBlendNotices =
        24;

    public CarSetupAnalysis Generate(
        TuneInput input,
        CarSetupAnalysis analysis,
        SetupAggressiveness aggressiveness,
        CarBehaviorTarget? behavior = null)
    {
        ArgumentNullException.ThrowIfNull(
            input);

        ArgumentNullException.ThrowIfNull(
            analysis);

        if (analysis.Parameters is null)
        {
            throw new InvalidDataException(
                "ADT cannot generate AC setup recommendations because the setup analysis contains no parameter collection.");
        }

        var scale =
            AggressivenessScale(
                aggressiveness);

        // Never normalize/mutate the caller-owned Desired Behavior profile
        // simply because a recommendation was requested.
        var desiredBehavior =
            CloneBehavior(
                behavior ?? new CarBehaviorTarget());

        desiredBehavior.Normalize();

        var blendReport =
            new BehaviorBlendReport
            {
                ActiveBiasCount =
                    desiredBehavior.ActiveBiasCount
            };

        foreach (var parameter in analysis.Parameters)
        {
            if (parameter is null)
            {
                continue;
            }

            parameter.RecommendedValue =
                parameter.CurrentValue;

            parameter.Reason =
                "Left unchanged; no safe drift adjustment was identified for this parameter and behavior target.";

            parameter.BlendStatus =
                "—";

            if (
                parameter.CurrentValue is not double current ||
                !double.IsFinite(current))
            {
                parameter.Reason =
                    "Left unchanged because the saved setup value is missing or is not a finite numeric value.";

                continue;
            }

            var styleDelta =
                CalculateDelta(
                    input,
                    parameter,
                    scale,
                    out var styleReason);

            if (!double.IsFinite(styleDelta))
            {
                parameter.Reason =
                    "Left unchanged because ADT could not produce a finite session-intent adjustment for this parameter.";

                continue;
            }

            var behaviorResult =
                CalculateBehaviorDelta(
                    parameter,
                    desiredBehavior,
                    scale);

            var behaviorDelta =
                FiniteOrZero(
                    behaviorResult.Delta);

            var behaviorReason =
                behaviorResult.Reason;

            if (behaviorResult.HasInfluence)
            {
                blendReport.ParametersAffected++;

                if (behaviorResult.HasConflict)
                {
                    blendReport.BehaviorConflictCount++;

                    AddBlendNotice(
                        blendReport,
                        parameter.Section,
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
                            parameter.Section,
                            "Aligned goals",
                            behaviorResult.BlendExplanation);
                    }
                }

                parameter.BlendStatus =
                    behaviorResult.Status;
            }

            // Session intent remains the higher-level request.
            //
            // If per-car Desired Behavior pulls a recognized parameter in the
            // opposite direction, preserve session intent as the stronger
            // layer and soften the behavior contribution.
            var intentConflict =
                Math.Abs(styleDelta) >
                Epsilon &&

                Math.Abs(behaviorDelta) >
                Epsilon &&

                Math.Sign(styleDelta) !=
                Math.Sign(behaviorDelta);

            if (intentConflict)
            {
                behaviorDelta *=
                    0.65;

                var intentNote =
                    "Session-intent compromise: the selected driving intent and the per-car Desired Behavior target pull this parameter in opposite directions; ADT keeps session intent as the priority and reduces the behavior influence.";

                behaviorReason =
                    string.IsNullOrWhiteSpace(
                        behaviorReason)
                        ? intentNote
                        : behaviorReason.Trim() +
                          " " +
                          intentNote;

                blendReport.IntentConflictCount++;

                AddBlendNotice(
                    blendReport,
                    parameter.Section,
                    "Intent compromise",
                    intentNote);

                parameter.BlendStatus =
                    behaviorResult.HasConflict
                        ? "Behavior + intent compromise"
                        : "Intent compromise";
            }

            var delta =
                styleDelta +
                behaviorDelta;

            if (!double.IsFinite(delta))
            {
                parameter.Reason =
                    "Left unchanged because the combined ADT setup adjustment was not a finite numeric value.";

                continue;
            }

            if (Math.Abs(delta) < Epsilon)
            {
                if (
                    Math.Abs(styleDelta) >
                    Epsilon ||
                    behaviorResult.HasInfluence)
                {
                    parameter.Reason =
                        MergeReasons(
                            styleReason,
                            behaviorReason) +
                        " Blend result: the competing requests resolve to no net saved-value change." +
                        RangeNote(
                            parameter.Range);

                    if (
                        parameter.BlendStatus ==
                        "—")
                    {
                        parameter.BlendStatus =
                            "Balanced out";
                    }
                }

                continue;
            }

            var proposed =
                current +
                delta;

            if (!double.IsFinite(proposed))
            {
                parameter.Reason =
                    "Left unchanged because the proposed ADT setup value was not finite.";

                continue;
            }

            proposed =
                SnapAndClamp(
                    proposed,
                    parameter.Range,
                    current,
                    parameter.CurrentRaw);

            if (!double.IsFinite(proposed))
            {
                parameter.Reason =
                    "Left unchanged because the setup range could not produce a valid finite saved value.";

                continue;
            }

            parameter.RecommendedValue =
                proposed;

            parameter.Reason =
                MergeReasons(
                    styleReason,
                    behaviorReason) +
                RangeNote(
                    parameter.Range);
        }

        analysis.BehaviorBlend =
            blendReport;

        return analysis;
    }

    public BehaviorBlendPreview PreviewBehaviorBlend(
        CarBehaviorTarget? behavior)
    {
        var desiredBehavior =
            CloneBehavior(
                behavior ?? new CarBehaviorTarget());

        desiredBehavior.Normalize();

        var preview =
            new BehaviorBlendPreview
            {
                ActiveBiasCount =
                    desiredBehavior.ActiveBiasCount
            };

        if (desiredBehavior.IsNeutral)
        {
            return preview;
        }

        var groups =
            new Dictionary<
                string,
                List<PreviewContribution>>(
                StringComparer.OrdinalIgnoreCase);

        void Add(
            string group,
            string source,
            double value)
        {
            if (
                !double.IsFinite(value) ||
                Math.Abs(value) <
                Epsilon)
            {
                return;
            }

            if (!groups.TryGetValue(
                    group,
                    out var list))
            {
                list =
                    [];

                groups[group] =
                    list;
            }

            list.Add(
                new PreviewContribution
                {
                    Source =
                        source,

                    Value =
                        value
                });
        }

        Add(
            "front pressure",
            "Front-end bite",
            -0.45 *
            desiredBehavior.FrontEndBite);

        Add(
            "front pressure",
            "Angle stability",
            -0.20 *
            desiredBehavior.AngleStability);

        Add(
            "rear pressure",
            "Rear grip",
            -0.65 *
            desiredBehavior.RearGrip);

        Add(
            "rear pressure",
            "Throttle steering",
            0.40 *
            desiredBehavior.ThrottleSteering);

        Add(
            "rear pressure",
            "Angle stability",
            -0.30 *
            desiredBehavior.AngleStability);

        Add(
            "front toe response",
            "Front-end bite",
            0.45 *
            desiredBehavior.FrontEndBite);

        Add(
            "front toe response",
            "Self-steer speed",
            0.25 *
            desiredBehavior.SelfSteerSpeed);

        Add(
            "front toe response",
            "Initiation",
            0.30 *
            desiredBehavior.InitiationSharpness);

        Add(
            "rear roll stiffness",
            "Rear grip",
            -0.55 *
            desiredBehavior.RearGrip);

        Add(
            "rear roll stiffness",
            "Transition speed",
            0.55 *
            desiredBehavior.TransitionSpeed);

        Add(
            "rear roll stiffness",
            "Angle stability",
            -0.45 *
            desiredBehavior.AngleStability);

        Add(
            "rear roll stiffness",
            "Throttle steering",
            0.30 *
            desiredBehavior.ThrottleSteering);

        Add(
            "rear spring response",
            "Rear grip",
            -0.45 *
            desiredBehavior.RearGrip);

        Add(
            "rear spring response",
            "Transition speed",
            0.35 *
            desiredBehavior.TransitionSpeed);

        Add(
            "rear spring response",
            "Angle stability",
            -0.35 *
            desiredBehavior.AngleStability);

        Add(
            "rear rebound",
            "Transition speed",
            0.55 *
            desiredBehavior.TransitionSpeed);

        Add(
            "rear rebound",
            "Self-steer speed",
            0.30 *
            desiredBehavior.SelfSteerSpeed);

        Add(
            "rear rebound",
            "Angle stability",
            -0.35 *
            desiredBehavior.AngleStability);

        Add(
            "power differential",
            "Throttle steering",
            2.00 *
            desiredBehavior.ThrottleSteering);

        Add(
            "power differential",
            "Rear grip",
            1.20 *
            desiredBehavior.RearGrip);

        Add(
            "power differential",
            "Angle stability",
            0.80 *
            desiredBehavior.AngleStability);

        Add(
            "coast differential",
            "Angle stability",
            1.80 *
            desiredBehavior.AngleStability);

        Add(
            "coast differential",
            "Initiation",
            -1.40 *
            desiredBehavior.InitiationSharpness);

        Add(
            "coast differential",
            "Transition speed",
            -0.80 *
            desiredBehavior.TransitionSpeed);

        Add(
            "brake bias",
            "Initiation",
            -0.45 *
            desiredBehavior.InitiationSharpness);

        Add(
            "brake bias",
            "Angle stability",
            0.25 *
            desiredBehavior.AngleStability);

        foreach (var pair in groups)
        {
            var active =
                pair.Value
                    .Where(
                        contribution =>
                            Math.Abs(
                                contribution.Value) >
                            Epsilon)
                    .ToList();

            if (active.Count < 2)
            {
                continue;
            }

            var hasPositive =
                active.Any(
                    contribution =>
                        contribution.Value >
                        0);

            var hasNegative =
                active.Any(
                    contribution =>
                        contribution.Value <
                        0);

            if (
                hasPositive &&
                hasNegative)
            {
                preview.PotentialConflictGroups++;

                var positive =
                    string.Join(
                        ", ",
                        active
                            .Where(
                                contribution =>
                                    contribution.Value >
                                    0)
                            .Select(
                                contribution =>
                                    contribution.Source)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase));

                var negative =
                    string.Join(
                        ", ",
                        active
                            .Where(
                                contribution =>
                                    contribution.Value <
                                    0)
                            .Select(
                                contribution =>
                                    contribution.Source)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase));

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
                            .Select(
                                contribution =>
                                    contribution.Source)
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase));

                preview.Details.Add(
                    $"{pair.Key}: {sources} align; ADT will damp the combined stack.");
            }
        }

        return preview;
    }

    private static double CalculateDelta(
        TuneInput input,
        CarSetupParameter parameter,
        double scale,
        out string reason)
    {
        reason =
            string.Empty;

        if (
            input.Intent is null ||
            parameter.CurrentValue is null)
        {
            return 0;
        }

        var section =
            NormalizeSection(
                parameter.Section);

        if (section.Length == 0)
        {
            return 0;
        }

        var front =
            IsFrontSection(
                section);

        var rear =
            IsRearSection(
                section);

        var style =
            input.Intent.Kind;

        if (section.StartsWith(
                "PRESSURE_",
                StringComparison.Ordinal))
        {
            var delta =
                style switch
                {
                    DriftStyleKind.Training =>
                        -1,

                    DriftStyleKind.FastSelfSteer =>
                        front
                            ? -1
                            : 1,

                    DriftStyleKind.Tandem =>
                        front
                            ? -1
                            : 0,

                    DriftStyleKind.Competition =>
                        -1,

                    _ =>
                        0
                };

            if (Math.Abs(delta) > Epsilon)
            {
                reason =
                    front
                        ? "Front pressure trimmed for front-end response and grip."
                        : "Rear pressure adjusted for the selected rotation and traction target.";
            }

            return
                delta *
                scale;
        }

        if (section.StartsWith(
                "CAMBER_",
                StringComparison.Ordinal))
        {
            if (
                front &&
                (
                    style ==
                    DriftStyleKind.FastSelfSteer ||
                    style ==
                    DriftStyleKind.Competition
                ))
            {
                reason =
                    "Adds a small amount of front negative-camber bias for response; verify tire temperatures and driver feel.";

                return
                    parameter.CurrentValue.Value <=
                    0
                        ? -1 *
                          scale
                        : 1 *
                          scale;
            }

            if (
                rear &&
                (
                    style ==
                    DriftStyleKind.Tandem ||
                    style ==
                    DriftStyleKind.Competition
                ))
            {
                reason =
                    "Moves rear camber slightly toward a traction-oriented setting.";

                return
                    parameter.CurrentValue.Value <
                    0
                        ? 1 *
                          scale
                        : -1 *
                          scale;
            }
        }

        if (
            section.StartsWith(
                "TOE_OUT_",
                StringComparison.Ordinal) &&
            front &&
            (
                style ==
                DriftStyleKind.FastSelfSteer ||
                style ==
                DriftStyleKind.Competition
            ))
        {
            reason =
                "Small front toe-out increase for initial steering response.";

            return
                1 *
                scale;
        }

        if (section.Contains(
                "ARB",
                StringComparison.Ordinal))
        {
            if (
                rear &&
                style ==
                DriftStyleKind.FastSelfSteer)
            {
                reason =
                    "One-step rear roll-stiffness bias to help rotation; kept small to reduce snap-oversteer risk.";

                return
                    1 *
                    scale;
            }

            if (
                rear &&
                style ==
                DriftStyleKind.Training)
            {
                reason =
                    "Softer rear roll-stiffness bias for a wider stability window.";

                return
                    -1 *
                    scale;
            }
        }

        if (section.Contains(
                "SPRING_RATE",
                StringComparison.Ordinal))
        {
            if (
                rear &&
                style ==
                DriftStyleKind.Training)
            {
                reason =
                    "Small softer-rear bias for progressive breakaway.";

                return
                    -1 *
                    scale;
            }

            if (
                rear &&
                style ==
                DriftStyleKind.FastSelfSteer)
            {
                reason =
                    "Small stiffer-rear bias for faster rotation and transition.";

                return
                    1 *
                    scale;
            }
        }

        if (
            section.Contains(
                "DAMP_REBOUND",
                StringComparison.Ordinal) &&
            rear &&
            (
                style ==
                DriftStyleKind.FastSelfSteer ||
                style ==
                DriftStyleKind.Competition
            ))
        {
            reason =
                "Small rear rebound increase to sharpen transition response.";

            return
                1 *
                scale;
        }

        if (
            section ==
            "DIFF_POWER")
        {
            reason =
                "Raises power-side locking to keep both rear tires driving consistently in drift.";

            return PercentLikeStep(
                parameter.CurrentValue.Value,
                4 *
                scale);
        }

        if (
            section ==
            "DIFF_COAST")
        {
            var delta =
                style switch
                {
                    DriftStyleKind.Training =>
                        3,

                    DriftStyleKind.Tandem =>
                        2,

                    _ =>
                        1
                };

            reason =
                "Adds a modest coast-lock bias for entry and transition stability.";

            return PercentLikeStep(
                parameter.CurrentValue.Value,
                delta *
                scale);
        }

        if (
            section is
                "FRONT_BIAS" or
                "BRAKE_BIAS")
        {
            if (
                style ==
                DriftStyleKind.FastSelfSteer ||
                style ==
                DriftStyleKind.Competition)
            {
                reason =
                    "Moves brake bias slightly rearward to help rotation under braking; test carefully.";

                return
                    -1 *
                    scale;
            }
        }

        if (
            section.Contains(
                "FINAL_RATIO",
                StringComparison.Ordinal) ||
            section.StartsWith(
                "GEAR",
                StringComparison.Ordinal))
        {
            reason =
                "Gearing left unchanged because saved values may be ratio-list indexes whose direction varies by car.";

            return 0;
        }

        if (
            section ==
            "FUEL" ||
            section ==
            "TYRES" ||
            section is
                "ABS" or
                "TC")
        {
            reason =
                "Driver or session choice left unchanged.";

            return 0;
        }

        return 0;
    }

    private static BehaviorDeltaResult CalculateBehaviorDelta(
        CarSetupParameter parameter,
        CarBehaviorTarget behavior,
        double aggressivenessScale)
    {
        var result =
            new BehaviorDeltaResult();

        if (
            behavior.IsNeutral ||
            parameter.CurrentValue is null ||
            !double.IsFinite(
                parameter.CurrentValue.Value))
        {
            return result;
        }

        var section =
            NormalizeSection(
                parameter.Section);

        if (section.Length == 0)
        {
            return result;
        }

        var front =
            IsFrontSection(
                section);

        var rear =
            IsRearSection(
                section);

        // Desired Behavior intentionally remains weaker than the selected
        // Training/Tandem/etc. session intent.
        var scale =
            aggressivenessScale *
            DesiredBehaviorStrength;

        var contributions =
            new List<BehaviorContribution>();

        void Add(
            double value,
            string source,
            string note)
        {
            if (
                !double.IsFinite(value) ||
                Math.Abs(value) <
                Epsilon)
            {
                return;
            }

            contributions.Add(
                new BehaviorContribution
                {
                    Delta =
                        value,

                    Source =
                        source,

                    Note =
                        note
                });
        }

        if (section.StartsWith(
                "PRESSURE_",
                StringComparison.Ordinal))
        {
            if (front)
            {
                Add(
                    -0.45 *
                    behavior.FrontEndBite *
                    scale,
                    "Front-end bite",
                    "front-end bite biases front pressure");

                Add(
                    -0.20 *
                    behavior.AngleStability *
                    scale,
                    "Angle stability",
                    "angle stability preserves front grip");
            }

            if (rear)
            {
                Add(
                    -0.65 *
                    behavior.RearGrip *
                    scale,
                    "Rear grip",
                    "rear-grip target biases rear pressure");

                Add(
                    0.40 *
                    behavior.ThrottleSteering *
                    scale,
                    "Throttle steering",
                    "throttle-steering target biases rear rotation");

                Add(
                    -0.30 *
                    behavior.AngleStability *
                    scale,
                    "Angle stability",
                    "angle stability adds rear traction margin");
            }
        }

        if (section.StartsWith(
                "CAMBER_",
                StringComparison.Ordinal))
        {
            if (
                front &&
                behavior.FrontEndBite !=
                0)
            {
                var direction =
                    parameter.CurrentValue.Value <=
                    0
                        ? -1
                        : 1;

                Add(
                    direction *
                    0.35 *
                    behavior.FrontEndBite *
                    scale,
                    "Front-end bite",
                    "front-end bite biases front camber");
            }

            if (
                rear &&
                behavior.RearGrip !=
                0)
            {
                var towardZero =
                    parameter.CurrentValue.Value <
                    0
                        ? 1
                        : parameter.CurrentValue.Value >
                          0
                            ? -1
                            : 0;

                Add(
                    towardZero *
                    0.30 *
                    behavior.RearGrip *
                    scale,
                    "Rear grip",
                    "rear-grip target biases rear camber toward or away from its traction window");
            }
        }

        if (
            section.StartsWith(
                "TOE_OUT_",
                StringComparison.Ordinal) &&
            front)
        {
            Add(
                0.45 *
                behavior.FrontEndBite *
                scale,
                "Front-end bite",
                "front-end bite biases front toe response");

            Add(
                0.25 *
                behavior.SelfSteerSpeed *
                scale,
                "Self-steer speed",
                "self-steer target biases initial steering response");

            Add(
                0.30 *
                behavior.InitiationSharpness *
                scale,
                "Initiation",
                "initiation target biases turn-in sharpness");
        }

        if (
            section.Contains(
                "ARB",
                StringComparison.Ordinal) &&
            rear)
        {
            Add(
                -0.55 *
                behavior.RearGrip *
                scale,
                "Rear grip",
                "rear-grip target biases rear roll stiffness");

            Add(
                0.55 *
                behavior.TransitionSpeed *
                scale,
                "Transition speed",
                "transition-speed target biases rear roll response");

            Add(
                -0.45 *
                behavior.AngleStability *
                scale,
                "Angle stability",
                "angle-stability target widens the rear stability window");

            Add(
                0.30 *
                behavior.ThrottleSteering *
                scale,
                "Throttle steering",
                "throttle-steering target biases rear rotation");
        }

        if (
            section.Contains(
                "SPRING_RATE",
                StringComparison.Ordinal) &&
            rear)
        {
            Add(
                -0.45 *
                behavior.RearGrip *
                scale,
                "Rear grip",
                "rear-grip target biases rear spring support");

            Add(
                0.35 *
                behavior.TransitionSpeed *
                scale,
                "Transition speed",
                "transition-speed target biases rear spring response");

            Add(
                -0.35 *
                behavior.AngleStability *
                scale,
                "Angle stability",
                "angle-stability target biases a more progressive rear platform");
        }

        if (
            section.Contains(
                "DAMP_REBOUND",
                StringComparison.Ordinal) &&
            rear)
        {
            Add(
                0.55 *
                behavior.TransitionSpeed *
                scale,
                "Transition speed",
                "transition-speed target biases rear rebound");

            Add(
                0.30 *
                behavior.SelfSteerSpeed *
                scale,
                "Self-steer speed",
                "self-steer target biases transition response");

            Add(
                -0.35 *
                behavior.AngleStability *
                scale,
                "Angle stability",
                "angle-stability target tempers rear rebound response");
        }

        if (
            section ==
            "DIFF_POWER")
        {
            Add(
                PercentLikeStep(
                    parameter.CurrentValue.Value,
                    2.00 *
                    behavior.ThrottleSteering *
                    scale),
                "Throttle steering",
                "throttle-steering target biases power-side differential locking");

            Add(
                PercentLikeStep(
                    parameter.CurrentValue.Value,
                    1.20 *
                    behavior.RearGrip *
                    scale),
                "Rear grip",
                "rear-grip target biases power-side differential locking");

            Add(
                PercentLikeStep(
                    parameter.CurrentValue.Value,
                    0.80 *
                    behavior.AngleStability *
                    scale),
                "Angle stability",
                "angle-stability target biases power-side differential locking");
        }

        if (
            section ==
            "DIFF_COAST")
        {
            Add(
                PercentLikeStep(
                    parameter.CurrentValue.Value,
                    1.80 *
                    behavior.AngleStability *
                    scale),
                "Angle stability",
                "angle-stability target biases coast-side differential locking");

            Add(
                PercentLikeStep(
                    parameter.CurrentValue.Value,
                    -1.40 *
                    behavior.InitiationSharpness *
                    scale),
                "Initiation",
                "initiation target biases coast-side differential locking");

            Add(
                PercentLikeStep(
                    parameter.CurrentValue.Value,
                    -0.80 *
                    behavior.TransitionSpeed *
                    scale),
                "Transition speed",
                "transition-speed target biases coast-side differential locking");
        }

        if (
            section is
                "FRONT_BIAS" or
                "BRAKE_BIAS")
        {
            Add(
                -0.45 *
                behavior.InitiationSharpness *
                scale,
                "Initiation",
                "initiation target biases brake balance for entry rotation");

            Add(
                0.25 *
                behavior.AngleStability *
                scale,
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
                HasInfluence =
                    contributions.Count >
                    0
            };

        if (contributions.Count == 0)
        {
            return result;
        }

        var positive =
            contributions
                .Where(
                    contribution =>
                        contribution.Delta >
                        0)
                .ToList();

        var negative =
            contributions
                .Where(
                    contribution =>
                        contribution.Delta <
                        0)
                .ToList();

        var positiveEffective =
            DampedMagnitude(
                positive.Select(
                    contribution =>
                        Math.Abs(
                            contribution.Delta)));

        var negativeEffective =
            DampedMagnitude(
                negative.Select(
                    contribution =>
                        Math.Abs(
                            contribution.Delta)));

        var hasPositive =
            positive.Count >
            0;

        var hasNegative =
            negative.Count >
            0;

        var notes =
            contributions
                .Select(
                    contribution =>
                        contribution.Note)
                .Where(
                    note =>
                        !string.IsNullOrWhiteSpace(
                            note))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        var desiredReason =
            notes.Count == 0
                ? string.Empty
                : "Desired Behavior: " +
                  string.Join(
                      "; ",
                      notes) +
                  ".";

        if (
            hasPositive &&
            hasNegative)
        {
            result.HasConflict =
                true;

            result.Status =
                "Compromise";

            var strongest =
                Math.Max(
                    positiveEffective,
                    negativeEffective);

            var overlap =
                Math.Min(
                    positiveEffective,
                    negativeEffective);

            var conflictRatio =
                strongest <=
                Epsilon
                    ? 0
                    : overlap /
                      strongest;

            // Cancellation already resolves most opposing pressure.
            // Additional softening increases as the competing requests
            // become more similar in magnitude.
            var compromiseScale =
                1.0 -
                (
                    0.25 *
                    conflictRatio
                );

            result.Delta =
                (
                    positiveEffective -
                    negativeEffective
                ) *
                compromiseScale;

            var positiveSources =
                string.Join(
                    ", ",
                    positive
                        .Select(
                            contribution =>
                                contribution.Source)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase));

            var negativeSources =
                string.Join(
                    ", ",
                    negative
                        .Select(
                            contribution =>
                                contribution.Source)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase));

            var reducedPct =
                (int)Math.Round(
                    (
                        1.0 -
                        compromiseScale
                    ) *
                    100.0,
                    MidpointRounding.AwayFromZero);

            result.BlendExplanation =
                $"{positiveSources} and {negativeSources} push this parameter in opposite directions. ADT cancels the overlap, then softens the remaining net request by {reducedPct}% so one goal does not overpower the other.";

            result.Reason =
                MergeReasons(
                    desiredReason,
                    "Blend compromise: " +
                    result.BlendExplanation);

            return result;
        }

        var alignedStack =
            contributions.Count >
            1;

        result.HasAlignedStack =
            alignedStack;

        result.Delta =
            hasPositive
                ? positiveEffective
                : -negativeEffective;

        if (alignedStack)
        {
            result.Status =
                "Aligned / damped";

            var sources =
                string.Join(
                    ", ",
                    contributions
                        .Select(
                            contribution =>
                                contribution.Source)
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase));

            result.BlendExplanation =
                $"{sources} push this parameter in the same direction. ADT applies diminishing returns to the stack so multiple goals do not overcorrect the setup.";

            result.Reason =
                MergeReasons(
                    desiredReason,
                    "Blend alignment: " +
                    result.BlendExplanation);
        }
        else
        {
            result.Status =
                "Single goal";

            result.BlendExplanation =
                "One Desired Behavior goal influences this parameter.";

            result.Reason =
                desiredReason;
        }

        return result;
    }

    private static double DampedMagnitude(
        IEnumerable<double> magnitudes)
    {
        var ordered =
            magnitudes
                .Where(
                    magnitude =>
                        double.IsFinite(
                            magnitude) &&
                        magnitude >
                        Epsilon)
                .OrderByDescending(
                    magnitude =>
                        magnitude)
                .ToList();

        if (ordered.Count == 0)
        {
            return 0;
        }

        var total =
            0.0;

        for (
            var index = 0;
            index < ordered.Count;
            index++)
        {
            var weight =
                index == 0
                    ? 1.0
                    : Math.Max(
                        0.35,
                        Math.Pow(
                            0.70,
                            index));

            total +=
                ordered[index] *
                weight;
        }

        return FiniteOrZero(
            total);
    }

    private static void AddBlendNotice(
        BehaviorBlendReport report,
        string? parameter,
        string kind,
        string summary)
    {
        if (string.IsNullOrWhiteSpace(
                summary))
        {
            return;
        }

        if (
            report.Notices.Count >=
            MaximumBlendNotices)
        {
            return;
        }

        var parameterName =
            string.IsNullOrWhiteSpace(
                parameter)
                ? "Unknown parameter"
                : parameter.Trim();

        var alreadyExists =
            report.Notices.Any(
                notice =>
                    string.Equals(
                        notice.Parameter,
                        parameterName,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        notice.Kind,
                        kind,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        notice.Summary,
                        summary,
                        StringComparison.Ordinal));

        if (alreadyExists)
        {
            return;
        }

        report.Notices.Add(
            new BehaviorBlendNotice
            {
                Parameter =
                    parameterName,

                Kind =
                    kind,

                Summary =
                    summary
            });
    }

    private static double PercentLikeStep(
        double current,
        double requested)
    {
        if (
            !double.IsFinite(current) ||
            !double.IsFinite(requested) ||
            Math.Abs(requested) <
            Epsilon)
        {
            return 0;
        }

        // Common AC differential values are percent-like.
        //
        // Very small saved values are more likely to represent click/index
        // positions, so preserve whole-step semantics rather than pretending
        // they are literal percentages.
        if (Math.Abs(current) >= 10)
        {
            return requested;
        }

        return
            Math.Sign(requested) *
            Math.Max(
                1,
                Math.Round(
                    Math.Abs(requested) /
                    4.0,
                    MidpointRounding.AwayFromZero));
    }

    private static double SnapAndClamp(
        double value,
        SetupRangeDefinition? range,
        double current,
        string? currentRaw)
    {
        if (
            !double.IsFinite(value) ||
            !double.IsFinite(current))
        {
            return current;
        }

        var compatible =
            IsCompatibleNumericRange(
                range,
                current);

        if (compatible)
        {
            if (
                range!.Step is double step &&
                double.IsFinite(step) &&
                step >
                0)
            {
                var origin =
                    range.Min is double minimum &&
                    double.IsFinite(minimum)
                        ? minimum
                        : 0;

                var stepPosition =
                    (
                        value -
                        origin
                    ) /
                    step;

                if (double.IsFinite(
                        stepPosition))
                {
                    value =
                        origin +
                        Math.Round(
                            stepPosition,
                            MidpointRounding.AwayFromZero) *
                        step;
                }
            }

            if (
                range.Min is double minimumValue &&
                double.IsFinite(
                    minimumValue))
            {
                value =
                    Math.Max(
                        value,
                        minimumValue);
            }

            if (
                range.Max is double maximumValue &&
                double.IsFinite(
                    maximumValue))
            {
                value =
                    Math.Min(
                        value,
                        maximumValue);
            }
        }
        else if (
            IsIntegralRawValue(
                currentRaw))
        {
            // Unknown/click-index mappings stay on whole saved-value steps
            // when the baseline itself was stored as an integer.
            value =
                Math.Round(
                    value,
                    MidpointRounding.AwayFromZero);
        }

        return
            double.IsFinite(value)
                ? value
                : current;
    }

    private static bool IsCompatibleNumericRange(
        SetupRangeDefinition? range,
        double current)
    {
        if (
            range is null ||
            range.ShowClicks ||
            !double.IsFinite(current))
        {
            return false;
        }

        if (
            range.Min is double minimum &&
            !double.IsFinite(minimum))
        {
            return false;
        }

        if (
            range.Max is double maximum &&
            !double.IsFinite(maximum))
        {
            return false;
        }

        if (
            range.Min is double min &&
            range.Max is double max &&
            min >
            max)
        {
            return false;
        }

        if (
            range.Step is double step &&
            (
                !double.IsFinite(step) ||
                step <= 0
            ))
        {
            return false;
        }

        if (
            range.Min is double minimumValue &&
            current <
            minimumValue -
            0.0001)
        {
            return false;
        }

        if (
            range.Max is double maximumValue &&
            current >
            maximumValue +
            0.0001)
        {
            return false;
        }

        return true;
    }

    private static bool IsIntegralRawValue(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(
                raw))
        {
            return false;
        }

        var trimmed =
            raw.Trim();

        return
            !trimmed.Contains(
                '.') &&
            !trimmed.Contains(
                ',');
    }

    private static string RangeNote(
        SetupRangeDefinition? range)
    {
        return range switch
        {
            null =>
                " Range metadata unavailable; change is baseline-relative.",

            { ShowClicks: true } =>
                " setup.ini uses click display; physical MIN/MAX mapping is not forced onto the saved raw value.",

            _ =>
                " Numeric setup.ini metadata is used only when the baseline raw value is compatible with that range."
        };
    }

    private static string MergeReasons(
        string? first,
        string? second)
    {
        var hasFirst =
            !string.IsNullOrWhiteSpace(
                first);

        var hasSecond =
            !string.IsNullOrWhiteSpace(
                second);

        if (
            hasFirst &&
            hasSecond)
        {
            return
                first!.Trim() +
                " " +
                second!.Trim();
        }

        if (hasFirst)
        {
            return first!.Trim();
        }

        if (hasSecond)
        {
            return second!.Trim();
        }

        return
            "Conservative drift recommendation.";
    }

    private static string NormalizeSection(
        string? section)
    {
        if (string.IsNullOrWhiteSpace(
                section))
        {
            return string.Empty;
        }

        return
            section
                .Trim()
                .ToUpperInvariant();
    }

    private static bool IsFrontSection(
        string section)
    {
        return
            section.EndsWith(
                "_LF",
                StringComparison.Ordinal) ||
            section.EndsWith(
                "_RF",
                StringComparison.Ordinal) ||
            section.Contains(
                "FRONT",
                StringComparison.Ordinal);
    }

    private static bool IsRearSection(
        string section)
    {
        return
            section.EndsWith(
                "_LR",
                StringComparison.Ordinal) ||
            section.EndsWith(
                "_RR",
                StringComparison.Ordinal) ||
            section.Contains(
                "REAR",
                StringComparison.Ordinal);
    }

    private static double AggressivenessScale(
        SetupAggressiveness aggressiveness)
    {
        return aggressiveness switch
        {
            SetupAggressiveness.Conservative =>
                0.55,

            SetupAggressiveness.Aggressive =>
                1.45,

            _ =>
                1.0
        };
    }

    private static double FiniteOrZero(
        double value)
    {
        return double.IsFinite(value)
            ? value
            : 0;
    }

    private static CarBehaviorTarget CloneBehavior(
        CarBehaviorTarget source)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        return new CarBehaviorTarget
        {
            Key =
                source.Key,

            DisplayName =
                source.DisplayName,

            UpdatedUtc =
                source.UpdatedUtc,

            FrontEndBite =
                source.FrontEndBite,

            RearGrip =
                source.RearGrip,

            SelfSteerSpeed =
                source.SelfSteerSpeed,

            TransitionSpeed =
                source.TransitionSpeed,

            AngleStability =
                source.AngleStability,

            ThrottleSteering =
                source.ThrottleSteering,

            InitiationSharpness =
                source.InitiationSharpness
        };
    }

    private sealed class BehaviorContribution
    {
        public string Source { get; set; } =
            string.Empty;

        public string Note { get; set; } =
            string.Empty;

        public double Delta { get; set; }
    }

    private sealed class BehaviorDeltaResult
    {
        public double Delta { get; set; }

        public string Reason { get; set; } =
            string.Empty;

        public string Status { get; set; } =
            "—";

        public string BlendExplanation { get; set; } =
            string.Empty;

        public bool HasInfluence { get; set; }

        public bool HasConflict { get; set; }

        public bool HasAlignedStack { get; set; }
    }

    private sealed class PreviewContribution
    {
        public string Source { get; set; } =
            string.Empty;

        public double Value { get; set; }
    }
}
