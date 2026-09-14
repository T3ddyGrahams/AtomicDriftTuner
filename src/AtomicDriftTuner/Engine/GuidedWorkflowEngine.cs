using AtomicDriftTuner.Models;
using AtomicDriftTuner.Services;
namespace AtomicDriftTuner.Engine;

public static class GuidedWorkflowEngine
{
    public static bool CanAdvanceFromRun(TelemetrySession session, TelemetryAnalysis analysis) =>
        analysis.DriftTimeSeconds >= 20 && DriftDiagnosisEngine.Reliable(session, analysis);
    public static string TuneSignature(TuneResult result) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { result.Ac, result.Azom })));

    public static string Overview(TuningFocus focus) =>
        "Your route: choose your car → save your goals → prepare " +
        (focus == TuningFocus.CarSetupOnly ? "your car setup" : focus == TuningFocus.FfbOnly ? "your FFB settings" : "your car setup and FFB") +
        " → record a baseline → test one change → compare and decide.";

    public static string ComingNext(GuidedStage stage) => stage switch
    {
        GuidedStage.Welcome => "Next: choose the car and driver for your first test.",
        GuidedStage.Car => "Next: describe how you want this car to behave.",
        GuidedStage.Goals or GuidedStage.GoalsChanged => "Next: save a starting setup and prepare a fresh baseline.",
        GuidedStage.Prepare => "Next: record your first drive as the baseline.",
        GuidedStage.Baseline => "Next: read what ADT noticed and choose one test.",
        GuidedStage.Findings => "Next: prepare that one change and repeat the same drive.",
        GuidedStage.Test => "Next: compare the new drive with your baseline.",
        GuidedStage.Compare => "Next: save how it felt and choose whether to keep, revert or test again.",
        _ => "Next: verify your preferred settings with another comparable drive."
    };

    public static string StartToFinish(TuningFocus focus) =>
        "1. Choose your own car, hardware and driver. Scan AC to find your installed cars.\n" +
        "2. Save Desired Behavior: tell ADT how you want this car to feel.\n" +
        (focus == TuningFocus.CarSetupOnly
            ? "3. Save a starting car setup in AC. Keep your FFB settings fixed.\n"
            : "3. Save a starting car setup and note your FFB settings. Review ADT's recommendations, enter or explicitly apply the chosen settings, then confirm they are in use.\n") +
        "4. Open Recorder: Connect → Record → drive → Stop → Save Session. This first drive is your baseline.\n" +
        "5. Open the findings. Read Your next step, then choose Plan this test for one recommendation.\n" +
        "6. Prepare that one change. Repeat the same drive and save another session.\n" +
        "7. Open Before / After. Compare the evidence and rate how it felt.\n" +
        "8. Save Run Review with your decision: keep, revert manually, or test again.\n\n" +
        "Changing your workflow keeps earlier recordings, tune versions, goals and reviews. Stop alone does not save a run. Generating or planning a change does not apply it.";

    public static GuidedStep Next(GuidedPreferences p, GuidedJourney j, string currentGoal)
    {
        var ffb = TuningFocusOptions.IncludesFfb(p.Focus);
        var car = TuningFocusOptions.IncludesCar(p.Focus);
        GuidedStep Step(GuidedStage stage, string title, string simple, string action, string details, string done) =>
            new(stage, title, simple, action) { Details = details, Completion = done };
        if (!p.Completed || !p.FocusChoiceConfirmed) return Step(GuidedStage.Welcome, "Start here · Choose what to tune", "Open setup and choose Car tuning only or Car + FFB. Enter your driver name, then Save & Continue.", "Set Up My Workflow",
            "Car tuning changes how the car handles, including grip, suspension and supported gearing. FFB (force feedback) is the feeling through your steering wheel. Car tuning only keeps your FFB settings fixed. Car + FFB also guides you through wheel feedback; SimHub and AZOM are optional. You can change this choice later without deleting earlier runs.", "Save & Continue remembers your choice. It does not apply car or wheelbase settings.");
        if (!j.CarConfirmed) return Step(GuidedStage.Car, "1 · Choose your car and driver", "Under Car & Hardware, scan AC and choose the installed car you will drive. Confirm your driver and current rig.", "Confirm This Car & Rig",
            "Start Assetto Corsa or Content Manager with that same car. If ADT's car list is empty, choose the Assetto Corsa installation folder and scan it. Hardware is recorded so comparisons stay tied to the same rig. Use the same driver name each time.", "ADT and the game show the same car, and the driver and rig selections are correct.");
        if (j.GoalSignature.Length == 0) return Step(GuidedStage.Goals, "2 · Tell ADT what you want", "Open Desired Behavior and choose what you want to improve. Click Save Desired Behavior, then return here and confirm.", "Use Saved Desired Behavior",
            "Choose Keep my current angle, Sustain more angle, or Sustain more extreme angle. The optional angle range is saved for this car. ADT checks time held, speed and recovery; the target is not a promise that every car can achieve it. " +
            "Handling sliders remain separate: for example, calmer transitions or faster self-steer. Save your goals before recording a new baseline. " +
            (car ? "The car setup tuner uses these goals for supported adjustments." : "Your goals help interpret the telemetry. This FFB workflow does not ask you to change the car setup."), "Your goals are saved for this car. Keep them unchanged during a before/after test.");
        if (j.GoalSignature != currentGoal) return Step(GuidedStage.GoalsChanged, "Your driving goals changed", "Start a fresh baseline for the new goals. The earlier runs and reviews remain saved.", "Start a New Baseline",
            "Comparing runs against different goals would move the goalposts. This restarts the current baseline/test progress; it does not delete tune versions, recordings, goals or ratings.", "A new baseline is recorded using the goals now selected.");
        if (!j.TuneReady && j.BaselineId.Length == 0) return Step(GuidedStage.Prepare, "3 · Save and prepare your starting tune",
            ffb ? "Save your current settings first. Generate and review the FFB recommendation, enter/apply the settings you choose, then confirm below." :
                "In AC's pits, save your current car setup with a clear name, then load that setup. Keep your FFB settings unchanged and confirm below.",
            ffb && !j.TuneGenerated ? "Generate & Review FFB" : "Confirm Starting Tune Is Ready",
            "First keep a recovery copy: in AC's pits open Setup and save a named baseline (for example, MyCar_Baseline). " +
            (ffb ? "Write down or screenshot your current in-game and wheelbase FFB settings. " : "Your current FFB settings stay unchanged during this test. ") +
            (ffb ? "Generate & Review FFB creates suggested numbers in ADT. It does not apply them. Use the connection instructions below to enter/apply supported settings, and check that the values took effect. " : "You can start with the car's current setup; no FFB generation or calibration is required. ") +
            (car ? "If you want an ADT starting car setup, use AC Setup: Load Baseline → Generate Car Setup → Save AC Setup File under a NEW name → load that file in the game's Setup menu. Gearing is a separate optional planner for supported cars. " : "Keep the baseline car setup unchanged. ") +
            "A file saved by ADT is not loaded into the game until you choose it there.", TuningFocusOptions.Confirmation(p.Focus));
        if (j.BaselineId.Length == 0) return Step(GuidedStage.Baseline, "4 · Record your first drive", "Load your car and track in AC. Open the recorder, check the setup and conditions, then Connect → Record → drive → Stop → Save Session.", "Record Baseline",
            RecordingHelp(p.Focus, false), "Save Session finishes successfully. Aim for 60–120 seconds; guided progress requires at least 20 seconds of clean, reliably sampled drifting.");
        if (j.Reviewed) return Step(GuidedStage.Complete, "8 · Decide what to do next", "Your review is saved. This does not mean the tune improved. Choose whether to keep the change, revert manually, or test it again.", "Open Saved Comparison",
            "A saved review does not mean the tune improved. Read the measured result and your own rating together. Keep: save the preferred setup/settings and repeat a comparable run. Revert: load your original baseline car setup and/or manually restore the earlier FFB values shown in Tune & Run History. Test again: use Record Comparison Run to repeat the same change. No keep/revert choice applies settings automatically.", "Your rating, notes and next action are saved. Confirm the result with repeated comparable runs.");
        if (j.AfterId.Length > 0) return Step(GuidedStage.Compare, "7 · Compare the two drives", "Open Before / After. Read the result and its limitations. In Tune & Run History, rate how it felt, add notes, choose a next action and save the review.", "Compare With Baseline",
            "Check that the correct earlier baseline is selected. 'Inconclusive' means ADT cannot establish a fair comparison, not that you failed. Different tracks, goals, conditions, unreliable samples or several changes can prevent a useful conclusion. The full diagnosis remains visible. Choose Better, Worse, No noticeable difference or Tradeoff based on your experience; your rating does not overwrite the measured result.", "Click Save Run Review after choosing a rating. Saving a session and saving a review are separate actions.");
        if (j.Recommendation.Length > 0) return Step(GuidedStage.Test, "6 · Make one change and drive again", "Prepare the selected change, then repeat the same section in the same conditions. Record, stop and save another session.", "Record Comparison Run",
            (car ? "For a car change, open AC Setup with Guidance, review the proposed values, save a new .ini and load it in AC. " : "Keep the car setup fixed. ") +
            (ffb ? "For an FFB change, review the recommendation or apply the proposed ADT calibration, generate the updated values, then enter/apply and verify those values. Applying an ADT calibration updates its recommendations; it does not by itself change game or wheelbase settings. " : "Keep the in-game and wheelbase FFB settings fixed. ") +
            "Change only the setting you are testing; leave Desired Behavior unchanged. " + RecordingHelp(p.Focus, true), "The comparison recording is saved with the right baseline, actual setup and change description.");
        return Step(GuidedStage.Findings, "5 · Read your next step", "Open the findings and read Your next step. If ADT offers a test, choose Plan this test. If it asks for more evidence, record another clean drive first.", "Review Baseline Findings",
            "Your next step shows the main observation and one suggested action. Expand Why this next step? for the reason, or enable advanced telemetry to inspect all evidence. " +
            (ffb && car ? "Car and FFB recommendations are available, but test only one change at a time. " : car ? "The suggested test stays focused on the car; FFB remains fixed. " : "The suggested test stays focused on FFB; the car setup remains fixed. ") +
            "You can also select a relevant row in Recommendations and choose Test This Recommendation. Low-confidence or missing advice calls for more evidence. Keep the same driving goals.", "The dashboard names the selected test. Selecting it creates a plan; it does not apply anything.");
    }

    public static string RecordingHelp(TuningFocus focus, bool comparison) =>
        "1. Load the selected car and track in AC and enter a driving session.\n2. In ADT Recorder, check Driver and Conditions (example: dry, same layout, solo transitions). Attach the exact .ini loaded in AC; ADT cannot detect that file automatically. " +
        (focus == TuningFocus.FfbOnly ? "Attaching the unchanged car setup is only a snapshot for a fair comparison; it does not tune the car. " : "") +
        "\n3. Tick the confirmation only if it is true, then click Connect. Live values should update when you drive.\n4. Click Record before your test section (or Start run on a paired touchscreen). Drive for roughly 60–120 seconds with several entries and transitions.\n5. Click Stop, then Save Session in ADT, or Stop run then Save run on the touchscreen. Stop alone does not save.\n6. Click " +
        (comparison ? "Compare With Baseline. Use the same line, conditions and task as the first drive." : "Review Baseline Findings. Your first recording is the reference for later changes.") +
        "\nTelemetry comes directly from AC; SimHub and AZOM are not required to record.";

    public static string Instructions(GuidedPreferences p, IntegrationState? s)
    {
        if (!p.ShowDetailedHelp)
        {
            if (!TuningFocusOptions.IncludesFfb(p.Focus)) return "Car setup: save a baseline → create a new setup in ADT if wanted → load the chosen .ini in AC's pits. Keep FFB fixed. SimHub/AZOM are not needed. Enable more explanation for the exact steps.";
            if (!p.WantLiveConnection || p.SimHub == "No" || p.Azom == "No") return "Manual FFB: enter AC FFB in Controls → Force Feedback and supported wheelbase values in its manufacturer's software. SimHub/AZOM are optional. Enable more explanation for menus and verification steps.";
            if (s is { BridgeConnected: true, AzomDetected: true, SettingsReadable: true }) return "AZOM readback is ready: review supported wheelbase changes, Apply explicitly, then verify readback. Enter AC FFB separately. Enable more explanation for the full instructions.";
            if (s is null) return "Click Check for Me to check the optional live connection. Or turn off live connection help and enter the FFB settings manually.";
            if (!s.SimHubInstalled && !s.BridgeConnected) return "SimHub's folder was not found. Choose its folder below, or turn off live connection help to continue manually. SimHub is optional.";
            if (!s.SimHubRunning && !s.BridgeConnected) return "Start SimHub, then click Check Again. AZOM availability is unknown while the bridge is offline. You can also continue manually.";
            if (!s.BridgeInstalled && !s.BridgeConnected) return "Close SimHub, use Install / Repair Packaged Bridge below, then start SimHub and Check Again. Enable more explanation for the full steps.";
            if (!s.BridgeConnected) return "Enable ADT's bridge in SimHub, restart SimHub if prompted, then Check Again. You can turn off live connection help to continue manually.";
            if (!s.AzomDetected) return "The bridge connected, but AZOM was not detected. Check AZOM is installed and enabled in SimHub, or continue with manual FFB entry.";
            return "AZOM was detected, but its settings are not readable. Check your supported wheelbase is connected and powered on, then Check Again, or continue manually.";
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
