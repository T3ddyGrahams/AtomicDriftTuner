# ADT adaptive touchscreen / SimHub Control Center — local preview.9

Preview.22 adds a recording evidence banner and visible missing-goal guidance. Ready to Review means enough reliable evidence for review; stop and save when convenient. Enable **Play ready chime on this device** before driving if wanted. Browser audio needs a touch or keyboard gesture after loading; blocked sound never prevents visual guidance or recording. Sound plays once per run on that screen, including after reconnects. The desktop recorder has a separate PC sound option; disable it if you want only touchscreen sound. Device preferences are saved locally.

ADT's touchscreen page is available at `/dash`. It fills the available browser width and rearranges its cards for different screen sizes and orientations. Short screens scroll to keep every action reachable. Resizing or rotating the device keeps the same pairing, current view, edited values and desktop recording. It uses the same recording, telemetry and tuning services. Keep ADT running.

## Raspberry Pi, tablet or another computer

1. On the gaming PC, save your work, close the older ADT and open preview.9. Existing settings and history remain available.
2. Open **Remote** and press **START REMOTE**. The default port is **5190**.
3. Connect the device to the same home network as the PC.
4. Press **COPY PI / TABLET ADDRESS**. Open that address in the device's browser. The address is generated for the PC running ADT; do not reuse another tester's address.
5. Tap **Full screen**, enter the six digits shown in ADT's Remote panel, and tap **PAIR**. The page has its own keypad.
6. Prepare the recorder in desktop ADT, then follow the recording steps below.

No screen width, height or resolution needs to be entered. A browser may require a fresh tap on **Full screen** after reopening the page. The button becomes **Exit full screen** while fullscreen is active. If the browser declines the request, ADT explains how to use its fullscreen menu; Chromium on a Pi also supports F11. Automatic fullscreen at Pi startup is configured in the Pi browser's kiosk settings, not in the SimHub editor.

## Open it through SimHub

1. In ADT **Setup & Paths**, check the SimHub folder.
2. Start ADT Remote, then press **INSTALL SIMHUB DASHBOARD**. ADT creates **ADT Control Center** and backs up changes to that generated dashboard. Your original **ADT** dashboard is preserved.
3. Reopen **ADT Control Center** through SimHub on the device.
4. On a Pi/tablet browser, tap **Open ADT**, then **Full screen**, and pair.

The browser entry opens ADT as a full page, outside SimHub's fixed dashboard canvas. This is what lets the controls adapt to the actual device size. Use the browser's Back action to return to SimHub's dashboard entry. A native PC WebView opens ADT directly. The native SimHub window itself still follows SimHub's window sizing rules; use the direct browser address for the adaptive device view.

The generated entry uses the PC's preferred private network address and current Remote port. Routed physical interfaces are suggested before host-only virtual adapters. If the PC address or port changes, install the dashboard again and reload it on the device. If several real networks are connected, choose an address reachable from the device. No address can make isolated guest networks communicate.

For a screen connected directly to the gaming PC, **COPY PC ADDRESS** gives `http://127.0.0.1:5190/dash` at the default port. On a Raspberry Pi, 127.0.0.1 means the Pi itself, so use the other-device address.

If Windows Firewall asks, allow ADT on the private network shared by the PC and device. Start/stop/save actions do not require **Allow remote AZOM writes**.

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

- **Blank page / page not found:** open preview.9 and start its Remote server. The new SimHub entry requires `/dash/launch`, which older builds did not serve.
- **Browser reports that ADT refused to connect inside SimHub:** install the current ADT Control Center entry. It embeds only the public Open ADT page. The paired controls deliberately open as a full page.
- **Borders or tiny controls inside SimHub:** tap Open ADT. Changing every tester's canvas resolution is unnecessary.
- **Fullscreen did not start:** tap Full screen on the ADT page. Browsers require a user gesture and may restrict fullscreen. Follow the inline browser instructions if it is declined.
- **Connection refused:** make sure ADT is running and Remote says RUNNING. Check that the page uses the same port. On the PC, try the address in **Touchscreen address on this PC**.
- **Works on the PC but not the phone:** use ADT's LAN address on the phone, check that both devices share the same network, and check Windows Firewall/private-network access. A guest network may prevent devices from reaching each other.
- **Pairing required again:** starting Remote or generating a new code changes the credentials. Pair again. If the browser cannot save local storage, the current session still works, but reopening may require pairing again.
- **Start run is disabled:** read the message above the first-time help. Prepare the recorder on the desktop, finish saving an earlier recording, confirm the same car/rig/driver, and connect AC telemetry.
- **OFFLINE controls:** wait for current status. Do not assume an action failed just because its reply was interrupted. Check the refreshed recorder or desktop ADT before trying again.

Automated tests cover the real HTTP routes and framing policy, recording guards, installation/backups, LAN address selection, cross-origin SimHub browser handoff, fullscreen and a denied fullscreen request, resizing during recording, pairing/reconnection/editing, short-screen confirmations, and layouts from 320×568 and 480×320 through 3440×1440. Physical taps and browser behavior on your actual Pi still need your rig test.
