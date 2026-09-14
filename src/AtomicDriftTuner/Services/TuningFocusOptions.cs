using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Workflow/presentation choices. These never change diagnosis or tuning calculations.</summary>
public static class TuningFocusOptions
{
    public sealed record Option(TuningFocus Focus, string Label)
    {
        public override string ToString() => Label;
    }
    // New work offers two choices. Retain FfbOnly for readable, immutable older runs.
    public static IReadOnlyList<Option> Current { get; } = [new(TuningFocus.CarSetupOnly, "Car tuning only"), new(TuningFocus.Both, "Car + FFB")];
    public static IReadOnlyList<Option> All { get; } = [new(TuningFocus.Both, "Car + FFB"), new(TuningFocus.FfbOnly, "FFB only"), new(TuningFocus.CarSetupOnly, "Car tuning only")];
    public static bool IsCurrent(TuningFocus focus) => focus is TuningFocus.Both or TuningFocus.CarSetupOnly;
    public static string Label(TuningFocus focus) => All.Single(x => x.Focus == focus).Label;
    public static bool IncludesFfb(TuningFocus focus) => focus is TuningFocus.Both or TuningFocus.FfbOnly;
    public static bool IncludesCar(TuningFocus focus) => focus is TuningFocus.Both or TuningFocus.CarSetupOnly;
    public static bool Allows(TuningFocus focus, AssistantRecommendation recommendation) => recommendation.Area switch
    {
        RecommendationArea.Ffb => IncludesFfb(focus), RecommendationArea.CarSetup => IncludesCar(focus), _ => true
    };
    public static string Description(TuningFocus focus) => focus switch
    {
        TuningFocus.FfbOnly => "Tune wheel feel: steering weight, feedback and wheel response. Keep the car setup fixed; attaching it to a recording is a record of what you drove, not a request to tune it.",
        TuningFocus.CarSetupOnly => "Improve the car's grip, handling and supported gearing. Keep your current steering-wheel feedback settings. SimHub and AZOM are not needed.",
        _ => "Improve the car's handling and the feeling through your steering wheel. FFB means force feedback. ADT will guide you through both, testing one change at a time."
    };
    public static string Confirmation(TuningFocus focus) => focus switch
    {
        TuningFocus.FfbOnly => "I entered/applied the FFB settings I will test, and I will keep the same car setup.",
        TuningFocus.CarSetupOnly => "I loaded my chosen car setup in AC, and I will keep my FFB and wheelbase settings unchanged.",
        _ => "I reviewed and entered/applied the FFB settings, and loaded my chosen car setup in AC."
    };
}
