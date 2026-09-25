# Atomic Drift Tuner (ADT) — Beta Tester Guide

**Local preview.34 — adjustable rev limits in gearing:** Standard adjustable engine limiters can now be read from your saved baseline and the car's definition. Open **AC Setup → Gearing**, select the setup you intend to drive, and use **Read car and suggest RPM range** before calculating again. ADT uses the resulting setup rev limit for its gearing estimates. It preserves the limiter when saving a final-drive change; it does not read live ECU/script overrides. If your current final drive is already the closest available match, there is no new gearing setup to save. See `docs/GEARING.md`.

**Included from preview.33 — Logitech G27 support:** Choose the G27 wheelbase and open **Wheelbase Settings** for the legacy Logitech Gaming Software / Profiler controls. Save your manual plan, enter and verify it in Logitech/AC, then record and compare. ADT captures all eight Logitech controls for before/after review. It does not read or apply Logitech settings; G27 automatic FFB calibration uses AC gain only. Car tuning intelligence remains available. See **Logitech G27** in `docs/GUIDED_WORKFLOW.md` for instructions and the hardware acceptance checklist.

**Included from preview.32 — see exactly what changes:** **Your setup changes** now appears when reviewing a generated setup or one focused adjustment, and in **Tuning Assistant → Before / After** for recorded runs. Readable names, before/after values, differences and explanations are together. Changed settings are highlighted; **Show all settings** includes unchanged controls. Missing evidence is labelled, and generated FFB targets are distinct from captured car settings. Diagnosis, tuning calculations and comparison scoring are unchanged. Verify desktop **0.9.0-preview.34**; this package carries Bridge **0.9.0-preview.34** with unchanged logic and Companion **0.4.0-preview.4**. No companion reinstall is needed for this update. Public preview.33 remains the published release.

**Camera compatibility fix in preview.31:** Cockpit driver-eye position/pitch edits no longer cause a false packed/unpacked conflict when all other supported car content matches. Reload the intended baseline and calculate again. Both source copies remain fingerprinted; ADT does not alter your camera, installed car or original saved setup.

**Included from preview.31 — clearer setup recommendations:** Open **Tuning Assistant → Your next step → Review one car setup change** after saving a confirmed baseline run. Choose the matching setup file, review exact current → proposed values and the possible tradeoff, then save or stage one adjustment. Load or explicitly apply it in the pits, record again and compare. ADT tracks the exact test without changing the diagnosis or tuning formulas. Unsupported or unverified changes remain unavailable. See `docs/GUIDED_WORKFLOW.md`.

**HUGE UPDATE — public beta preview.30:** This build includes all changes since public preview.17: easier corner-based gearing goals, deeper Gearing & ECU review, readable packed/unpacked car data, recording readiness alerts, and fixes for Pit House SDK crashes, missed telemetry, setup capture and pit staging. Read the [release notes](https://github.com/T3ddyGrahams/AtomicDriftTuner/releases/tag/v0.9.0-preview.30).

**Start here after upgrading:** Close ADT before installing, then reopen and verify **0.9.0-preview.34**. Reload your intended baseline before generating/staging. If upgrading from preview.17, exit the AC driving session, use **Remote → Install / Update Companion** for **0.4.0-preview.4**, then start a new session. If updating the packaged SimHub/AZOM bridge, close SimHub first and restart afterwards; its logic has not changed in this update. Record a fresh baseline for the expanded evidence and source snapshots if upgrading from an older public version.

**Recording progress:** Useful time adds up across shorter corners and drifts. The evidence panel lists what is still missing. **READY TO REVIEW** means the selected checks passed; **READY FOR PARTIAL REVIEW** lets you inspect useful evidence while missing measurements remain unknown. Optional PC/device chimes and the companion notification occur once per recording. Stop and save manually when ready. Background buffering preserves acquired frames during busy UI updates, but cannot reconstruct old missing samples or genuine outages.

**Car definitions and pit staging:** Supported car data is read in memory without changing the installed car. Exact matching packed/unpacked copies are accepted; conflicting copies stay blocked. Readable independent sections remain available. A conflicting setup control, such as duplicated brake bias, is left unchanged with an explanation while valid controls and unrelated gearing can still work. Standard camber uses verified saved-value steps. Broader tuning-engine audit work remains open; these fixes do not establish universally optimal tuning or proven recommendation attribution.

**Gearing & ECU review:** Choose representative corner gears/speeds, review the provisional engine RPM estimate, and save the targets for future recordings. The Tuning Assistant's Gearing & ECU tab relates observed RPM/speed to those recorded targets and shows supported setup meanings. Test gearing separately from ECU. File-defined ECU labels/multipliers are not measured horsepower; before/after associations do not prove causation.

**Wheelbase software choice:** Open **Setup & Paths → Car + FFB**, choose **SimHub / AZOM**, **MOZA Pit House**, or **Other wheelbase software / manual**, then **Save & Continue**. Existing users keep SimHub/AZOM by default. Pit House includes manual recommendations and an experimental desktop SDK read/apply/backup/restore path for ten core controls. It requires user-provided MOZA SDK files and SDK-compatible Pit House on the tester PC; real hardware acceptance remains pending. See `docs/PITHOUSE.md`. No vendor DLL is bundled or loaded just by selecting the option. Telemetry, car tuning and FFB generation calculations remain unchanged; provider changes require a fresh baseline for fair FFB comparisons.

**Content Manager integration retained:** The installer and portable build include ADT Companion **0.4.0-preview.4** for Content Manager / Custom Shaders Patch. Open **Remote → Install / Update Companion** to add it to the Assetto Corsa folder saved in Setup & Paths. Exit your driving session first; ADT backs up changed companion files and preserves other mods. Start Remote, launch a new AC session, open ADT Companion and pair with the code. In desktop **AC Setup**, generate/review a setup and choose **Stage for in-game pits**; then use the companion's **Pit setup → Save & Apply Tune** explicitly while stationary in the editable pit setup menu. The action saves unique previous/changed setups and verifies readback; **Restore previous** is an explicit action in the same session. No recording may be active. An unknown outcome blocks recording until acknowledged, or until AC is fully closed and desktop **Clear pending pit action** is used. Follow `docs/PIT_SETUP.md` for the first test, fixed-setup/unsupported-CSP cases and manual fallback. Actual in-game acceptance is still pending. This public beta includes the intervening local companion previews.

In **Telemetry Recorder**, leave **Capture the current car setup through the in-game companion** enabled to use fresh CSP evidence. Wait for the captured-setup status, check the selected car/driver and explicitly confirm the settings you intend to test. Capture does not read wheelbase settings or apply a tune. If it is unavailable, turn it off and use **Attach AC Setup Snapshot...** with the file actually loaded in AC. Setup monitoring is periodic at roughly 1 Hz with a five-second freshness limit. Lost or changed evidence does not discard the recording, but ADT cannot use it to claim a setup improvement.

New users choose their own hardware, car and drift target. Setup asks **Car tuning only** or **Car + FFB**, then **Your Next Step** explains what to do now, when it is complete and what comes next. Short instructions are the default; enable **Show more explanation and examples** for extra help. ADT remembers selections for the current Windows user.

Existing tuning calculations, angle goals, pedal/phase diagnosis, histories, five-second recording recovery and adaptive fullscreen touchscreen remain. For the touchscreen, open **Remote → START REMOTE → INSTALL SIMHUB DASHBOARD**, then launch **ADT Control Center** in SimHub Dash Studio. Tap **Open ADT**, pair, and use **Full screen** if needed. Prepare the desktop recorder once, then use **Start run → Stop run → Save run**. The `/dash` page also works directly on a phone/tablet using the PC's LAN address. See `docs/TOUCHSCREEN.md`, `docs/GUIDED_WORKFLOW.md` and `docs/TELEMETRY_INTELLIGENCE.md`.

**Testing v0.9.0-preview.30?** Follow the [release checklist](https://github.com/T3ddyGrahams/AtomicDriftTuner/blob/main/docs/testing/v0.9.0-preview.30-checklist.md) and review the [compatibility tracker](https://github.com/T3ddyGrahams/AtomicDriftTuner/blob/main/docs/testing/v0.9.0-preview.30-compatibility.md). This guide covers installation and the broader testing workflow.

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

Launch the executable from this build; an older shortcut may still open an earlier preview. In **Setup & Paths**:

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
# Gearing for your corners

Open **AC Setup → Gearing** and choose the saved setup you intend to drive. To retain generated handling changes, use that handling setup as the baseline here. Choose a gear and speed window for tight corners and one for long sweepers, or retain a single-gear target. Select mph/km/h and optionally save it as the default for new car targets. Existing car targets keep their units.

If you do not know your speeds, expand **Not sure about your speeds? Use a recorded run**, find/select a representative run, then use its speeds. Every requested gear needs at least ten seconds of reliable drift evidence. ADT cannot identify corner shape from gear alone. Initial speed boxes contain examples, not a personalized result.

**Read car and suggest RPM range** offers a provisional starting estimate from a supported base engine curve. Review it or enter your own range under Advanced. The estimate excludes ECU/turbo/script effects. Use **Find gearing for my corners** to compare the car's real final-drive choices against both goals. The result explains when no preset fits both. Individual gears/gearsets stay as saved; fixed final drives can be assessed but cannot be changed.

**Save targets for this car** remembers your goals. **Save gearing setup…** exports a separate setup changing only final drive; load it explicitly in AC and compare similar driving. Packed and unpacked supported car data work; protected/custom mappings, conflicting sources, AWD and unsupported adjustable limiters retain clear limitations. Predictions use nominal tyre radius without tyre/clutch slip. See `docs/GEARING.md`.
