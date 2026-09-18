# ADT Companion — workflow preview

Record runs, read findings, plan one test and save comparison feedback inside Assetto Corsa.

Companion **0.3.0-preview.1** adds read-only current-setup capture to the four scrollable tabs: **Record**, **Findings**, **Compare** and **Help**. After installing or updating, exit the current driving session and launch a new session so CSP reloads the app.

Current-setup capture requires desktop **ADT 0.9.0-preview.14 or newer** running on the same PC, and a CSP version exposing the current-setup serializer. Desktop preview.13 supports the in-game workflow with manual setup attachment; preview.4–preview.12 retain recording controls. Public preview.3 lacks the companion endpoints. SimHub/AZOM is not required for AC recording. Desktop ADT remains responsible for analysis and saved history.

## Install with Content Manager

The desktop installer and portable ZIP include this app. The simplest installation is **Remote → Install / Update Companion**, after selecting the AC install folder in Setup & Paths and exiting the driving session. ADT copies only its five shipped app files and backs up changed versions. **Open Companion Package** locates the ZIP for installation through CM instead.

1. Keep the companion ZIP intact and drag it into Content Manager. Review the installation entry for **ADT Companion** and install it. This package contains `apps/lua/ADTCompanion`.
2. If your CM version does not recognize the archive, extract its `apps` folder into your Assetto Corsa installation folder (the folder containing `acs.exe`). This adds `apps/lua/ADTCompanion`; it does not replace car files.
3. Ensure CSP is active. Enter an AC session and open **ADT Companion** from the in-game apps sidebar. Exact Lua app menus vary by CM/CSP version.

## Prepare once on the PC

1. Start desktop ADT preview.14, choose **Car tuning only** or **Car + FFB**, and select your own car, rig and driver. Follow **Your Next Step** to save your goals and prepare the baseline.
2. Open **Telemetry Recorder**. Enter the driver and conditions/driving task. Leave **Capture the current car setup through the in-game companion** enabled for supported CSP capture. If capture is unavailable, turn it off and attach the exact saved setup loaded in AC. These details are captured when recording starts; update them when the setup or test changes.
3. Open **ADT Remote**, press **START REMOTE**, and leave ADT running. You can minimize ADT. You do not need to enable remote AZOM writes.

## Record from inside the game

1. In **ADT Companion**, enter the Remote port (normally `5190`) and its six-digit pairing code. Click **Pair with ADT**. The token stays in memory and must be paired again after ADT Remote restarts or its code is regenerated.
2. In **Record**, check the car, driver, workflow, setup and conditions. With automatic capture enabled, wait for fresh captured-setup status while in a live, unpaused driving session. **Prepare next recording**, when available, uses the current desktop guided plan. Read the confirmation, check **I checked this is true** only after verifying the actual settings, then choose **Confirm these settings**. Preparation/confirmation does not apply settings or start recording. Wheelbase/FFB settings still need your confirmation.
3. Confirm **AC TELEMETRY: LIVE**, then choose **Start recording**. Drive roughly 60–120 seconds with several entries and transitions. Choose **Stop recording → Save session**. Stop alone does not save. ADT uses the same analysis and JSON/CSV storage as the desktop recorder.
4. In **Findings**, choose **Read saved run**. The panel shows the saved goal, what ADT noticed, confidence, the next action and optional reasons. Opening a tab does not repeatedly analyze recordings.
5. If ADT offers a supported change, choose **Plan this test** for one recommendation. Planning records the test and prepares its recording details. It does not apply car, FFB or wheelbase settings. When evidence is weak, follow the request for another clean run instead.
6. Make the chosen change in AC. With automatic capture, return to the live session and wait for fresh evidence of the current setup. With manual attachment, save/load the changed setup and update its attachment in desktop ADT. Keep the same car, rig, driver, workflow, Desired Behavior, track and conditions. Return to **Record**, check the plan and explicitly confirm the settings again. Record, stop and save the comparison run.
7. In **Compare**, choose **Read saved run / comparison**. Read the measured result and every limitation. Select how it felt, choose a next action, add optional notes (up to 2,000 characters), then **Save run review**. Your rating stays separate from the measured result. Keep/Revert saves your decision; it does not apply or undo settings.

**Help** explains the sequence and the current completion conditions. Every pane scrolls, including at the minimum window size. First-time setup, file selection and editing driving conditions remain in desktop ADT. The full telemetry tables, older runs and advanced tuning tools also remain there.

## What current-setup capture establishes

Supported CSP exposes the current car setup, including unsaved pit edits, according to its installed SDK. This has not yet completed a real-game acceptance test across cars/CSP versions. Capture reads numeric setup values only; it never changes a setup, saves an AC file or applies FFB. ADT stores a numeric fingerprint and capture identity without the raw INI, setup name, author or local file paths.

Checks are periodic at roughly **1 Hz**, with a **five-second freshness limit**. They are not continuous verification: an edit changed and undone between observations can escape detection. A detected setup/session change or lost capture marks that run's setup evidence unconfirmed. If setup capture alone is lost, telemetry sampling continues and the run can be saved for inspection, but it cannot support a setup improvement claim. Reconnecting does not retroactively verify the missing interval; start a new clean run.

If your CSP/car cannot provide capture, use manual attachment. The attachment represents the saved file you selected; it cannot include later unsaved pit edits. Save the intended setup, load that exact file in AC, attach it and confirm its use. In either mode, confirm that FFB is held fixed for car-only tests, or that the intended FFB targets are in use for combined tests. Hardware settings are not read automatically.

## What the messages mean

- **Connected to desktop ADT**: the companion can communicate with ADT; it does not mean AC telemetry is live.
- **AC TELEMETRY: WAITING / STALE**: telemetry is missing or no longer fresh. Enter a live AC session. Interrupted recordings are retained for review and can be saved when analysis succeeds.
- **Waiting for fresh AC telemetry** (desktop preview.8 or newer): the recorder allows up to five seconds for a brief telemetry interruption to recover, then resumes the same recording. A longer outage stops the run and shows the cause. Save the partial recording and start again once telemetry is live. Missing time is excluded from driving analysis. No companion reinstall is needed for this desktop fix.
- **Prepare in ADT**: initial setup, the selected car/rig/driver, conditions or setup attachment needs attention. Complete the described desktop step, then refresh the plan in **Record**.
- **Unsaved**: save the stopped run before starting another. The companion never discards a recording.
- **Outcome unknown / reconnecting**: an action response was lost. All action buttons stay unavailable until fresh status returns. Check whether the run, plan or review already changed before trying again; the panel never automatically repeats a command.
- **Inconclusive**: the runs cannot establish a fair comparison. Read the reason; the observations remain available. A positive driver rating does not override this result.
- **Automatic setup unavailable / waiting for fresh setup evidence**: return to the live unpaused session with the updated companion paired. If capture stays unavailable, turn off automatic capture in the desktop recorder and attach the saved setup manually. A run that already lost setup evidence still needs a clean repeat for comparison.

Disconnecting or hiding this panel does not stop a recording in ADT. Keep desktop ADT running and use its recorder if the in-game connection is unavailable. The companion and touchscreen share the same recorder and command guard; changing the plan in one invalidates old commands from the other.

## Validation status

The transport/UI tests exercise the actual Lua entry point with mocked CSP drawing and networking. Capture checks cover bounded numeric parsing, canonical fingerprints, identity, stale responses and read-only behavior. Existing tests cover findings, planning, feedback, timeouts, save failures and duplicate saves. The installed CSP SDK documents the UI and setup serialization APIs used here. These checks do not replace actual CSP rendering, unsaved pit-edit capture or a live baseline/change/comparison test. No claim of verified compatibility across CSP versions is made.

Report the desktop ADT version, companion version, CM/CSP versions, the operation attempted and the panel's error text. Do not share pairing codes/tokens.
