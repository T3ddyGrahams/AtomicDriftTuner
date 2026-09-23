# Atomic Drift Tuner (ADT) — Beta Tester Guide

**Local preview.24 — matching car-data copies:** If a car has both `data.acd` and an unpacked `data` folder, ADT now verifies that all supported physics files match exactly. Matching copies can be used for gearing and physics guidance; differing copies remain blocked with an explanation. No car files are changed. Reopen Gearing, load your saved baseline and calculate again. Use a fresh baseline run if ADT previously could not read this car's physics. Includes the preview.23 recording fix; companion **0.4.0-preview.3** is unchanged.

**Local preview.23 — recording continuity:** ADT now buffers telemetry in the background so busy desktop/status updates do not discard samples. Stop and save include the pending frames. Run a fresh 60–120 second test with the companion or touchscreen connected and check Recording evidence details, then save and record a second run. Genuine outages still count as gaps; the readiness requirements and tuning calculations are unchanged. Companion **0.4.0-preview.3** is unchanged, so this correction only needs the desktop update.

**Local preview.22 — recording guidance:** Desktop, companion and touchscreen now show a prominent evidence banner, missing-goal instructions and **Ready to review**. Stop and save when convenient; recording continues. Optionally enable **Play a ready-to-review chime on this PC** in Telemetry Recorder, or **Play ready chime on this device** on the touchscreen before driving. Disable PC sound if you want only the touchscreen chime. Each sounds once per recording. Ready means useful evidence for review, not proof of improvement.

Supported unnamed numeric setup values are now captured and monitored without guessing their meaning. Their notice limits before/after attribution and pit staging; ordinary run review remains available. Both packages include companion **0.4.0-preview.3**. Update the companion with the game closed, then start a fresh session. Tuning calculations and outstanding audit fixes are unchanged.

**Local preview.21 — packed car data and saved-setting meanings:** In **AC Setup**, leave **Use car data for setup guidance (packed or unpacked)** enabled, load your baseline, and expand **What my saved settings mean**. ADT reads supported ordinary archives in memory and shows verified gearing/ECU mappings, partial interpretations and unsupported controls. The gearing planner also supports these archives. Installed car files stay untouched; no scripts execute. Record a new baseline for the expanded fingerprint coverage. See `docs/CAR_PHYSICS.md` and `docs/GEARING.md` for compatibility limits. Includes earlier local fixes; public GitHub remains preview.17.

**Local preview.20 — readable car physics:** **AC Setup** now imports supported accessible base-car values, shows their sources beside recommendations, and holds unsupported controls/drivetrain advice. Saved setup values remain separate. New run snapshots flag differing base-file fingerprints in comparisons. See `docs/CAR_PHYSICS.md` for steps and limits. No car files are modified or unpacked. Includes the preview.18/.19 fixes below; public GitHub downloads remain preview.17.

**Local preview.19 — missed initiation fix:** Unusable wheel-slip readings no longer erase valid initiation/transition evidence. Grip and pedal-slip measurements still reject those readings. Reload a saved run to reanalyze its original samples; initiation timing still needs three complete entries. This build also includes the preview.18 SDK crash containment described below. Public GitHub downloads remain preview.17.

**Local preview.18 — Pit House SDK crash containment:** SDK access now runs in a separate hidden process. A native SDK failure or timeout returns a connection error instead of closing desktop ADT. Failed reads clear selectable values; interrupted Apply retains its backup and reports that settings may have changed. This build preserves the tuning intelligence and the features below. Real Pit House compatibility and the exact cause of the reported tester crash still need verification. The public GitHub release remains preview.17.

**HUGE UPDATE — public beta preview.17:** This update brings the Content Manager companion, explicit pit Save & Apply Tune, current-setup capture, final-drive gearing, pedal-aware diagnosis and sustained-angle goals, adaptive touchscreen controls, clearer guided workflows and FFB provider selection to public testers. Read the [full release notes](https://github.com/T3ddyGrahams/AtomicDriftTuner/releases/tag/v0.9.0-preview.17). Browse SDK folder also now works from the embedded Setup & Paths page. The provider addition and folder fix preserve existing tuning calculations.

**Wheelbase software choice:** Open **Setup & Paths → Car + FFB**, choose **SimHub / AZOM**, **MOZA Pit House**, or **Other wheelbase software / manual**, then **Save & Continue**. Existing users keep SimHub/AZOM by default. Pit House includes manual recommendations and an experimental desktop SDK read/apply/backup/restore path for ten core controls. It requires user-provided MOZA SDK files and SDK-compatible Pit House on the tester PC; real hardware acceptance remains pending. See `docs/PITHOUSE.md`. No vendor DLL is bundled or loaded just by selecting the option. Telemetry, car tuning and FFB generation calculations remain unchanged; provider changes require a fresh baseline for fair FFB comparisons.

**Content Manager integration retained:** The installer and portable build include ADT Companion **0.4.0-preview.1** for Content Manager / Custom Shaders Patch. Open **Remote → Install / Update Companion** to add it to the Assetto Corsa folder saved in Setup & Paths. Exit your driving session first; ADT backs up changed companion files and preserves other mods. Start Remote, launch a new AC session, open ADT Companion and pair with the code. In desktop **AC Setup**, generate/review a setup and choose **Stage for in-game pits**; then use the companion's **Pit setup → Save & Apply Tune** explicitly while stationary in the editable pit setup menu. The action saves unique previous/changed setups and verifies readback; **Restore previous** is an explicit action in the same session. No recording may be active. An unknown outcome blocks recording until acknowledged, or until AC is fully closed and desktop **Clear pending pit action** is used. Follow `docs/PIT_SETUP.md` for the first test, fixed-setup/unsupported-CSP cases and manual fallback. Actual in-game acceptance is still pending. This public beta includes the intervening local companion previews.

In **Telemetry Recorder**, leave **Capture the current car setup through the in-game companion** enabled to use fresh CSP evidence. Wait for the captured-setup status, check the selected car/driver and explicitly confirm the settings you intend to test. Capture does not read wheelbase settings or apply a tune. If it is unavailable, turn it off and use **Attach AC Setup Snapshot...** with the file actually loaded in AC. Setup monitoring is periodic at roughly 1 Hz with a five-second freshness limit. Lost or changed evidence does not discard the recording, but ADT cannot use it to claim a setup improvement.

New users choose their own hardware, car and drift target. Setup asks **Car tuning only** or **Car + FFB**, then **Your Next Step** explains what to do now, when it is complete and what comes next. Short instructions are the default; enable **Show more explanation and examples** for extra help. ADT remembers selections for the current Windows user.

Existing tuning calculations, angle goals, pedal/phase diagnosis, histories, five-second recording recovery and adaptive fullscreen touchscreen remain. For the touchscreen, open **Remote → START REMOTE → INSTALL SIMHUB DASHBOARD**, then launch **ADT Control Center** in SimHub Dash Studio. Tap **Open ADT**, pair, and use **Full screen** if needed. Prepare the desktop recorder once, then use **Start run → Stop run → Save run**. The `/dash` page also works directly on a phone/tablet using the PC's LAN address. See `docs/TOUCHSCREEN.md`, `docs/GUIDED_WORKFLOW.md` and `docs/TELEMETRY_INTELLIGENCE.md`.

**Testing v0.9.0-preview.17?** Follow the [release checklist](https://github.com/T3ddyGrahams/AtomicDriftTuner/blob/main/docs/testing/v0.9.0-preview.17-checklist.md) and review the [compatibility tracker](https://github.com/T3ddyGrahams/AtomicDriftTuner/blob/main/docs/testing/v0.9.0-preview.17-compatibility.md). This guide covers installation and the broader testing workflow.

Thank you for testing Atomic Drift Tuner (ADT).

ADT is currently in public beta. Your feedback helps identify hardware compatibility issues, tuning problems, telemetry inconsistencies, usability problems, and bugs before a stable release.

The installer and portable builds are self-contained. Testers do not need Visual Studio or the .NET SDK.

---

## 1. Choose Your Build

ADT may be distributed as:

- **Installer** — recommended for most testers.
- **Portable ZIP** — extract it to a folder and run `AtomicDriftTuner.exe`.

Do not run the portable version directly from inside the ZIP.

Both formats include `ADTCompanion-ContentManager.zip` and the unpacked `CompanionPayload` folder. Installing desktop ADT does not automatically modify Assetto Corsa. Use the explicit install button in **Remote**, or choose **Open Companion Package** and drag the ZIP into Content Manager. If Windows denies access to the AC folder, Content Manager may offer its usual installation permission prompt; ADT does not change folder permissions or launch the game. For manual copying and safe updates, follow `CompanionPayload/README.md`.

---

## 2. First Launch

Launch the executable from this build; an older shortcut may still open preview.3. In **Setup & Paths**:

1. Choose **Car tuning only** to work on handling/setup while keeping FFB fixed, or **Car + FFB** to include the forces you feel through the steering wheel.
2. Enter the driver name you will reuse for comparisons. Enable the extra-explanation checkbox if you want more help.
3. For **Car + FFB**, choose **SimHub / AZOM**, **MOZA Pit House**, or **Other wheelbase software / manual**. For SimHub/AZOM, answer whether each is installed: Yes, No or Not sure; **Check for Me / Check Again** checks the optional connection. Follow `docs/PITHOUSE.md` for the experimental SDK path. Car-only skips FFB connection setup.
4. Confirm the **Assetto Corsa install** folder containing `content\cars`, and the **Assetto Corsa user data** folder, normally Documents `Assetto Corsa`. Redirected and OneDrive Documents locations are supported. Check the SimHub folder only if using that integration.
5. Choose **Check AC Paths** if needed, then **Save & Continue**. Saving does not apply any car or wheelbase settings.
6. Under **Car & Hardware**, select your wheelbase, rim, pack, car and drift target. Choosing a pack does not choose its first car for you. Hardware identifies the rig for comparisons even in car-only mode.

ADT remembers your choices and edited profile values for this Windows account. A saved installed car is restored only after a scan finds that exact car and folder; if it is unavailable, choose a car explicitly. If active-car detection is enabled, verify the car it finds in AC.

**Upgrading from preview.3:** Choose your own rig/car and workflow once. The old default selections were startup values, not a copy of the developer's saved history. Existing local tunes, Desired Behavior, recordings and reviews remain available. Installer and portable copies use the same ADT data folders for the current Windows user; a separate executable is not a separate user profile.

**Quick check:** Close and reopen ADT after choosing your rig/car. Confirm that your choices return, that a different pack needs an explicit car choice, and that **Change My Setup** can switch between the two workflows. Use a fresh baseline when starting a comparison in a different workflow.

---

## 3. ADT SimHub Bridge

Some ADT features use the optional ADT SimHub Bridge.

Beta packages include a precompiled bridge under:

`BridgePayload`

You do not need PowerShell, Visual Studio, or the SimHub SDK to install the packaged bridge.

In ADT:

1. Open **Setup & Paths**.
2. Fully close SimHub.
3. Choose **Install / Repair Packaged Bridge**.
4. Allow the Windows elevation prompt if required.
5. Start SimHub.
6. Enable **Atomic Drift Tuner Bridge** under SimHub's plugin settings.
7. Restart SimHub if prompted.

ADT should report the bridge/integration status after SimHub is running.

---

## 4. Before Your First Test

Select the hardware and vehicle context that matches your actual session as closely as possible.

Verify:

- wheelbase,
- wheel/rim,
- drift pack,
- installed car,
- Assetto Corsa paths,
- SimHub connection if using live wheelbase control,
- bridge status if using bridge-dependent features.

If ADT automatically detects the active car or pack, verify that the detected information is correct.

---

## 5. Desired Behavior

ADT can use per-car **Desired Behavior** settings to understand how you want a particular car to drive.

Set these according to what you actually want from the car rather than what you think ADT expects.

Desired Behavior may influence setup and tuning recommendations for that car.

When reporting recommendation quality, tell us what Desired Behavior settings you were using.

---

## 6. Telemetry Testing

For telemetry-assisted recommendations:

1. Follow **Your Next Step** to confirm the car/driver, save Desired Behavior and prepare a named baseline setup. Car-only keeps FFB fixed; Car + FFB also guides FFB preparation.
2. Start Assetto Corsa and load that car, track and setup.
3. Open **Telemetry Recorder**, enter the conditions/task and check the driver. With automatic capture enabled, wait for fresh current-setup evidence from the companion; otherwise attach the exact setup file loaded in AC. Confirm the settings in use, then Connect.
4. Record roughly 60–120 seconds with sustained drifts, transitions and normal corrections. Click **Stop → Save Session**. Stop alone does not save.
5. Open **Tuning Assistant** and read **Your next step**. **Why this next step?** explains the evidence; the optional advanced view contains the full diagnosis.
6. If ADT offers a supported recommendation, choose **Plan this test**, prepare that one change and load the new setup or verify the FFB settings. Keep Desired Behavior unchanged.
7. Choose **Record Comparison Run**. Check the baseline, verify fresh capture of the changed setup (or update the manual attachment) and describe the change. Repeat the same section under comparable conditions, then stop and save.
8. Open **Before / After**, then rate how it felt in **Tune & Run History**, add notes and choose **Save Run Review**. Keep, revert manually or test again based on the measured result and your feedback.

SimHub and AZOM are not required for recording. ADT needs enough clean drifting for guided progress; short/interrupted runs remain available for inspection. An inconclusive comparison explains what prevented a fair result.

Before/After testing is especially valuable.

Try to change one major variable at a time when possible so the result is easier to evaluate.

---

## 7. AC Setup Recommendations

When testing AC setup recommendations, pay attention to more than whether the car simply feels "better."

Useful feedback includes changes in:

- initiation,
- front grip,
- rear grip,
- transition behavior,
- stability,
- rotation,
- self-steer behavior,
- throttle response,
- predictability,
- ability to hold angle.

Tell us both what improved and what became worse.

Tradeoffs are useful feedback.

---

## 8. FFB and AZOM Testing

Treat force-feedback changes as safety-sensitive.

Start conservatively and do not apply a recommendation that appears unreasonable for your hardware.

ADT's supported live Apply/Revert workflow uses controlled bridge operations, validation, serialized changes, and readback verification.

If a requested value cannot be verified, the operation should stop rather than continuing through the remaining changes.

If the wheelbase behaves unexpectedly:

1. Stop testing immediately.
2. Reduce or disable FFB if necessary.
3. Restore known-safe settings.
4. Record what happened.
5. Report the issue before attempting to reproduce unsafe behavior.

Do not repeatedly reproduce potentially unsafe wheelbase behavior just to gather more data.

---

## 9. ADT Remote

ADT Remote provides a local-network browser interface that can be used from devices such as an iPhone or other touchscreen.

Open the ADT Remote controls in the Windows application, start the local server, and open the displayed private-network address from a device on the same network.

Pair using the code displayed by ADT.

Remote write capabilities should remain disabled unless you intentionally enable them from the Windows application.

Do not expose the ADT Remote service directly to the public Internet.

---

## 10. Diagnostics

Use **System Diagnostics** if something is not working correctly.

For issues involving:

- path detection,
- SimHub,
- AZOM,
- the ADT SimHub Bridge,
- telemetry connections,
- active-car detection,
- crashes,
- configuration or profile persistence,

please create an **Export Support Package** when possible.

The support package is designed to contain diagnostic information needed for troubleshooting while excluding user tuning and telemetry content that is not required for support.

Review the package before sharing it if you have privacy concerns.

---

## 11. Reporting Bugs

A useful bug report should include:

- ADT version,
- installer or portable build,
- Windows version,
- wheelbase,
- wheel/rim,
- firmware version when relevant,
- SimHub version when relevant,
- AZOM version when relevant,
- drift pack,
- car,
- track when relevant,
- what you expected,
- what actually happened,
- steps to reproduce it,
- whether it happens consistently,
- screenshots or video when useful,
- support package when applicable.

Report bugs through the ADT GitHub **Bug Report** issue form.

---

## 12. Beta Test Reports

You do not need to find a bug to submit useful feedback.

Successful tests are valuable too.

Use the GitHub **Beta Test Report** form to report:

- tuning results,
- FFB/AZOM results,
- telemetry quality,
- Desired Behavior results,
- hardware compatibility,
- bridge behavior,
- installation/update testing,
- UI/workflow feedback,
- Before/After results.

Tell us what worked as well as what did not.

---

## 13. What We Need Most

During the public beta, the most valuable testing is:

- different wheelbases and rims,
- different drift packs and cars,
- AC setup recommendation accuracy,
- FFB/AZOM recommendation accuracy,
- telemetry reliability,
- active-car and pack detection,
- SimHub bridge reliability,
- clean installation,
- upgrading between ADT versions,
- portable-build testing,
- profile/configuration persistence,
- confusing or frustrating UI workflows.

If something feels wrong, confusing, inconsistent, or unnecessarily difficult, report it.

That feedback matters even when ADT technically "works."

---

## Safety

ADT provides tuning recommendations and integration tools for simulation hardware.

Always review recommendations before applying them.

Force-feedback behavior varies significantly between wheelbases, firmware versions, rims, vehicle configurations, and software environments.

Keep physical access to your wheelbase's power or emergency-stop controls when testing unfamiliar FFB behavior.

---

## Thank You

Every useful test helps make ADT more accurate, reliable, and easier to use.

The goal is not just to find crashes.

We want to know whether ADT correctly understands what the car is doing, recommends changes that make sense for the driver's goal, and helps verify whether those changes actually improved the car.
# Final-drive gearing (preview.5)

Open **AC Setup → Gearing • Final-drive planner**. Choose a saved setup for the selected car, enter your desired drift gear, speed range and RPM range, then calculate. Use **Save target for this car** to remember the inputs, and **Save gearing setup…** to export a separate setup changing only the final drive. Load it in Assetto Corsa and compare the same section against your baseline.

The initial targets are examples. Predictions assume no tyre/clutch slip and use the driven tyre's nominal radius; they do not identify your engine's power band. Version 1 needs unambiguous unpacked car data and supported ratio definitions. Packed/encrypted cars, duplicate ratio labels, AWD and adjustable limiters receive an explanation instead of a guessed result. See the repository's `docs/GEARING.md` for details.
