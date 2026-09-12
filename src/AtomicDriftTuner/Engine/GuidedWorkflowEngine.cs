using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
namespace AtomicDriftTuner.Engine;

public static class GuidedWorkflowEngine
{
    public static bool CanAdvanceFromRun(TelemetrySession session, TelemetryAnalysis analysis) =>
        analysis.DriftTimeSeconds >= 20 && DriftDiagnosisEngine.Reliable(session, analysis);
    public static string TuneSignature(TuneResult result) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { result.Ac, result.Azom })));

    public static GuidedStep Next(GuidedPreferences p, GuidedJourney j, string currentGoal)
    {
        var ffb = TuningFocusOptions.IncludesFfb(p.Focus);
        var car = TuningFocusOptions.IncludesCar(p.Focus);
        GuidedStep Step(GuidedStage stage, string title, string simple, string action, string details, string done) =>
            new(stage, title, simple, action) { Details = details, Completion = done };
        if (!p.Completed) return Step(GuidedStage.Welcome, "Welcome · Choose what to tune", "Choose FFB, car setup, or both. Tell ADT about your optional tools, then save your choices.", "Set Up My Workflow",
            "FFB (force feedback) is the feeling through your steering wheel. Car setup changes how the car handles, including suspension, grip and supported gearing. SimHub is an optional companion program; AZOM is an optional SimHub plugin for supported wheelbase settings. 'Not sure' is fine: use Check for Me. You can always use the manual path.", "Save & Continue in Setup & Paths. No settings are applied by answering these questions.");
        if (!j.CarConfirmed) return Step(GuidedStage.Car, "1 · Choose your car and driver", "Under Car & Hardware, scan AC and select the installed car you will drive. Check your wheelbase, rim and driver name.", "Confirm This Car & Rig",
            "Start Assetto Corsa or Content Manager with that same car. If ADT's car list is empty, choose the Assetto Corsa installation folder and scan it. Hardware is recorded even in car-setup-only mode so comparisons stay tied to the same rig. Use the same driver name each time.", "ADT and the game show the same car, and the driver and rig selections are correct.");
        if (j.GoalSignature.Length == 0) return Step(GuidedStage.Goals, "2 · Describe how you want it to feel", "Open Desired Behavior, choose your driving goals and click Save Desired Behavior. Then return here and confirm them.", "Use Saved Desired Behavior",
            "Desired Behavior is your goal, not a setting applied to the game. Start Neutral if you are unsure. Move a slider only when you know what you want: for example, calmer transitions or faster self-steer. ADT still studies initiation, transitions, front/rear balance, self-steer, stability and oscillation in every mode. " +
            (car ? "The car setup tuner uses these goals for supported adjustments." : "Your goals help interpret the telemetry. This FFB workflow does not ask you to change the car setup."), "Your goals are saved for this car. Keep them unchanged during a before/after test.");
        if (j.GoalSignature != currentGoal) return Step(GuidedStage.GoalsChanged, "Your driving goals changed", "Start a fresh baseline for the new goals. The earlier runs and reviews remain saved.", "Start a New Baseline",
            "Comparing runs against different goals would move the goalposts. This restarts only this workflow's baseline/test progress; it does not delete tune versions, recordings, ratings or progress in another tuning mode.", "A new baseline is recorded using the goals now selected.");
        if (!j.TuneReady && j.BaselineId.Length == 0) return Step(GuidedStage.Prepare, "3 · Save and prepare your starting tune",
            ffb ? "Save your current settings first. Generate and review the FFB recommendation, enter/apply the settings you choose, then confirm below." :
                "In AC's pits, save your current car setup with a clear name, then load that setup. Keep your FFB settings unchanged and confirm below.",
            ffb && !j.TuneGenerated ? "Generate & Review FFB" : "Confirm Starting Tune Is Ready",
            "First keep a recovery copy: in AC's pits open Setup and save a named baseline (for example, MyCar_Baseline). For FFB, write down or screenshot your current in-game and wheelbase settings. " +
            (ffb ? "Generate & Review FFB creates suggested numbers in ADT. It does not apply them. Use the connection instructions below to enter/apply supported settings, and check that the values took effect. " : "You can start with the car's current setup; no FFB generation or calibration is required. ") +
            (car ? "If you want an ADT starting car setup, use AC Setup: Load Baseline → Generate Car Setup → Save AC Setup File under a NEW name → load that file in the game's Setup menu. Gearing is a separate optional planner for supported cars. " : "Keep the baseline car setup unchanged. ") +
            "A file saved by ADT is not loaded into the game until you choose it there.", TuningFocusOptions.Confirmation(p.Focus));
        if (j.BaselineId.Length == 0) return Step(GuidedStage.Baseline, "4 · Record your first drive", "Open the recorder, attach the setup you actually loaded, describe the conditions and confirm what you are using. Connect → Record → drive → Stop → Save Session.", "Record Baseline",
            RecordingHelp(p.Focus, false), "Save Session finishes successfully. Aim for 60–120 seconds; guided progress requires at least 20 seconds of clean, reliably sampled drifting.");
        if (j.Reviewed) return Step(GuidedStage.Complete, "8 · Decide what to do next", "Your review is saved. This does not mean the tune improved. Choose whether to keep the change, revert manually, or test it again.", "Open Saved Comparison",
            "A saved review does not mean the tune improved. Read the measured result and your own rating together. Keep: save the preferred setup/settings and repeat a comparable run. Revert: load your original baseline car setup and/or manually restore the earlier FFB values shown in Tune & Run History. Test again: use Record Comparison Run to repeat the same change. No keep/revert choice applies settings automatically.", "Your rating, notes and next action are saved. Confirm the result with repeated comparable runs.");
        if (j.AfterId.Length > 0) return Step(GuidedStage.Compare, "7 · Compare the two drives", "Open Before / After. Read the result and its limitations. In Tune & Run History, rate how it felt, add notes, choose a next action and save the review.", "Compare With Baseline",
            "Check that the correct earlier baseline is selected. 'Inconclusive' means ADT cannot establish a fair comparison, not that you failed. Different tracks, goals, conditions, unreliable samples or several changes can prevent a useful conclusion. The full diagnosis remains visible. Choose Better, Worse, No noticeable difference or Tradeoff based on your experience; your rating does not overwrite the measured result.", "Click Save Run Review after choosing a rating. Saving a session and saving a review are separate actions.");
        if (j.Recommendation.Length > 0) return Step(GuidedStage.Test, "6 · Make one change and drive again", "Prepare the selected change, then repeat the same section in the same conditions. Record, stop and save another session.", "Record Comparison Run",
            (car ? "For a car change, open AC Setup with Guidance, review the proposed values, save a new .ini and load it in AC. " : "Keep the car setup fixed. ") +
            (ffb ? "For an FFB change, review the recommendation or apply the proposed ADT calibration, generate the updated values, then enter/apply and verify those values. Applying an ADT calibration updates its recommendations; it does not by itself change game or wheelbase settings. " : "Keep the in-game and wheelbase FFB settings fixed. ") +
            "Change only the setting you are testing; leave Desired Behavior unchanged. " + RecordingHelp(p.Focus, true), "The comparison recording is saved with the right baseline, actual setup and change description.");
        return Step(GuidedStage.Findings, "5 · Read the findings and choose one test", "In Recommendations, select one relevant row and click Test This Recommendation. Read what to change, why, and the confidence first.", "Review Baseline Findings",
            "Assessments and phase evidence explain what ADT observed. Recommendations are filtered to your tuning choice, but all diagnosis and history remain available. A low-confidence or missing recommendation is a reason to collect a cleaner run, not to guess. Pick one change, keep the same goals and use the dashboard to prepare the next run.", "The dashboard names the selected test. Selecting it creates a plan; it does not apply anything.");
    }

    public static string RecordingHelp(TuningFocus focus, bool comparison) =>
        "1. Load the selected car and track in AC and enter a driving session.\n2. In ADT Recorder, check Driver and Conditions (example: dry, same layout, solo transitions). Attach the exact .ini loaded in AC; ADT cannot detect that file automatically. " +
        (focus == TuningFocus.FfbOnly ? "Attaching the unchanged car setup is only a snapshot for a fair comparison; it does not tune the car. " : "") +
        "\n3. Tick the confirmation only if it is true, then click Connect. Live values should update when you drive.\n4. Click Record before your test section. Drive for roughly 60–120 seconds with several entries and transitions.\n5. Return to ADT and click Stop, then Save Session. Stop alone does not save.\n6. Click " +
        (comparison ? "Compare With Baseline. Use the same line, conditions and task as the first drive." : "Review Baseline Findings. Your first recording is the reference for later changes.") +
        "\nTelemetry comes directly from AC; SimHub and AZOM are not required to record.";

    public static string Instructions(GuidedPreferences p, IntegrationState? s)
    {
        if (!p.ShowDetailedHelp)
        {
            if (!TuningFocusOptions.IncludesFfb(p.Focus)) return "Car setup: save a baseline → create a new setup in ADT if wanted → load the chosen .ini in AC's pits. Keep FFB fixed. SimHub/AZOM are not needed. Enable more explanation for the exact steps.";
            if (!p.WantLiveConnection || p.SimHub == "No" || p.Azom == "No") return "Manual FFB: enter AC FFB in Controls → Force Feedback and supported wheelbase values in its manufacturer's software. SimHub/AZOM are optional. Enable more explanation for menus and verification steps.";
            if (s is { BridgeConnected: true, AzomDetected: true, SettingsReadable: true }) return "AZOM readback is ready: review supported wheelbase changes, Apply explicitly, then verify readback. Enter AC FFB separately. Enable more explanation for the full instructions.";
            return "Live connection is not verified yet. Check for Me / Check Again, enable more explanation for troubleshooting, or choose manual guidance in Change My Setup.";
        }
        if (!TuningFocusOptions.IncludesFfb(p.Focus))
            return "CAR SETUP ONLY: save a named baseline in AC's pits → Setup. For an ADT change: AC Setup → Load Baseline → Generate Car Setup → Save AC Setup File under a new name → load that file in AC. Keep your FFB and wheelbase settings fixed. SimHub and AZOM are not needed for this path or for recording telemetry.";
        const string manual = "MANUAL FFB\n1. Write down or screenshot your current FFB and wheelbase values.\n2. In Content Manager, open Settings → Assetto Corsa → Controls → Force Feedback (or AC's Options → Controls → Force Feedback).\n3. Enter the recommended AC FFB values there.\n4. Open your wheelbase manufacturer's software. Enter only matching settings supported by your model; AZOM/MOZA-specific values do not map to every wheelbase.\n5. Check that the values took effect before recording.";
        var car = TuningFocusOptions.IncludesCar(p.Focus) ? " CAR SETUP: save the AC setup file under a new name, then load it from AC's Setup menu in the pits." : " Keep your existing car setup fixed; FFB settings in AC's Controls menu are part of FFB-only tuning.";
        var common = manual + car;
        if (!p.WantLiveConnection || p.SimHub == "No" || p.Azom == "No")
            return common + (p.SimHub == "Yes" && p.Azom == "No" ? " SimHub by itself does not provide AZOM wheelbase control." : "") + " SimHub/AZOM live control is optional. Choose Change My Setup if you want connection help later.";
        if (s is null) return common + " Optional live control: choose Check for Me / Check Again to find out what ADT can reach now.";
        if (s.BridgeConnected && s.AzomDetected && s.SettingsReadable)
            return "LIVE WHEELBASE: AZOM readback is available. Open Wheelbase Settings → review the supported changes → choose Apply explicitly → wait for verified readback. This checks supported settings only; it does not promise support for every wheelbase. Enter AC's in-game FFB settings separately in Controls → Force Feedback." + car + " A connection check applies nothing.";
        if (!s.SimHubInstalled && !s.BridgeConnected) return common + " SimHub's folder was not found. Locate an existing installation in Setup & Paths or continue manually. Not found does not prove it is absent.";
        if (!s.SimHubRunning && !s.BridgeConnected) return common + " SimHub is installed but is not running. Start it, then Check Again. AZOM availability is unknown while the bridge is offline.";
        if (!s.BridgeInstalled && !s.BridgeConnected) return common + " In Setup & Paths use Install / Repair ADT Bridge while SimHub is fully closed. Start SimHub, enable its ADT bridge plugin and Check Again.";
        if (!s.BridgeConnected) return common + " SimHub is running, but the ADT bridge did not answer. Enable the bridge in SimHub, restart if requested and Check Again. AZOM availability is unverified.";
        if (!s.AzomDetected) return common + " The bridge answered but did not detect AZOM. Check that AZOM is installed and enabled in SimHub, then Check Again. You can continue manually.";
        return common + " AZOM was detected, but settings are not readable yet. Connect/power your supported wheelbase, check AZOM and Check Again, or continue manually.";
    }
}
