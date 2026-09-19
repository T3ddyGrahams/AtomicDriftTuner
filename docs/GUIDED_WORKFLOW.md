# Start-to-finish tuning — local preview.15

Local 0.9.0-preview.15 starts with your own hardware/car choices and asks what you want to tune. It keeps the existing tuning intelligence, sustained-angle goals, run history, touchscreen and gearing support, and adds explicit pit Save & Apply Tune through the updated in-game companion. It is a local test build; the public release remains preview.3. Live-game acceptance is pending.

## Choose what to tune

| Choice | What the walkthrough includes |
| --- | --- |
| **Car tuning only** | Car handling/setup and supported gearing. Keep your current FFB and wheelbase settings fixed. SimHub and AZOM are not required. |
| **Car + FFB** | Car setup plus wheel feedback. ADT asks about optional SimHub/AZOM and explains manual entry or supported live settings. Test one change at a time. |

FFB means the forces you feel through the steering wheel. Assetto Corsa's Controls → Force Feedback page changes wheel feel; its in-pits Setup menu changes the car setup. Hardware selection is required in both workflows to identify the rig used for comparisons; selecting it does not apply FFB settings. ADT remembers progress for the selected workflow, car, rig, driver and session intent.

New users start with short instructions. Turn on **Show more explanation and examples** for fuller instructions; an existing help preference is remembered. Every step says **Ready when** and what comes next. Expand **See the whole start-to-finish process** to see the route for your chosen workflow. Detailed telemetry remains available on demand.

## Start here

1. Launch this preview's executable. An older desktop shortcut may point to an older build.
2. On first launch, choose **Car tuning only** or **Car + FFB** in **Setup & Paths**. Enter a consistent driver name and check the AC install/user folders. If setup is closed, use **Choose What to Tune** on the dashboard; later, use **Change My Setup** to review these choices.
3. For **Car + FFB**, answer Yes, No or Not sure for SimHub and AZOM. **Check for Me / Check Again** only inspects what ADT can find/reach; it installs nothing and applies nothing. Car-only hides these optional connection questions.
4. Choose **Save & Continue**, then use **Choose My Car & Hardware** or open **Car & Hardware**. Select your wheelbase, rim, pack, car and drift target. Scan AC for installed cars. A pack selection does not select its first car automatically.
5. Follow **Your Next Step** on the dashboard. Generation becomes available once the required selections are complete; restoring selections does not generate or apply a tune.

SimHub is an optional companion program. AZOM is an optional SimHub plugin for supported wheelbase settings. ADT reads driving telemetry directly from Assetto Corsa, so neither is required for recording.

Fresh or older settings without saved session selections start blank instead of assuming the developer's rig/car. ADT remembers your selections and edited profile values for this Windows account afterward. Saved installed cars wait for a scan matching their exact car ID and folder; unavailable profiles remain unselected instead of being replaced with the first item. If active-car detection is enabled, verify the detected car before confirming the session.

## The eight steps

1. **Choose your car and driver.** Scan the AC installation, select the installed car/pack, check the wheelbase and rim, and use a consistent driver name. Choose **Confirm This Car & Rig** when ADT and AC match.
2. **Describe your driving goals.** Choose **Open Desired Behavior**. Leave **Keep my current angle** selected, or choose **Sustain more angle** / **Sustain more extreme angle**. The optional custom range is body angle relative to travel, not steering-wheel rotation. Handling presets and the stability preference remain separate. Save your goals and return to the walkthrough, then choose **Use Saved Desired Behavior**. A changed goal needs a fresh baseline. This goals-only window does not generate/apply car or FFB settings.
3. **Save and prepare your starting tune.** In AC's pits, save a named baseline setup and load it. **Car tuning only:** keep FFB unchanged; no FFB generation or calibration is required. **Car + FFB:** also write down or screenshot current FFB/wheelbase settings, choose **Generate & Review FFB**, then enter/apply and verify the supported values using the instructions below. An optional ADT starting car tune follows Load Baseline → Generate Car Setup → review. Then either **Save AC Setup File** under a new name and load it in AC, or **Stage for in-game pits** and explicitly use the companion's **Pit setup → Save & Apply Tune**. The pit action requires a stationary car in the editable setup menu, matching baseline and no recording; see [the first test and recovery guide](PIT_SETUP.md). Tick the readiness confirmation only when it is true.
4. **Record your first drive.** In Recorder, enter the conditions/task, check the driver and click Connect. With the updated in-game companion paired, leave **Capture the current car setup through the in-game companion** checked and wait for a fresh captured-setup status. Otherwise, uncheck it and use **Attach AC Setup Snapshot** to select the exact saved setup loaded in AC. Confirm the selected settings and FFB are in use, then click Record. Drive roughly 60–120 seconds with several entries and transitions. Click Stop, then Save Session. Stop alone does not save.
5. **Read the next step.** Choose **Review Baseline Findings**. **Your next step** shows the goal saved with the run, what ADT noticed, confidence and one action. Expand **Why this next step?** for the explanation. When a supported recommendation is available, **Plan this test** returns to the dashboard with that test selected. Otherwise follow the request for another run or a review. Enable **Show advanced telemetry and recommendations** for all assessments, recommendations and phase/pedal evidence; the original **Test This Recommendation** action remains available there. Planning does not apply settings.
6. **Make that change and repeat the drive.** For a car change, change the chosen setting in AC and save a named setup, or explicitly apply a reviewed staged plan through **Pit setup**. In **Car + FFB**, an FFB calibration test means saving the explicit calibration, regenerating/reviewing the values, then entering/applying them in the game/wheelbase software as needed. Car-only keeps FFB fixed. Keep the same workflow, Desired Behavior, car, driver, rig, track, conditions and task. Choose **Record Comparison Run**. Wait for a fresh captured setup, or load the new saved setup in AC and update the manual attachment. Describe the change, confirm use again, record, stop and save. Use the same setup-capture method for both runs. An unresolved pit action blocks recording until acknowledged, or until the AC driving session is fully closed and desktop **Clear pending pit action** is used. After restarting, inspect/load the intended setup and generate/stage again.
7. **Compare and rate it.** In Before / After, verify the baseline and read the result and limitations. In Tune & Run History, choose Better, Worse, No noticeable difference or Tradeoff, add notes, choose a next action and click **Save Run Review**. Driver feedback does not overwrite the measured outcome.
8. **Keep, restore, or test again.** Keep and verify: save the preferred settings and repeat a comparable run. To restore a pit-applied setup in the same game session, explicitly use **Pit setup → Restore previous** in the editable setup menu. Otherwise load the original baseline AC setup manually; restore earlier FFB values separately from Tune & Run History if needed. Test again: choose **Record Comparison Run Again** on the dashboard. Saving a Keep/Revert review decision does not automatically apply or undo settings.

A short, interrupted or unreliable recording stays available for inspection. Guided progress requires at least 20 seconds of clean, reliably sampled drifting. A completed review does not mean an improvement was established. If no supported recommendation is available, retain the baseline and collect more representative evidence.

Automatic setup capture reads the current car settings without changing them. It checks about once per second; it is not continuous verification. If capture is lost or the setup changes during recording, ADT keeps the samples but cannot use that run to claim a tuning improvement. Repeat the run with stable capture. A manual attachment represents the saved file and cannot include later unsaved edits. FFB still needs your confirmation. The packaged CompanionPayload/README.md explains companion setup and capture limits; real-game acceptance of this local preview remains pending.

## Choose your wheelbase software

For **Car + FFB**, Setup & Paths offers **SimHub / AZOM**, **MOZA Pit House**, and **Other wheelbase software / manual**. Save & Continue remembers the choice. The connection questions and Wheelbase Settings view follow it; existing users retain SimHub/AZOM. Pit House can be used manually without SimHub. Its optional SDK connection is experimental and requires SDK-compatible Pit House plus the native SDK files on the tester PC. Follow [the Pit House guide](PITHOUSE.md) for read/apply/restore and hardware acceptance steps.

Changing providers starts a new guided baseline without deleting earlier history. Pit House and AZOM do not support identical controls/ranges, so ADT does not issue an improvement verdict across different FFB providers. Car-only comparisons remain independent of this optional choice.

## SimHub/AZOM connection help

The connection walkthrough is for **Car + FFB**. **Car tuning only** goes straight to preparing the car setup and recording. It does not require installing SimHub, AZOM or the bridge. You can change the workflow later in **Setup & Paths** without deleting earlier runs.

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

- ADT automatically remembers your current hardware/car/target selections and edited profile values locally. This is separate from saving a generated profile or applying settings.
- **Save ADT Profile** remembers selections and generated targets. It does not apply them.
- **Save Desired Behavior** remembers the car's driving goals.
- **Save AC Setup File** creates a separate .ini. Load it in the game's Setup menu to use it.
- **Stage for in-game pits** freezes the reviewed numeric plan. **Pit setup → Save & Apply Tune** separately saves previous/changed setups, applies supported values and checks readback. **Restore previous** is another explicit action in the same game session.
- **Update recommendations only** (formerly **Apply Feedback + Regenerate**) recalculates ADT's recommendations; it does not apply car or FFB values.
- **Apply ADT calibration** changes ADT's future recommendations, not the actual game/wheelbase settings.
- **Apply Wheelbase Settings** explicitly sends supported changes through the existing integration and verifies them.
- **Save Session** saves the recording and context. **Save Run Review** saves the rating, measured comparison, notes and next action.

All raw telemetry, phase diagnosis, goal interpretation, confidence thresholds and numerical tuning calculations are preserved. Assessments, full recommendations, before/after metrics, tune versions and reviews remain available.

Progress/preferences remain under `%LOCALAPPDATA%\AtomicDriftTuner\GuidedWorkflow`. Car-only and combined journeys are kept separately; combined progress keeps its original path. Changing the choice does not relabel or delete recordings, tune versions or reviews. Use a baseline recorded in the same workflow for a new recommendation test. Older users are asked to confirm a current choice once; legacy FFB-only recordings remain readable, but FFB-only is not offered for new workflows.

The separate executable still uses the normal ADT settings/history folders. Use one ADT version at a time. No installer, car physics, live hardware values or public release is changed merely by opening this build.

## Verification and remaining hands-on checks

For this build, check that a fresh Windows user starts without a rig/car, that chosen profiles and edited values return after reopening, and that choosing a different pack requires an explicit car choice. Test both workflow choices: car-only should skip FFB preparation and connection questions; combined should adapt to manual or available live control. See [test commands and automated coverage](../tests/README.md).

Confirm the manual/live instructions match your rig, record a baseline and comparison, and save a review. For the touchscreen, follow [Touchscreen setup](TOUCHSCREEN.md), pair, then try Start run → Stop run → Save run. Real driving, physical monitor/DPI moves, screen-reader behavior, SimHub window placement and live AZOM setting application still require hands-on testing. Existing analysis limitations are explained in TELEMETRY_INTELLIGENCE.md; final-drive support is described in GEARING.md.
