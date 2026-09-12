# ADT touchscreen / SimHub Control Center — local preview.8

ADT's touchscreen page is available at `/dash`. Use it through SimHub Dash Studio on the PC, or open it directly in a phone/tablet browser on the same private network. It uses the same desktop recording, telemetry and tuning services. Keep ADT running.

## Set it up in SimHub

1. Close older ADT versions and open preview.8. The separate executable uses your existing ADT settings and history.
2. In **Setup & Paths**, make sure the SimHub folder is correct. AZOM is optional for recording.
3. Open **Remote** and press **START REMOTE**. The default port is **5190**. Keep this port unless another program is using it.
4. Press **INSTALL SIMHUB DASHBOARD**. ADT creates a dashboard named **ADT Control Center**. Your existing **ADT** dashboard is preserved. Updating a previously generated Control Center saves a backup first.
5. In SimHub, open **Dash Studio → ADT Control Center**, then launch it as a window on your touchscreen. Reopen Dash Studio if it has not refreshed its list. Use SimHub's window/full-screen controls to place it on the screen you want.
6. Tap the six digits shown in ADT's Remote panel, then **PAIR**. The page has its own keypad; a physical keyboard also works.

The generated dashboard uses `http://127.0.0.1:5190/dash` at the default port. This refers to the same PC, so it does not depend on the PC keeping its current Wi-Fi address. If you change the Remote port, press **INSTALL SIMHUB DASHBOARD** again and reopen the dashboard.

The page is enabled in driving, idle and pit screens. Start/stop/save actions do not require **Allow remote AZOM writes**.

## Use a phone/tablet instead

1. Connect the device to the same private network as the PC.
2. Start ADT Remote on the PC.
3. Copy the **Active LAN address** from ADT, add `dash` after its trailing slash, and open it on the device. For example, `http://192.168.1.50:5190/dash`. Use the address ADT actually shows; **127.0.0.1** is only for a screen running on the PC itself.
4. Enter the pairing code using the on-page keypad and tap **PAIR**.

If Windows Firewall asks, allow ADT on the private network used by your PC and device. The listener is intended for the same trusted local network; it does not require Internet hosting or an account.

## Record a run

1. In desktop ADT, select your car, rig and driver. Follow **Your Next Step** to prepare the starting tune.
2. Open **Telemetry Recorder**. Enter your driver and conditions/driving task, attach the actual AC setup if available, and confirm the settings in use. Start an AC driving session and confirm the live values update.
3. On the touchscreen, check the car, driver and recording message. Tap **Start run** when it becomes available.
4. Drive your test section. The screen shows the recording state, duration and sample count.
5. Tap **Stop run**, then **Save run**. Stopping alone does not save. Wait for the save confirmation before starting another run.
6. Follow **Your Next Step** and return to desktop Recommendations to inspect the full diagnosis, choose the next change, compare runs and save your review.

The recording actions invoke the existing desktop recorder. Its full JSON/CSV recording, analysis and history are preserved. They do not apply settings. Old commands cannot act on a replacement recorder or edited recording plan. Duplicate or outdated commands are rejected. After an interrupted reply, check the refreshed recording state before trying again. A running recording remains in desktop ADT if the screen loses connection.

### If recording pauses or stops after a tune change

Return to the driving session after applying the recommendation, confirm **AC TELEMETRY: LIVE**, then start your next run. Preview.8 waits up to five seconds when telemetry briefly stops updating. The recorder and companion show **Waiting for fresh AC telemetry**; sampling resumes in the same run if fresh frames return within that window. Missing time stays a gap and is excluded by the existing analysis.

If the outage lasts longer, AC changes car/track, or the physics stream restarts, ADT ends the run and displays the reason. Save the partial recording, return to live telemetry, and start a new run. Stopping manually while telemetry is unavailable also marks the run interrupted; it cannot qualify as proof that a tune improved or supply an automatic calibration correction. The reason is retained in the saved session JSON.

This fix is in desktop ADT and works with the existing in-game companion and SimHub dashboard. Re-pair them after starting the new ADT build; reinstalling those clients is unnecessary.

## Tune and adjust wheel feel

- **TUNE** shows the current tune and can request a new recommendation using desktop ADT's existing engine.
- **BEHAVIOR** edits the car's Desired Behavior profile. Saving goals does not change the car's physical settings. Use a fresh baseline if you change the goals for a comparison.
- **AZOM** shows supported live values when the integration is available. Remote writes remain off until enabled in desktop ADT. Use the large minus/plus controls or type a value, then **APPLY** and confirm on the screen. Background refreshes preserve unfinished edits. Switching the car/rig clears unfinished wheelbase edits so they are not reused for another context.
- **REVERT LAST REMOTE CHANGE** uses the existing guarded remote revert operation. It does not undo every tune or AC setup change. Desktop ADT verifies supported live writes through the same bridge as before.

The detailed recommendation review, AC setup-file selection/generation, run ratings and full before/after history remain in desktop ADT. This version completes the existing browser touchscreen connection and recording controls; the roadmap's broader accept/reject/switch-whole-tune controls are separate future work.

## If it does not connect

- **Blank page / page not found:** open preview.7 and start its Remote server. Older ADT builds did not serve the `/dash` address used by the installed SimHub dashboard.
- **Connection refused:** make sure ADT is running and Remote says RUNNING. Check that the page uses the same port. On the PC, try the address in **Touchscreen address on this PC**.
- **Works on the PC but not the phone:** use ADT's LAN address on the phone, check that both devices share the same network, and check Windows Firewall/private-network access. A guest network may prevent devices from reaching each other.
- **Pairing required again:** starting Remote or generating a new code changes the credentials. Pair again. If the browser cannot save local storage, the current session still works, but reopening may require pairing again.
- **Start run is disabled:** read the message above the first-time help. Prepare the recorder on the desktop, finish saving an earlier recording, confirm the same car/rig/driver, and connect AC telemetry.
- **OFFLINE controls:** wait for current status. Do not assume an action failed just because its reply was interrupted. Check the refreshed recorder or desktop ADT before trying again.

Automated tests cover the real HTTP routes and recording guards, installation/backups, a private-network request, browser pairing/recording/reconnection/editing behavior, and layout at 320×568 through 1280×720. Physical taps on your actual touchscreen, SimHub's native window placement and live driving/wheelbase application still need your rig test.
