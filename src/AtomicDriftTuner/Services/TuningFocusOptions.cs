using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

/// <summary>Workflow/presentation choices. These never change diagnosis or tuning calculations.</summary>
public static class TuningFocusOptions
{
    public sealed record Option(TuningFocus Focus, string Label)
    {
        public override string ToString() => Label;
    }
    public static IReadOnlyList<Option> All { get; } = [new(TuningFocus.Both, "FFB + car setup"), new(TuningFocus.FfbOnly, "FFB only"), new(TuningFocus.CarSetupOnly, "Car setup only")];
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
        TuningFocus.CarSetupOnly => "Tune the car: grip, handling and supported gearing. Keep your wheelbase and in-game FFB settings fixed. SimHub and AZOM are not needed for this path.",
        _ => "Tune wheel feel and the car's handling. FFB means force feedback through your steering wheel. Car setup means suspension, grip and gearing. Test one change at a time."
    };
    public static string Confirmation(TuningFocus focus) => focus switch
    {
        TuningFocus.FfbOnly => "I entered/applied the FFB settings I will test, and I will keep the same car setup.",
        TuningFocus.CarSetupOnly => "I loaded my chosen car setup in AC, and I will keep my FFB and wheelbase settings unchanged.",
        _ => "I reviewed and entered/applied the FFB settings, and loaded my chosen car setup in AC."
    };
}
