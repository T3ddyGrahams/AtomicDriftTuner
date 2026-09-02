# Atomic Drift Tuner v0.7.3 Beta Tester Guide

This beta is intended to run without Visual Studio or a .NET SDK when distributed
through the self-contained portable package or installer.

## First launch

Atomic opens **Setup & Paths** the first time it runs.

Confirm or browse to:

1. **SimHub** — the folder containing `SimHubWPF.exe`.
2. **Assetto Corsa install** — the folder containing `content\cars`.
3. **Assetto Corsa user data** — normally the Windows Documents `Assetto Corsa`
   folder. Redirected and OneDrive Documents locations are supported.

Use **Test Everything**, then **Save & Continue**.

## Atomic SimHub Bridge

Beta packages contain a precompiled bridge under `BridgePayload`.

Open **Setup & Paths** and choose **Install / Repair Packaged Bridge**.

- Fully exit SimHub before installing.
- If SimHub is inside a protected Windows folder, Atomic can request UAC
  elevation for the bridge-copy operation only.
- Restart SimHub and enable `Atomic Drift Tuner Bridge` under SimHub plugins.

Testers do not need PowerShell, Visual Studio, or the SimHub SDK.

## Diagnostics

Use **System Diagnostics** from the main window.

If something fails, click **Export Support Package** and send the resulting ZIP
to the Atomic Drift Tuner developer.

The support package includes:

- redacted diagnostics,
- redacted machine-path settings,
- Atomic log files.

It does **not** include telemetry sessions, saved tune profiles, Assetto Corsa
setup files, or per-car behavior-profile contents.

## What to report

Please include:

- hardware (wheelbase + wheel),
- drift pack/car,
- what you expected,
- what happened,
- the support ZIP when the issue involves detection, SimHub/AZOM, paths, crashes,
  or telemetry connection.


## v0.7 Tuning Assistant test flow

1. Select the exact wheelbase + wheel + drift pack + installed car.
2. Open **Telemetry Recorder**.
3. Record a representative run with several sustained drifts and transitions.
4. Click **Save Session**.
5. Open **Tuning Assistant**.
6. Review Desired vs Observed, recommendations, and confidence.
7. Apply calibration only if the proposed AZOM/AC FFB delta makes sense.
8. Use **Open AC Setup with Guidance** to test temporary car-behavior guidance.
9. Save another telemetry session after the change to populate Before / After.

The assistant does not automatically write AZOM or overwrite AC setups.


## AZOM write-safety in v0.7.2

Live Apply is explicit and verified. Atomic suppresses already-matched targets,
serializes Apply/Revert batches, spaces direct compatibility commits, and stops a
batch if readback does not match.

Atomic does not currently send a write for every slider movement. Future
write-on-edit controls are required to use a 500 ms last-value-wins debounce.


## Live appearance editing

`Customize Appearance` is modeless in v0.7.3. Leave it open while normal Atomic
tool windows are open. Color-wheel previews update application-level WPF brush
resources only; they do not change tuning, telemetry, calibration, AC setup, or
AZOM values.

Checkbox label, box background, border, and check-mark colors are independently
customizable.
