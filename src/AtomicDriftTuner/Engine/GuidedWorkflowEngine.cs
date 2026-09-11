using AtomicDriftTuner.Models;
namespace AtomicDriftTuner.Engine;

public static class GuidedWorkflowEngine
{
    public static bool CanAdvanceFromRun(TelemetrySession session, TelemetryAnalysis analysis) =>
        analysis.DriftTimeSeconds >= 20 && DriftDiagnosisEngine.Reliable(session, analysis);
    public static string TuneSignature(TuneResult result) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { result.Ac, result.Azom })));
    public static GuidedStep Next(GuidedPreferences preferences, GuidedJourney j, string currentGoal)
    {
        if (!preferences.Completed) return new(GuidedStage.Welcome, "Welcome — personalize your instructions", "Tell ADT which optional integrations you use. AC setup and telemetry work without SimHub or AZOM.", "Set Up My Workflow");
        if (!j.CarConfirmed) return new(GuidedStage.Car, "1 · Confirm your car and driver", "Review Car & Hardware below, including the installed car and drift pack. Choose your driver name here so progress and run comparisons belong to you.", "Confirm This Car & Rig");
        if (j.GoalSignature.Length == 0) return new(GuidedStage.Goals, "2 · Set Desired Behavior", "Open AC Car Setup to choose how this car should feel, then save Desired Behavior for this car. A neutral target is valid. Return here and confirm the saved target.", "Use Saved Desired Behavior");
        if (j.GoalSignature != currentGoal) return new(GuidedStage.GoalsChanged, "Your Desired Behavior changed", "Start a fresh baseline with these goals. Existing runs and reviews remain in history; comparing different goals cannot establish improvement.", "Start a New Baseline");
        if (!j.TuneReady && j.BaselineId.Length == 0) return new(GuidedStage.Prepare, "3 · Prepare your baseline tune", "Generate and review the ADT recommendation. Load your chosen AC setup in the game and enter the recommended FFB/wheelbase settings using the instructions below. Generating and saving a profile do not apply settings.", j.TuneGenerated ? "Confirm Tune Is Ready" : "Generate & Review Tune");
        if (j.BaselineId.Length == 0) return new(GuidedStage.Baseline, "4 · Record your baseline", "Connect to AC, attach the setup actually used, describe conditions, confirm tune use and record. Aim for 60–120 seconds with several initiations and transitions. Stop, then Save Session.", "Record Baseline");
        if (j.Reviewed) return new(GuidedStage.Complete, "8 · Review saved — repeat or start another test", "Your rating and measured outcome are saved. Complete means the review was saved; it does not mean the tune improved. Inspect the result and repeat comparable runs before keeping a change.", "Open Saved Comparison");
        if (j.AfterId.Length > 0) return new(GuidedStage.Compare, "7 · Compare and rate the change", "Review Before / After and the tune differences. In Tune & Run History, choose Better, Worse, No noticeable difference or Tradeoff, add notes and Save Run Review.", "Compare With Baseline");
        if (j.Recommendation.Length > 0) return new(GuidedStage.Test, "6 · Test one recommended change", "Make the selected change and load/use the revised tune. The recorder will carry your driver, baseline, conditions and recommendation forward. Attach the new AC setup and confirm actual tune use again.", "Record Comparison Run");
        return new(GuidedStage.Findings, "5 · Review findings and choose a test", "Open Recommendations, select one recommendation and choose Test This Recommendation. Review the evidence and change only one thing. Keep Desired Behavior fixed during the comparison.", "Review Baseline Findings");
    }

    public static string Instructions(GuidedPreferences p, IntegrationState? s)
    {
        const string manual = "Core workflow: save the AC setup file, load it from AC's Setup menu, and enter the recommended AC FFB settings. For your wheelbase, use its own software and only settings your model supports.";
        if (!p.WantLiveConnection || p.SimHub == "No" || p.Azom == "No")
            return manual + " SimHub/AZOM live control is optional. Change My Setup lets you enable it later.";
        if (s is null) return manual + " Live control requested: use Check My Connection to verify what is available now.";
        if (s.BridgeConnected && s.AzomDetected && s.SettingsReadable)
            return "AZOM readback is available. Open Wheelbase Settings, review supported changes and choose Apply explicitly; wait for verified readback. Save/load AC setup files and AC FFB separately. This connection check does not apply any setting.";
        if (!s.SimHubInstalled && !s.BridgeConnected)
            return manual + " SimHub's folder was not found. In Setup & Paths, locate an existing installation or choose the manual workflow. Not found does not prove it is absent.";
        if (!s.SimHubRunning && !s.BridgeConnected)
            return manual + " SimHub is installed but is not running. Start SimHub, then Check Again. AZOM availability is unknown while the bridge is offline.";
        if (!s.BridgeInstalled && !s.BridgeConnected)
            return manual + " Start with Setup & Paths → Install / Repair ADT Bridge (SimHub must be closed). Then start SimHub, enable its ADT bridge plugin and Check Again.";
        if (!s.BridgeConnected) return manual + " SimHub is running; the ADT bridge did not answer. Enable the bridge in SimHub, restart SimHub if requested and Check Again. AZOM availability is unverified.";
        if (!s.AzomDetected) return manual + " The ADT bridge answered but did not detect AZOM. Check that AZOM is installed and enabled in SimHub, then Check Again.";
        return manual + " AZOM was detected, but settings are not readable yet. Connect/power your supported wheelbase, check AZOM, then Check Again.";
    }
}
