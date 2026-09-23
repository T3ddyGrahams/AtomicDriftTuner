# Atomic Drift Tuner — Changelog

This changelog tracks public Atomic Drift Tuner (ADT) releases and explicitly marked local previews. Preview.17 rolls the intervening local previews into a public beta update.

## Local companion 0.4.0-preview.2 — CSP setup capture compatibility

- Accept CSP's callable car-reader API in current-setup capture and guarded pit preflight. The old function-only check incorrectly reported a supported CSP API as unavailable.
- Add runtime-shaped regression fixtures while retaining replay, pause, identity, parked-car and setup checks. Fix the UI test harness's multi-return loadfile argument for LuaJIT.
- Standalone companion update for existing desktop preview.21; no tuning calculations, physics files or desktop version changed. The tuning-engine audit fixes remain pending. Live-game acceptance still needs confirmation after restarting the session.

## Local v0.9.0-preview.21 — Packed car data and verified setup meanings

- Read supported ordinary data.acd archives into a bounded private memory cache without installing tools or changing car files. Keep protected, corrupt, ambiguous and unsupported sources explicit; never execute car scripts.
- Decode final-drive indexes, documented individual gears/gearsets and supported CSP engine-map selections using the selected car's own definitions. Show numeric ratios separately from display labels, sources, and Verified mapping / Partial understanding / Unsupported states.
- Extend the gearing planner to supported packed cars with identical calculations and source checks. Identify RPM estimates as based on engine.ini, not verified ECU/script limits.
- Show saved-setting meanings in AC Setup and retain them in new run/tune snapshots and comparisons. Flag unnamed values; refuse wrong-car baselines and stale archive/map data. A decoded engine-map multiplier is not a horsepower measurement or proof of in-game application.
- Expand file fingerprints to the complete packed archive or supported unpacked text files. Record a fresh preview.21 baseline before comparing; older fingerprints have narrower coverage.
- Preserve the existing telemetry, Desired Behavior, FFB workflows, and preview.18/.19 fixes. No public release has been made for this local build.

## Local v0.9.0-preview.20 — Readable base car physics

- Add optional read-only import of supported unpacked suspension, tyre, drivetrain, engine, brake and car data, with source labels and the saved baseline's selected tyre compound.
- Show base context beside setup recommendations while preserving saved clicks/indexes. Hold controls not exposed by readable setup.ini and unverified rear-drive differential advice for FWD/AWD cars.
- Refuse stale generation/export/pit staging after imported file changes. Record a digest in new history snapshots and flag differing/unmatched base physics in before/after comparisons.
- Preserve saved setups, car files, existing tuning heuristics and the preview.18/.19 fixes. Packed/ambiguous sources fall back with a clear status. No unpacking, CSP/geometry simulation or automatic optimal-tune claim.
- Add a themed, scrollable review panel and a bundled [car physics guide](docs/CAR_PHYSICS.md).

This local build has not replaced public preview.17. Live-game and hardware acceptance remain tester checks.

## Local v0.9.0-preview.19 — Preserve initiation evidence when wheel-slip is unusable

- Separate optional wheel-slip validity from core motion validity. Out-of-range/nonfinite axle readings no longer reset initiation, transition or angle evidence when motion remains usable.
- Exclude unusable wheel-slip from axle averages, grip proxies and affected pedal-slip responses; preserve angle/yaw pedal responses. Save a separate validity flag for sanitized SDK readings and explain excluded wheel-slip counts in the analysis.
- Explain that initiation timing requires three complete entries. Do not lower detection thresholds or relabel linked transitions as fresh entries.
- Reanalyze saved recordings on load without rewriting their raw data. Retain the preview.18 Pit House process isolation fix.

This local hotfix has not replaced public preview.17. A restored event count does not establish that a tune improved the car.

## Local v0.9.0-preview.18 — Pit House SDK crash containment

- Move native MOZA SDK initialization, reads, writes and teardown into a hidden helper process communicating over a random pipe restricted to the current Windows user.
- Keep ADT open when the SDK process exits or a request times out. Show a connection error, clear stale selections and leave Apply disabled after a failed Read.
- Preserve explicit Apply, provider/device/staleness checks, durable original-value backups and uncertain-write reporting. Never reconnect or retry a failed request automatically.
- Test abrupt initialization/read/write exits, ordinary SDK errors, timeout and teardown failure using isolated fixture processes; test the actual helper startup with missing SDK files without loading vendor code.
- Tuning calculations, telemetry diagnosis, gearing and SimHub/AZOM behavior are unchanged. The reported tester crash's exact native cause and real Pit House hardware compatibility remain unconfirmed.

This local hotfix has not replaced the public preview.17 release.

## v0.9.0-preview.17 — HUGE UPDATE

- Bring recording, connection status, next steps, findings, test planning and comparison feedback into Assetto Corsa with bundled ADT Companion 0.4.0-preview.1.
- Add explicit pit Save & Apply Tune, previous/changed setup saves, verified readback, same-session restore and current-setup capture, with manual fallbacks for unsupported combinations.
- Add a final-drive gearing planner, pedal-aware diagnosis, sustained-angle goals and an adaptive touchscreen dashboard.
- Simplify the baseline/change/compare workflow with Car tuning only or Car + FFB, optional extra explanations and clean first-run hardware/car selections.
- Add SimHub/AZOM, MOZA Pit House and manual FFB provider choices. The optional Pit House SDK read/apply/backup/restore path supports ten core controls; actual hardware acceptance is pending and vendor DLLs are not bundled.
- Recover from brief telemetry gaps and fix SDK folder browsing in the embedded Setup & Paths workspace.

This is a beta preview with live-game/hardware acceptance still pending. The provider addition and final folder-picker fix preserve existing tuning calculations. See the [full release notes](docs/releases/v0.9.0-preview.17.md) and [testing checklist](docs/testing/v0.9.0-preview.17-checklist.md).

---

## Local v0.9.0-preview.15 — Explicit Pit Save & Apply Tune

- Stage an immutable numeric plan from the existing AC Setup generation workflow, then explicitly **Save & Apply Tune** from the new companion **Pit setup** tab.
- Require the matching car and baseline, stationary editable pit setup menu, no recording and valid editable values. Save uniquely named previous/changed setups, verify current-setup readback and offer explicit **Restore previous** in the same game session.
- Keep recording blocked while an action outcome is unresolved. Preserve manual export/load and setup attachment for unsupported CSP/car combinations.
- Rename feedback regeneration to **Update recommendations only** to clarify that it does not apply settings. Goals, feedback and generation do not automatically change the car or FFB.
- Bundle Companion `0.4.0-preview.1`, including `pit_setup.lua`, with both desktop packages. Live-game Save & Apply Tune, restore and refusal-case acceptance remain pending.

See the [first test and recovery instructions](docs/PIT_SETUP.md). This preview has not been published as a public release.

---

## Local v0.9.0-preview.14 — Current Setup Capture

- Bundle ADT Companion `0.3.0-preview.1` with read-only CSP current-setup capture, alongside in-game recording, findings, test planning and comparison feedback.
- Capture numeric setup values from the current CSP state, including unsaved pit edits according to the installed SDK. Retain manual setup-file attachment when capture is unavailable.
- Bind capture responses to the recorder's challenge and live car/track; limit input to 64 KiB and 512 numeric VALUE sections. Save numeric evidence and its fingerprint without raw INI metadata or paths.
- Check setup evidence periodically at roughly 1 Hz, with a five-second freshness limit. If evidence is lost or the observed setup/session changes during a run, retain the samples but mark the setup evidence unsuitable for an improvement claim. This is periodic observation, not continuous verification.
- Show provisional recording progress while preserving existing tuning calculations and driver confirmation of FFB settings. Real CSP rendering, unsaved-edit capture and full baseline/change/comparison acceptance remain pending.

See [companion setup and test guidance](docs/COMPANION.md). This preview has not been published as a public release.

---

## v0.9.0-preview.3 — Intelligence, Guided Workflow & Appearance

- Add phase-aware diagnosis tied to Desired Behavior and driver-specific run/tune comparisons.
- Guide setup based on SimHub/AZOM availability and the next tuning step.
- Expand Appearance to 72 color roles and fix unreadable section headings; Remote uses the saved palette.
- Pass 167 theme assertions, 252 layout assertions and 50 regression scenarios. Released as a preview at the maintainer's request with hands-on/live-driving checks pending.

See [release notes](docs/releases/v0.9.0-preview.3.md) and the [tester checklist](docs/testing/v0.9.0-preview.3-checklist.md).

---

## v0.8.3-beta.1 — Adaptive UI & New App Icon

- Reflow dashboard cards and car setup controls for narrow windows and portrait displays.
- Keep Save actions accessible while tool content scrolls; improve wrapping and table overflow.
- Add collapsible, adjustable navigation and preserve embedded-page styles and keyboard visibility.
- Add per-monitor display scaling and monitor work-area sizing.
- Add the new ADT application, shortcut, title-bar, and installer icon.
- Pass 156 layout geometry assertions and all 18 existing regression scenarios; maintainer reported successful installed-preview testing.

See [release notes](docs/releases/v0.8.3-beta.1.md) for downloads, upgrade guidance, and testing scope.

---
## v0.8.2-beta.1 — Reliability & Recovery

- Hardened saved settings, profiles, calibration, recovery records and update handling.
- Added stale-source checks for AZOM Apply/Revert and remote undo.
- Fixed telemetry reconnect/freshness and failed-save file preservation.
- Reject changed AC setup baselines, invalid shared identities and unsupported telemetry formats.
- Fixed bridge compilation and strengthened UI lifecycle/error handling.
- Added 18 regression scenarios, including 8,000 built-in tuning combinations.
- Added share registry error handling that keeps internal storage errors private (separate service deployment required).

See [release notes](docs/releases/v0.8.2-beta.1.md) for upgrade instructions and testing focus.

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
