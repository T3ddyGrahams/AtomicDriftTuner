# Atomic Drift Tuner (ADT) — Beta Tester Guide

Thank you for testing Atomic Drift Tuner (ADT).

ADT is currently in public beta. Your feedback helps identify hardware compatibility issues, tuning problems, telemetry inconsistencies, usability problems, and bugs before a stable release.

The installer and portable builds are self-contained. Testers do not need Visual Studio or the .NET SDK.

---

## 1. Choose Your Build

ADT may be distributed as:

- **Installer** — recommended for most testers.
- **Portable ZIP** — extract it to a folder and run `AtomicDriftTuner.exe`.

Do not run the portable version directly from inside the ZIP.

---

## 2. First Launch

On first launch, ADT will guide you through **Setup & Paths**.

Confirm or browse to:

1. **SimHub** — the folder containing `SimHubWPF.exe`.
2. **Assetto Corsa install** — the folder containing `content\cars`.
3. **Assetto Corsa user data** — normally your Windows Documents `Assetto Corsa` folder.

Redirected Documents and OneDrive Documents locations are supported.

Use **Test Everything** to validate the detected paths, then choose **Save & Continue**.

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
- SimHub connection,
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

1. Start Assetto Corsa and load the car you want to test.
2. Confirm ADT sees the correct active context.
3. Open **Telemetry Recorder**.
4. Record a representative drifting session.
5. Include sustained drifts, transitions, and normal corrections where possible.
6. Save the session.
7. Open **Tuning Assistant**.
8. Review the observations, recommendations, and confidence.
9. Make only changes you are comfortable testing.
10. Record another representative session after the change.

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
