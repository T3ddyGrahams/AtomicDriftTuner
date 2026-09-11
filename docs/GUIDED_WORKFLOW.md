# Guided tuning — 0.9.0-preview.2

This development preview adds a saved **Your Next Step** panel and setup-aware instructions to the telemetry intelligence introduced in preview 1. It is a local testing build, not a newly published beta.

## Tonight's quick test

1. Close any running ADT. Launch `AtomicDriftTuner.exe` from the **0.9.0-preview.2** folder. The installed desktop shortcut still opens the older beta.
2. In **Your Next Step**, choose **Set Up My Workflow**. Enter a driver name, answer whether you have SimHub and AZOM, and select whether you want help connecting live wheelbase control. **Check My Connection / Check Again** inspects what is installed/running/reachable without applying settings. Review the AC folders and choose **Save & Continue**.
3. Review your installed car, rig and drift pack on the dashboard and choose **Confirm This Car & Rig**. Use the same driver name for the whole test; **Use This Driver** switches the remembered workflow to that driver.
4. Open **Edit Desired Behavior / AC Setup**, adjust how you want this car to feel and choose **Save Desired Behavior**. Return to the dashboard and choose **Use Saved Desired Behavior**. Neutral is a valid target.
5. Choose **Generate & Review Tune**. ADT scrolls to the generated recommendations. Use the AC FFB and supported wheelbase settings as instructed. Save an AC setup file when needed and load it from the game's Setup menu. Return with **Back to My Next Step**, tick the readiness confirmation and continue.
6. Choose **Record Baseline**. The recorder carries your driver and any setup file saved through this workflow. Confirm the actual setup attachment, enter conditions/task, connect to AC, confirm tune use and record about 60–120 seconds with several initiations/transitions. Stop, then **Save Session**. Use **Review Baseline Findings**.
7. In Recommendations, select one row and choose **Test This Recommendation**. The dashboard saves a plan and explains the next step. Use **Review / Prepare Selected Change** to return to the assistant; for applicable setup changes, use **Open AC Setup with Guidance**, Generate, inspect the changes, and **Save AC Setup File**. Load the revised file in AC. Keep the saved Desired Behavior fixed while testing temporary guidance. Apply an AC gain calibration recommendation only through its explicit action, then regenerate/review the tune. No change is applied by selecting a recommendation.
8. Choose **Record Comparison Run**. Driver, baseline, conditions and the selected recommendation are prefilled. A setup saved through the workflow is offered as an attachment; verify that it is actually loaded in AC. Name the test, describe the exact change, confirm tune use again and record under comparable conditions. Stop, save and choose **Compare With Baseline**.
9. Inspect Before / After and Tune & Run History. Choose a rating, add notes and **Save Run Review**. The dashboard marks the review saved. A completed workflow does not mean the tune improved; the measured outcome, limitations and your feedback still determine that conclusion.

If a run is interrupted, frozen or has less than 20 seconds of clean drift, it remains available for inspection and does not advance guided progress. If no recommendation needs testing, retain your baseline and collect more representative evidence rather than changing a setting just to complete the loop. Every sidebar tool remains available for experienced users.

## Instructions adapted to your setup

| Situation | Guidance |
| --- | --- |
| Manual workflow, no SimHub or no AZOM | AC setup and telemetry remain available. Follow manual AC FFB and supported wheelbase-software instructions. Optional SimHub path/bridge controls are hidden in the setup interview. |
| Unsure what is installed | Check reports the SimHub folder, process, bridge connection, AZOM detection and setting readback separately. A failed search does not claim the software is absent. |
| SimHub installed but closed | Start SimHub and check again. AZOM availability is unknown while the bridge is offline. |
| SimHub running, bridge unavailable | Locate/install/enable the ADT bridge using Setup & Paths, then check again. Installing the bridge remains an explicit operation with SimHub closed. |
| Bridge connected, AZOM unavailable | Check that AZOM is installed/enabled in SimHub. |
| AZOM detected, settings unreadable | Connect/power the supported wheelbase, check AZOM and retry. |
| Live readback available | Review supported changes in Wheelbase Settings and use explicit Apply/Revert. The existing live controller verifies setting changes; a prior connection check does not bypass that verification. |

Use **Change My Setup** to revise saved answers. Connection status is checked on demand and timestamped; it is not a permanent claim that the hardware remains connected. The preview includes the existing 0.8.3-beta.1 bridge payload for optional installation; the bridge protocol and hardware-write implementation are unchanged.

## What Save and Apply mean

- **Save ADT Profile:** remembers selections and generated recommendations.
- **Save Desired Behavior:** remembers how you want this particular car to feel.
- **Save AC Setup File:** writes a separate `.ini`; load it in AC to use it.
- **Apply Wheelbase Settings:** explicitly sends supported changes through a live integration and verifies readback.
- **Save Session:** stores the recording and captured context.
- **Save Run Review:** stores your rating, notes and the measured comparison.

Generating, saving a profile, checking a connection and planning a test do not apply settings. The dashboard contains this glossary beside the workflow.

## Remembered state and implementation

`GuidedWorkflowStore` keeps preferences and progress under `%LOCALAPPDATA%\AtomicDriftTuner\GuidedWorkflow`. Progress is scoped to car/pack, hardware/rim, session intent and persistent driver ID. Reselect the same car/rig/driver to resume it. Existing telemetry, tune versions and reviews remain in their existing stores.

The workflow tracks explicit car/goal confirmation, generated settings, readiness, saved baseline, planned recommendation, saved after-run and a rated review. A changed generated recommendation invalidates baseline readiness; changed saved Desired Behavior prompts a fresh baseline. **Start a New Baseline** resets navigation progress for the current context while preserving telemetry/history files and the selected setup path.

The recorder refuses to replace an active or unsaved recording with a different plan. Missing planned runs are reported instead of silently selecting another run. A context change while a tool is open prevents an incorrect car's guided handoff. Choosing a recommendation does not change the recorded goals or automatically apply that recommendation. Full saved reviews remain the authority for the outcome; navigation checkmarks are only progress indicators.

## Validation

- 50 regression scenarios pass, including 10 guided-workflow checks and the previous intelligence/history coverage. The existing range sweep covers 8,000 built-in tuning combinations.
- 252 WPF geometry assertions pass at narrow, portrait, short-landscape and ultrawide sizes, including the setup interview, readiness confirmation, Next Step, recommendation-test and comparison actions.
- Layout rendering uses production XAML/styles with business event handlers disabled and isolated application startup. Synthetic checks do not replace hands-on connection, keyboard, monitor/DPI or driving validation.

For diagnosis thresholds and comparison limitations, see [Telemetry intelligence](TELEMETRY_INTELLIGENCE.md).
