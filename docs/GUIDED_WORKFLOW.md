# Start-to-finish tuning — local preview.7

Local 0.9.0-preview.7 restores the original combined tuning workflow while keeping the clearer guidance, tuning intelligence and gearing support. It adds a working touchscreen page and SimHub Control Center setup. It is a local test build; the public release remains preview.3.

## One combined workflow

Wheel feel and car setup stay together in **Your Next Step**. There is no separate FFB-only/car-only selector. Review the full recommendations and test one change at a time, keeping the same car, driver, track, conditions and Desired Behavior during a comparison.

FFB means the forces you feel through the steering wheel. Assetto Corsa's Controls → Force Feedback page changes wheel feel; its in-pits Setup menu changes the car setup. ADT remembers progress for the selected car, rig, driver and session intent.

Leave **Show more explanation and examples** on for the fuller instructions. Turn it off for short instructions. Every step also says **Ready when**. Expand **See the whole start-to-finish process** to see what comes next.

## Start here

1. Launch this preview's executable. An older desktop shortcut may point to an older build.
2. In **Your Next Step**, choose **Set Up My Workflow** or **Change My Setup** to review the optional software, driver name and AC folders.
3. If asked about SimHub or AZOM, choose Yes, No or Not sure. **Check for Me / Check Again** only inspects what ADT can find/reach; it installs nothing and applies nothing.
4. Choose **Save & Continue**, then follow the current step on the dashboard.

SimHub is an optional companion program. AZOM is an optional SimHub plugin for supported wheelbase settings. ADT reads driving telemetry directly from Assetto Corsa, so neither is required for recording.

## The eight steps

1. **Choose your car and driver.** Scan the AC installation, select the installed car/pack, check the wheelbase and rim, and use a consistent driver name. Choose **Confirm This Car & Rig** when ADT and AC match.
2. **Describe your driving goals.** Choose **Open Desired Behavior**. Neutral is a useful starting point if you are unsure. Save your goals and return to the walkthrough, then choose **Use Saved Desired Behavior**. This goals-only window does not generate/apply car or FFB settings.
3. **Save and prepare your starting tune.** In AC's pits, save a named baseline setup. Write down or screenshot current FFB/wheelbase settings before changing them. Choose **Generate & Review FFB**; enter/apply and verify the supported values using the instructions below. For an optional ADT starting car tune: Load Baseline → Generate Car Setup → Save AC Setup File under a new name → load it in AC. Tick the readiness confirmation only when it is true.
4. **Record your first drive.** In Recorder, attach the exact setup loaded in AC, enter the conditions/task, and check the driver. Confirm the selected settings are in use, click Connect, then Record. Drive roughly 60–120 seconds with several entries and transitions. Click Stop, then Save Session. Stop alone does not save.
5. **Read the findings and select one test.** Choose **Review Baseline Findings**. Assessments and phase evidence show what ADT observed. Review both wheel-feel and car-setup recommendations. Read the reason and confidence, select one row, and choose **Test This Recommendation**. This saves a plan, not a setting change.
6. **Make that change and repeat the drive.** For a car change, save a new setup and load it in AC. For an FFB calibration, save the explicit calibration, regenerate/review the values, then enter/apply them in the game/wheelbase software as needed. Keep the same Desired Behavior, car, track, conditions and task. Choose **Record Comparison Run**, verify the actual setup attachment, describe the change, confirm use again, record, stop and save.
7. **Compare and rate it.** In Before / After, verify the baseline and read the result and limitations. In Tune & Run History, choose Better, Worse, No noticeable difference or Tradeoff, add notes, choose a next action and click **Save Run Review**. Driver feedback does not overwrite the measured outcome.
8. **Keep, revert manually, or test again.** Keep and verify: save the preferred settings and repeat a comparable run. Revert manually: load the original baseline AC setup and/or restore the earlier FFB values from Tune & Run History. Test again: choose **Record Comparison Run Again** on the dashboard. These decisions are saved; they do not automatically apply or undo settings.

A short, interrupted or unreliable recording stays available for inspection. Guided progress requires at least 20 seconds of clean, reliably sampled drifting. A completed review does not mean an improvement was established. If no supported recommendation is available, retain the baseline and collect more representative evidence.

## With or without SimHub/AZOM

| Situation | What to do |
| --- | --- |
| Neither installed | Enter recommended AC FFB in Controls → Force Feedback. Use the wheelbase manufacturer's software for matching supported settings. |
| SimHub only | Use manual FFB entry. SimHub alone does not provide AZOM wheelbase control. |
| Unsure | Check for Me separates folder detection, running process, bridge connection, AZOM detection and readable settings. Not found does not prove uninstalled. |
| SimHub installed but closed | Start SimHub and check again. AZOM remains unverified while the bridge is offline. |
| Bridge unavailable | If you want live control, use Setup & Paths to locate/install/enable the packaged ADT bridge with SimHub closed, then restart/check. Manual entry remains available. |
| Bridge connected, AZOM unavailable | Check that AZOM is installed/enabled, or continue manually. |
| AZOM detected, settings unreadable | Connect/power the supported wheelbase, check AZOM, then retry or use manual entry. |
| Readback available | Open Wheelbase Settings, review supported changes, explicitly Apply and verify readback. Enter AC's in-game FFB separately; load AC setup files separately. |

For Content Manager, AC FFB is under Settings → Assetto Corsa → Controls → Force Feedback. In the standard AC launcher use Options → Controls → Force Feedback. AZOM/MOZA-specific values do not map directly to every wheelbase; only use controls your hardware supports.

## Saving, intelligence and comparisons

- **Save ADT Profile** remembers selections and generated targets. It does not apply them.
- **Save Desired Behavior** remembers the car's driving goals.
- **Save AC Setup File** creates a separate .ini. Load it in the game's Setup menu to use it.
- **Apply ADT calibration** changes ADT's future recommendations, not the actual game/wheelbase settings.
- **Apply Wheelbase Settings** explicitly sends supported changes through the existing integration and verifies them.
- **Save Session** saves the recording and context. **Save Run Review** saves the rating, measured comparison, notes and next action.

All raw telemetry, phase diagnosis, goal interpretation, confidence thresholds and numerical tuning calculations are preserved. Assessments, full recommendations, before/after metrics, tune versions and reviews remain available.

Progress/preferences remain under %LOCALAPPDATA%\AtomicDriftTuner\GuidedWorkflow using the original combined-workflow path. A limited-scope choice saved by preview.6 now opens the combined workflow. The earlier limited-scope recordings and progress files are preserved. Record a fresh combined baseline before using those older runs to plan a new guided comparison; their measured results remain available in history.

The separate executable still uses the normal ADT settings/history folders. Use one ADT version at a time. No installer, car physics, live hardware values or public release is changed merely by opening this build.

## Verification and remaining hands-on checks

Regression checks cover unchanged intelligence and legacy history, restored combined progress, real touchscreen HTTP routes, recording guards and dashboard installation/backups. WPF checks cover the guidance and scrolling; browser checks exercise touch pairing, start/stop/save, reconnects, editable values and responsive tabs.

Confirm the manual/live instructions match your rig, record a baseline and comparison, and save a review. For the touchscreen, follow [Touchscreen setup](TOUCHSCREEN.md), pair, then try Start run → Stop run → Save run. Real driving, physical monitor/DPI moves, screen-reader behavior, SimHub window placement and live AZOM setting application still require hands-on testing. Existing analysis limitations are explained in TELEMETRY_INTELLIGENCE.md; final-drive support is described in GEARING.md.
