using AtomicDriftTuner.Models;

namespace AtomicDriftTuner.Services;

public static class FfbProviderOptions
{
    public sealed record Option(FfbProvider Provider, string Label) { public override string ToString() => Label; }
    public static IReadOnlyList<Option> All { get; } = Array.AsReadOnly(new[] {
        new Option(FfbProvider.SimHubAzom, "SimHub / AZOM"),
        new Option(FfbProvider.MozaPitHouse, "MOZA Pit House"),
        new Option(FfbProvider.LogitechG27, "Logitech G27 / legacy Profiler (manual)"),
        new Option(FfbProvider.Manual, "Other wheelbase software / manual") });
    public static string Label(FfbProvider provider) => All.Single(x => x.Provider == provider).Label;
    public static bool UsesAzom(GuidedPreferences p) => p.FfbProvider == FfbProvider.SimHubAzom;
    public static bool AzomSelected()
    {
        try { return UsesAzom(new GuidedWorkflowStore().Preferences()); }
        catch { return false; } // Unreadable preferences must never enable remote writes.
    }
    public static void Require(FfbProvider expected) => Require(expected, new GuidedWorkflowStore().Preferences().FfbProvider);
    public static void Require(FfbProvider expected, FfbProvider actual)
    {
        if (expected != actual) throw new InvalidOperationException($"Select {Label(expected)} in Setup & Paths before using its connection. Current choice: {Label(actual)}.");
    }
    public static string PitHouseInstructions(bool detailed) => detailed
        ? "MOZA PIT HOUSE\n1. Save or screenshot your current Pit House profile.\n2. Generate your FFB recommendation, then open Wheelbase Settings in ADT. The Pit House view lists the core settings ADT can translate. Enter these in Pit House manually, or use the optional SDK connection.\n3. For the experimental SDK connection, install MOZA's SDK-compatible Pit House on the wheelbase PC and select the matching SDK_CSharp/x64 folder (x86 for 32-bit ADT). Click Read Pit House to check the connected base. Review current and proposed values before explicitly applying selected changes.\n4. Enter AC's in-game FFB separately in Controls → Force Feedback. Check the actual settings before recording.\n\nSimHub/AZOM are not required for this choice. SDK hardware compatibility still needs beta testing. AZOM-only effects, equalizer/curve controls and steering limits are not sent through this first SDK version. Do not let AZOM or another tool change the base during a Pit House test."
        : "MOZA Pit House: generate FFB → open Wheelbase Settings → enter matching values in Pit House, or Read Pit House using the optional SDK → review and explicitly apply selected changes → verify. Enter AC FFB separately. SimHub/AZOM are not required. The SDK connection is experimental; enable more explanation for setup steps.";
}
