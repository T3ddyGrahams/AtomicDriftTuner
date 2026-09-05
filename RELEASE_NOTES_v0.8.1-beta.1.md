# Atomic Drift Tuner v0.8.1-beta.1

This beta is a major usability and UI release for ADT. It keeps the existing tuning, telemetry, AZOM, AC setup, remote, profile, diagnostics, and update systems while moving the Windows app into a cohesive modern single-window workflow.

## Highlights

- Completely redesigned modern ADT desktop shell.
- Major tools now open inside the main ADT workspace instead of spawning separate windows.
- Embedded pages preserve their state while switching between tools.
- **Appearance is intentionally the only normal tool that opens in its own modeless window**, allowing live theme changes beside the active workspace.
- New guided tuning workflow:
  **Car & Rig → Desired Behavior → Generate Tune → Drive & Telemetry → Refine → AC Setup**.
- Unified dark control styling across the app, including redesigned scrollbars, buttons, sliders, dropdowns, text fields, cards, navigation states, and embedded-page framing.
- Sidebar navigation now clearly separates the tuning workflow from system utilities.
- Dashboard surfaces current car/hardware context, driver intent, telemetry, generated recommendations, calibration, and next-step actions.
- Automatic custom drift-pack discovery during installed-car scans. Unknown cars that share a strong folder prefix can now be grouped into an auto-detected pack—for example, `matsuri_mayhem_*` becomes **Matsuri Mayhem**.
- Built-in pack signatures still take priority over auto-detected groups.
- Conservative prefix filtering avoids treating obvious manufacturer/chassis naming patterns as fake packs.
- Auto-detected packs are rebuilt on each scan so added/removed cars do not leave stale pack entries.

## Existing systems retained

- Hardware-aware tune generation.
- MOZA/AZOM recommendation and guarded live-write paths.
- Assetto Corsa active-car detection and installed-car scanning.
- Desired Behavior profiles.
- AC Car Setup Tuner.
- Telemetry Recorder and Tuning Assistant.
- Calibration and profiles.
- Atomic Remote.
- Share Codes Phase 1 (`AT1-...`).
- System Diagnostics and support tooling.
- Manual GitHub update checking/downloading.

## Important notes

- This remains a **beta**. Review generated wheelbase/AZOM values before applying them.
- The initial first-run Setup & Paths wizard remains modal by design. Normal tools remain embedded except Appearance.
- File/folder pickers, warnings, confirmations, and other true dialogs still open as normal Windows dialogs.
- Required SimHub bridge for live AZOM writes remains **v0.7.2**.
- Atomic Remote is intended for private same-LAN use; do not expose its HTTP port directly to the public Internet.

## Suggested tester focus

Please pay particular attention to:

- switching repeatedly between Telemetry, AZOM, AC Setup, Desired Behavior, Diagnostics, and the Dashboard;
- state preservation when navigating away from and back to an embedded tool;
- Appearance changes while another page remains active;
- newly installed AC cars and automatic pack grouping;
- active-car auto-selection after adding/removing cars;
- the full six-step guided tuning workflow.

Bug reports and feedback are welcome through the repository Issues tab.

### Active-car auto-selection fix
- Long mod-folder active-car auto-selection fix: ADT now safely resolves Assetto Corsa's 32-character shared-memory car ID to a unique full installed folder name. This fixes auto-select for cars such as `matsuri_mayhem_nissan_silvia_180sx_rps13`.

### Active-car detection hotfix

- Fixed active-car auto-selection for AC/CSP sessions where shared memory reports a numeric car slot (for example `0`) instead of the installed car folder ID. ADT now safely falls back to the current `Documents\Assetto Corsa\cfg\race.ini` `[CAR_0] MODEL` value before matching the installed car and pack.
