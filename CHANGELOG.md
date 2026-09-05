# Atomic Drift Tuner — Changelog

This changelog tracks public Atomic Drift Tuner (ADT) releases.

Development experiments, temporary validation builds, and internal test revisions
are intentionally omitted. Detailed notes for current releases are also available
through GitHub Releases.

---

## v0.8.1-beta.1 — Modern Workflow UI + Automatic Pack Discovery

### Added

- Rebuilt the Windows application around a modern single-window ADT workspace.
- Embedded major tools into the main application, including:
  - Full AZOM Settings
  - AC Car Setup Tuner
  - Telemetry Recorder
  - Tuning Assistant
  - Share Codes
  - ADT Remote
  - System Diagnostics
  - Setup & Paths
  - Updates
- Embedded pages preserve state while navigating between tools.
- Added the guided six-step tuning workflow:
  **Car & Rig → Desired Behavior → Generate Tune → Drive & Telemetry → Refine → AC Setup**.
- Added automatic custom drift-pack discovery using strong shared car-folder prefixes.
- Added unified modern styling for navigation, cards, controls, inputs, dropdowns,
  sliders, scrollbars, and embedded tool pages.

### Changed

- Appearance remains intentionally modeless so themes can be edited beside the
  active ADT workspace.
- Built-in drift-pack signatures continue to take priority over automatically
  detected pack groups.
- Automatically detected packs rebuild during installed-car scans so removed or
  added cars do not leave stale pack data.

### Fixed

- Fixed active-car auto-selection when AC/CSP shared memory reports a numeric
  car slot rather than the installed car folder ID.
- Added safe fallback to the current Assetto Corsa `race.ini` model when needed.
- Fixed active-car matching for long mod-folder names where Assetto Corsa shared
  memory exposes only a truncated car identifier.

### Existing systems retained

- Hardware-aware tune generation.
- MOZA/AZOM recommendations and guarded live-write workflow.
- Assetto Corsa active-car detection and installed-car scanning.
- Desired Behavior.
- AC Car Setup Tuner.
- Telemetry Recorder.
- Tuning Assistant.
- Calibration and profiles.
- ADT Remote.
- Share Codes.
- System Diagnostics.
- Manual GitHub update checking and downloading.

The required ADT SimHub Bridge for this release remains v0.7.2.

---

## v0.8.0-beta.1 — ADT Remote + Automatic AC Context

### ADT Remote

- Added a same-LAN mobile/browser companion hosted by the Windows ADT application.
- Added six-digit pairing and randomized browser authentication.
- Remote credentials rotate when the server starts or a new pairing code is generated.
- Restricted remote access to loopback/private-network clients.
- Remote AZOM writes reset to OFF when the server starts and require explicit
  Windows-side opt-in.
- Added mobile Dashboard, Tune, Behavior, and AZOM views.
- Added live Assetto Corsa speed, slip angle, steering angle, FFB output, and
  drift-detection information.
- Added current hardware, wheel, pack, car, and Drift Target context.
- Added remote Drift Target selection.
- Added remote tune-generation requests while keeping tune generation authoritative
  in the Windows application.
- Added mobile review of AZOM/MOZA and Assetto Corsa FFB recommendations.
- Added per-car Desired Behavior editing.
- Added supported live AZOM readback and guarded Apply/Revert controls.

### Automatic Assetto Corsa context

- Added installed-car scanning.
- Added active-car detection from Assetto Corsa shared memory.
- Added automatic matching to the installed car folder.
- Added automatic drift-pack inference.
- Added automatic car and pack selection when enabled.
- Added independent controls for automatic scanning and active-car selection.

### Safety

ADT Remote is intended for private same-LAN use. Its HTTP service should not be
directly exposed to the public Internet.

---

## v0.7.3-beta.2 — Compile Fix

### Fixed

- Restored `MainWindow.Number(string value, string label)` after it was
  accidentally removed during the v0.7.3 UI changes.
- Fixed the resulting `The name 'Number' does not exist in the current context`
  build error.

No tuning, telemetry, AZOM, theme, or bridge behavior changed.

---

## v0.7.3-beta.1 — Checkbox Readability + Live Theme Editing

### Added

- Added configurable checkbox text, background, border, and check-mark colors.
- Added application-wide checkbox styling.
- Added checkbox examples to the Appearance preview.
- Extended contrast checking to checkbox text and surfaces.

### Changed

- Appearance now opens modelessly for live theme editing beside ADT tools.
- Normal tool windows were moved to modeless operation.
- ADT keeps one instance of each normal tool window to avoid duplicate stateful views.
- The first-run Setup & Paths wizard remains modal.

These changes affected UI behavior only and did not change tuning or AZOM logic.

---

## v0.7.2-beta.1 — AZOM Write Guard Hardening

### Added

- Added serialized AZOM Apply/Revert batches.
- Added serialized bridge requests.
- Added duplicate-target suppression.
- Added minimum spacing between direct AZOM commits.
- Added live readback verification for each supported setting.
- Added stop-on-first-unverified behavior.
- Added a 500 ms debounce service for any future interactive write workflow.

The normal UI continues to require explicit Apply/Revert rather than continuously
writing settings while values are edited.

The required ADT SimHub Bridge version became v0.7.2.

---

## v0.7.1-beta.1 — UI Readability & Theme Control

### Added

- Added independent theme controls for:
  - input fields;
  - DataGrid rows and headers;
  - selected rows;
  - tabs;
  - dropdowns;
  - borders and grid lines.
- Added expanded Appearance previews.
- Added contrast checks for major text/background combinations.

### Changed

- Application-level dynamic resources now provide consistent readable styling
  across ADT tools.

No SimHub Bridge behavior changed in this release.

---

## v0.7.0-beta.1 — Telemetry-Assisted Tuning

### Added

- Added the Tuning Assistant.
- Added telemetry-backed comparison of Desired Behavior against observed behavior.
- Added telemetry-assisted recommendations for:
  - transition speed;
  - self-steer speed;
  - angle stability;
  - oscillation control;
  - FFB clipping/headroom.
- Added preserve-good-settings logic.
- Added bounded calibration recommendations.
- Added telemetry-assisted Assetto Corsa FFB recommendations.
- Added temporary AC setup guidance.
- Added before/after telemetry comparison.
- Added LOW / MEDIUM / HIGH recommendation confidence.

### Safety and scope

- Telemetry-supported recommendations remain reviewable rather than automatically
  applied to hardware.
- ADT explicitly avoids claiming telemetry conclusions when the available signal
  does not support them.
- Existing setup range and click safeguards remain in control of AC setup recommendations.

---

## v0.6.3-beta.1 — Beta Distribution

### Added

- Added the first-run Setup & Paths wizard.
- Added automatic/manual SimHub and Assetto Corsa path detection.
- Added packaged ADT SimHub Bridge installation and repair.
- Added System Diagnostics.
- Added privacy-conscious support ZIP export.
- Added portable beta packaging.
- Added Inno Setup installer support.
- Added self-contained Windows x64 release packaging.

### Changed

- Machine-specific paths are stored locally rather than embedded in shared tune data.
- The AC setup tuner uses the configured Assetto Corsa user-data location.

---

## v0.6.2 — Desired Behavior Blending

### Added

- Added conflict-aware blending between multiple Desired Behavior goals.
- Added diminishing returns when several behavior goals push the same setup
  parameter in the same direction.
- Added compromise handling when behavior goals oppose each other.
- Added session-intent priority when Desired Behavior conflicts with the selected
  high-level driving intent.
- Added Behavior Blend preview information.
- Added blend-state explanations to setup recommendations.
- Added the **Fast + Stable** Desired Behavior preset.

Per-car Desired Behavior persistence remained compatible with v0.6.1 profiles.

---

## v0.6.0 — Live AZOM Integration

### Added

- Added live AZOM readback through the isolated ADT SimHub Bridge.
- Added comparison between current AZOM values and generated ADT recommendations.
- Added guarded Apply and Revert workflows.
- Added pre-apply snapshots.
- Added per-setting selection for supported live changes.
- Added exact supported AZOM commits where required.
- Added live readback verification after writes.
- Added stop-on-first-unverified behavior.
- Added a Last Batch view showing Before → Target → Actual After results.
- Added compatibility handling for supported AZOM versions.
- Added named-pipe safety and reliability improvements.
- Added timeouts and crash handling around live bridge communication.

### Appearance

- Added customizable application themes.
- Added built-in color presets.
- Added HSV color-wheel editing.
- Added independent dropdown styling and contrast checking.

### Safety

- Live changes require explicit user action.
- Only supported settings are eligible for automatic changes.
- Settings are verified from live AZOM state after application.
- A failed verification stops the active batch.
- Revert targets values captured before ADT's previous apply operation.

The ADT SimHub Bridge remains isolated from the main .NET application so SimHub
dependencies cannot break the primary desktop application.

---

## Earlier Development

Versions prior to v0.6.0 established the initial ADT tuning engine, hardware and
wheel profiles, drift-pack support, Assetto Corsa scanning, calibration,
telemetry recording, setup recommendations, profile storage, and the foundation
used by later public beta releases.
