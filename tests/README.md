# ADT regression checks

## Setup comparisons (local preview.32)

`dotnet run --project tests/AtomicDriftTuner.RegressionTests -c Release -- --setup-comparison` checks read-only projection of actual tuner output, all unchanged/missing settings, verified versus stale/partial mappings, camber units, FFB target provenance, changed ratio definitions and culture handling. Included in the full regression suite.

`dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/setup-comparison-wpf --setup-comparison` exercises proposed and recorded comparisons, filtering, stale selection clearing, history navigation, wrapped explanations, custom colours and narrow/portrait/wide layouts. Includes existing focused setup, goal and car-physics UI checks. Test fixtures are isolated; no live tuning or hardware commands are issued.

## Focused saved-run setup tests (local preview.31)

`MatchingCarDataChecks` also verifies the camera-only dual-source exception: only finite driver-eye/pitch values in a valid, otherwise identical `car.ini` can differ. Mass, inertia, steering, FFB, fuel, other graphics fields, unrelated files, duplicate/malformed sections, misplaced keys, nonfinite values and stale fingerprints remain guarded. A read-only RX-7 probe reproduced the real two-field camera difference and verified gearing load/calculation and a workspace-only final-drive export without changing the car or user's baseline.

`dotnet run --project tests/AtomicDriftTuner.RegressionTests -c Release -- --car-test` covers matching recorded baselines, source/identity/confirmation gates, one-setting-family isolation, preserved original files and unrelated values, exact history descriptions, direct-mode/camber mappings, paired controls, no-op limits, stale save/stage rejection, unchanged input/report/goal state, and next-step priority. These checks are also included in the full regression suite.

`dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/car-test-wpf --car-test` exercises the default card and primary action, real review/save/stage handlers, partial-success tracking errors, locked run selection, FFB-only visibility, narrow/portrait/wide scrolling and action reachability. It includes the existing angle/summary checks. All fixture writes are isolated; no live pit or hardware commands are sent. A fresh in-game before/after run remains a manual acceptance check.

## Gearing and ECU review (local preview.27)

`PowertrainChecks` covers saved/fixed/individual/gearset decoding, missing and ambiguous selections, forward-gear numbering, backward/invalid/frozen/excluded frames, 15–100 Hz and uneven time weighting, saved target gear/speed windows, gaps/shifts/clutch/brake episode boundaries, confirmed context, base-versus-adjustable limiter evidence, bounded ECU lookup, unknown selections, descriptive comparison, packed/unpacked immutable snapshots, invalid metadata, legacy recordings and unchanged handling/FFB analysis. Gearing checks cover real `ENGINE_LIMITER` in both definition-only and saved-only cases and stale export after its introduction. The suite passes 311 regression groups, including the existing 8,000-combination tuning sweep.

`dotnet run --project tests/AtomicDriftTuner.LayoutTests -- . artifacts/powertrain-wpf --powertrain` checks the always-visible review tab, collapsed details, full selected-event text, baseline navigation, custom colors, narrow/portrait/wide reachability and clearing stale results. It also runs the existing angle/next-step UI checks. Live readiness skips the extra powertrain work; the detailed review never writes a car tune or attributes an improvement.

Read-only replay of five recent S14/350Z runs matched all pre-existing analysis fields from the preview.26 release assembly exactly. Old runs acquired no target or ECU curves. Separate current-file inspection resolved saved gearing without rewriting history or installed physics. A 162,430-sample stress replay completed in roughly 0.5 seconds on the development PC; timing is not a hardware guarantee. Synthetic and saved-run checks do not replace a fresh in-game before/after test.

## Recording progress and partial review (local preview.26)

`RecordingEvidenceChecks` covers short-section accumulation, separate front-response and axle-slip counters, normal-cornering eligibility, the 60-second usable-drift threshold, unchanged measured evidence and goal snapshots, missing angle recovery, interrupted/poor-quality/lost-setup guards and once-only notifications through partial-to-full readiness. WPF `--evidence`, companion UI fixtures and the touchscreen browser suite exercise the explicit partial-review heading and visible limitations with the existing recording controls. These tests do not establish driving quality or resolve the separate tuning-engine audit findings.

## Partial physics import (local preview.25)

`PartialPhysicsChecks` covers malformed auxiliary text with packed/unpacked/matching sources, whole-section exclusion for duplicate fields/sections, malformed-header boundaries, unassigned values, unchanged valid differential scaling, explicit unsupported differential values and resource limits. The WPF car-physics fixture verifies recovered suspension context and uncertainty messages, including first/last-line reachability of the expanded details at small, portrait and wide sizes. Real-car inspection uses read-only hashes and does not save or apply settings.

## Matching car-data copies (local preview.24)

`MatchingCarDataChecks` verifies exact supported file-set/byte equality, independent source fingerprints, unchanged single-source fingerprint formats, differing/missing/extra files, cache revalidation, changes to either or both copies, source removal and resource limits. `PackedGearingChecks` loads, calculates and exports a matching-copy fixture, verifies that only the output final-drive selection changes, and refuses stale export after either source changes. All writes use isolated fixture cars; actual installed-car validation is read-only.

## Pit setup application (local preview.15)

The regression runner passes 165 scenarios, including strict immutable setup plans, stale/range/step validation, at-most-once admission, matching and idempotent completion, restore authorization, unknown-result recovery, and real paired HTTP checks. Existing tuning calculations and the 8,000-combination range sweep remain covered. The Lua 5.1 suites pass 388 assertions, including 196 simulated pit-operation and 32 pit-transport assertions; no game APIs or user setup files are changed by these tests.

The full WPF suite passes 455 geometry assertions and the existing theme/workflow/recorder checks. Its new pit workflow checks pass 29 assertions after the final manual-attachment test: production generation/staging, stale proposals, immutable plans, goals-only visibility, recording blocks and confirmation invalidation. Run only these with `dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/pit-layout --pit-setup`. The touchscreen suite passes 151 browser assertions.

Run the two new companion tests with Lua 5.1/LuaJIT:

```text
lua tests/companion/pit-setup-tests.lua companion/apps/lua/ADTCompanion/pit_setup.lua
lua tests/companion/pit-client-tests.lua companion/apps/lua/ADTCompanion/companion_client.lua
```

These are simulated game tests. Follow [pit setup acceptance](../docs/PIT_SETUP.md) for actual CSP/car validation, including save/load/restore, editable-menu behavior and failure recovery.

## Clean startup and two-choice guidance (local preview.12)

The regression runner passes 118 scenarios. The WPF runner passes 48 startup-selection assertions using actual dashboard controls with isolated settings: fresh and legacy startup, explicit car choice, saved/custom/partial values, missing profiles, installed-car identity checks, preserved unrelated preferences and cleared stale remote context. It also passes 34 guided-mode assertions, covering the required Car tuning only / Car + FFB choice, simple help, adaptive integration questions, preserved answers and existing recording/review behavior.

Existing coverage also passes: 411 geometry, 167 theme, 16 companion recorder, 50 recorder recovery, 14 gearing and 17 angle-goal UI assertions. Run the commands below with `artifacts/startup-layout` for this build's PNGs. Startup tests suppress machine startup and use isolated storage; they do not replace a real install or driving check.

## Simple next step and sustained angle goals (local preview.11)

The full regression runner now passes 116 scenarios. Fifteen new scenarios cover the saved per-car angle goal, independent stability preferences, custom range validation, sharing and immutable history, native travel direction, complete held-angle attempts and speed/recovery, 15–100 Hz timing, clipped/gapped/frozen/excluded/invalid recordings, backward slides, legacy unknown recovery, unchanged FFB/setup/gearing generation values, remote save preservation, comparison tradeoffs and goal-aware next steps. Repeated pedal advice requires meaningful repeated evidence; raw observations stay available.

The WPF runner passes 411 geometry assertions and 17 new production-handler checks for saving/reloading angle goals, preserving them when a handling preset changes, optional range controls, simple/advanced visibility and next-step actions. Existing theme, recorder, gearing and guided-workflow checks remain included. PNGs are synthetic previews; driving validation is still needed. Use the commands below, with `artifacts/angle-layout` as an optional output folder.

## Adaptive touchscreen (local preview.9)

The touchscreen suite covers a cross-origin SimHub launcher leaving its scaled fixed canvas, actual fullscreen entry/exit and a rejected request, responsive layouts across nine landscape/portrait/ultrawide sizes, reachable recording actions and long confirmations on short displays, and resizing while recording without replaying a command. HTTP checks retain framing protection for authenticated controls and allow only the public launcher to be embedded. Installer checks cover generated LAN addresses, port changes, destination validation, backups, and preference for routed physical interfaces.

Use the render and browser commands below, or choose `artifacts/adaptive-browser` as the output directory. The desktop layout runner also checks the separate PC and Pi/tablet address controls.

## Recorder recovery (local preview.8)

The WPF runner exercises actual recorder start/stop/save handlers with an anonymous telemetry map and isolated history/calibration stores. It records a baseline, applies its calculated recommendation to the fixture calibration, then starts a linked comparison run. Checks cover brief frozen-frame recovery without duplicate samples, unchanged time gaps, prolonged loss, late recovery, manual stopping during an outage, successive starts, car/track changes, packet/time resets and saved stop reasons. The original brief-freeze case failed against preview.7 before the fix. No named AC maps or hardware writes are used.

Run just these checks with `dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/recording-recovery --recorder-recovery`. They also run in the full WPF suite. Live driving is still required to confirm the cause of the user-reported second-run stop.

Preview.23 adds recording-buffer checks: an anonymous shared-memory producer and the real background sampler run while the WPF thread stalls for 350 ms four times. The recorder must retain the acquired frames without quarter-second gaps. Deterministic cases cover batched catch-up, pending frames on stop/save, source timestamps, real gaps, timely recovery before a delayed UI tick, resets hidden inside a batch, stop-time context changes, bounded overflow and new-run isolation. `TelemetryBufferChecks` also verifies duplicate polls, independent snapshot/recording copies and buffer detachment. No real game mapping, wheelbase or user-history writes are used.

The guided-workflow preview brings the suite to **50 scenarios** and layout coverage to **252 geometry assertions**. New checks cover workflow ordering, generated-versus-ready state, changed goals/settings, optional integration guidance, installed/offline distinctions, driver/car/intent isolation, reopening, reset/corruption preservation and incomplete recordings. See [guided test steps](../docs/GUIDED_WORKFLOW.md). Historical counts below describe the earlier intelligence milestone.

Responsive layout checks (Windows, run from repository root):

```powershell
dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/layout-checks
```

This suite loads production XAML and styles without business event handlers, saved-user-data access, or hardware services. It measures action bounds at narrow, portrait, short, and ultrawide dimensions; exercises tab visibility and card reflow; and writes PNG previews under the chosen output directory. A passing result complements real monitor/DPI and keyboard interaction testing.

Run from the repository root on Windows with the .NET SDK installed:

```powershell
dotnet run --project tests/AtomicDriftTuner.RegressionTests -c Release
```

This dependency-free console suite exits nonzero on failure. It uses generated files in a unique temporary directory and anonymous simulated shared-memory maps. It does not connect to or write to a wheelbase, start ADT's UI, or edit the user's saved settings/calibrations. Reflection isolates existing storage implementations without changing their production constructors. Temporary fixture locations are printed for inspection.

Coverage: telemetry recovery/freshness, session preservation, behavior storage and identity, setup baseline consistency, saved-tune and portable-share round trips, calibration backup recovery, and the AZOM stale-source guard. An output-range sweep covers all 8,000 built-in hardware/wheel/car/intent combinations; it is not a driving-quality test.

The intelligence preview adds 22 scenarios (40 total): complete initiations/transitions, sample-rate invariance, phase-aware oscillation, missing axle evidence, invalid/frozen frames, excluded driving states, impacts/restarts, low-confidence gating, driver/car/track/goal mismatch, direction-aware comparison, control tradeoffs, recorded-goal preservation, conflicting driver feedback, setup-file fingerprints, append-only tune/review storage, corrupted entries and legacy context. These are synthetic fixtures, not evidence of real-car tuning improvement.

Layout checks now include scrolling the new recorder and run-history fields into view, including Save Run Review, for 201 geometry assertions. The harness also writes RunHistory PNGs. It does not execute production event handlers or verify physical monitor/DPI transitions.

Other checks:

```powershell
dotnet build src/AtomicDriftTuner/AtomicDriftTuner.csproj -c Release
dotnet build bridge/AtomicDriftTuner.SimHubBridge/AtomicDriftTuner.SimHubBridge.csproj -c Release -p:SimHubInstallPath=E:\SimHub
node share-api/selftest.js
```

Use the actual SimHub installation path on other machines. The share API self-test launches an isolated localhost service and uses temporary registry data. If a constrained Windows environment prevents Node from resolving ancestor directories, set `NODE_OPTIONS=--preserve-symlinks --preserve-symlinks-main` for that test process.

## In-game companion

The regression runner now includes companion command validation and a real loopback HTTP pairing/command test (52 scenarios total). The WPF layout runner additionally exercises the real recorder's stop/save operations with isolated output: 16 companion assertions, alongside 167 theme and 252 geometry assertions. Recording-start rejection is tested offline; actual AC sampling and CSP rendering still require hands-on testing.

With a Lua 5.1 interpreter (or LuaJIT), run from the repository root:

```text
lua tests/companion/client-tests.lua companion/apps/lua/ADTCompanion/companion_client.lua
lua tests/companion/ui-tests.lua companion/apps/lua/ADTCompanion/companion_client.lua companion/apps/lua/ADTCompanion/ADTCompanion.lua
```

The 18 client assertions cover pairing, stale/disconnected state, protocol mismatch, double clicks, timeouts, ignored late responses and no automatic mutation retries. Six UI assertions execute the actual entry point with mocked CSP drawing/network functions. This is not an in-game rendering test. No Lua test dependency is included in the application/mod packages.

## Guided modes (local preview.6)

The regression runner now has 79 scenarios, including 11 new mode/persistence/intelligence-preservation checks. The WPF runner adds 21 guided-mode UI assertions (real interview, recorder and assistant handlers with isolated stores), alongside 167 theme, 14 gearing, 16 recorder and 330 geometry assertions. It renders manual/car-only interviews and recording guidance. New checks cover legacy defaults, mode-specific progress, late run callbacks, unchanged telemetry/tuning outputs, full recommendation retention, selected-scope snapshots, cross-mode comparison limitations and saved review decisions. No live setting writes or game driving are performed.

## Touchscreen and restored workflow (local preview.7)

The regression runner includes 83 scenarios plus a private-network transport check when a private interface is available. Tests cover the restored combined workflow without rewriting prior scoped history, `/dash` routing, paired recording, stale/duplicate/revoked commands, the shared in-game/touchscreen command gate, and SimHub dashboard installation/backups. WPF geometry coverage includes reaching the touchscreen setup controls by scrolling.

Render the compiled web page, then run the isolated browser tests with Playwright and Microsoft Edge installed:

```text
dotnet run --project tests/AtomicDriftTuner.RegressionTests -- --render-remote artifacts/touch-browser
node tests/remote/touch-browser-tests.cjs artifacts/touch-browser
```

The browser suite uses fake HTTP responses and never changes game or wheelbase settings. It covers touch pairing, live telemetry display, recording, reconnects, edit retention, in-page confirmation, remote-write opt-out, pairing revocation and layouts from 320×568 through 1280×720. SimHub native rendering and real touch hardware still require hands-on validation. Build test projects sequentially because their shared WPF project generates files under the same `obj` directory; already-built test executables can run independently.

## Camber staging (local preview.28)

`CamberSetupChecks` covers packed/unpacked mode 0/1/2 round trips, equivalent adjustment direction across encodings, both end stops, fractional/out-of-range values, malformed/custom mappings, explicit local overrides, all four wheels, and stale source rejection. File export and pit staging must agree without rewriting the baseline. The pit WPF fixture also generates and stages a -55 to -56 camber adjustment through the production handlers. Its setup limits match the reported GT86 case.

`tests/companion/pit-setup-tests.lua` includes saved-tenths apply/restore and independent live-range refusals. It uses mocked game APIs under Lua 5.1/LuaJIT; it does not prove live game application. No production companion code changed.

## Final-drive gearing

The regression runner includes 16 gearing scenarios (68 total). `GearingChecks` covers actual ratio/index mapping, selected gearsets/gears/tyres, malformed and unavailable data, wrong-car baselines, limiter exclusion, no-op/partial fits, per-car goals and preservation of all unrelated setup settings. Stale-source checks include the entire baseline and each data file.

The WPF runner has 14 additional gearing workflow/theme assertions and 307 total geometry assertions. It exercises production calculation/invalidation/unit/persistence handlers against isolated fixtures and renders gearing screenshots. It does not drive Assetto Corsa or interact with user setup/settings folders.

Optional installed-car verification: set `ADT_GEARING_TEST_CAR` to an installed car folder and `ADT_GEARING_TEST_SETUP` to an existing saved setup before running the regression project. This adds one scenario that reads the real data and writes any test export only under the runner's temporary fixture directory. Leave those variables unset for a fully synthetic run. See [Gearing](../docs/GEARING.md) for supported formats and driving-test steps.

## Pedal evidence (local preview.10)

The regression runner has 101 scenarios, including 14 in `PedalChecks`. Coverage includes sustained pedal events, response windows, initiation/transition association, sample-rate equivalence from 15–100 Hz, spikes, gaps/exclusions, incomplete clutch cycles, shifts, raw polarity, missing signals, matching means with different input distributions or cycle rates, saved Desired Behavior guidance, and reanalysis without raw-file changes. Pedal context never becomes an improvement score or writes tuning settings.

The WPF suite checks production report binding and clearing, selected-event detail binding, theme inheritance, minimum column widths, and scroll access to the event/detail/context sections. It passes 381 geometry assertions plus 167 theme, 16 companion-recorder, 50 recovery, 14 gearing and 25 guided-workflow assertions. Pedal screenshots are explicitly synthetic fixtures. Actual driving is still required to evaluate the usefulness of the provisional event/comparison thresholds.

## Definition isolation (local preview.30)

`SetupDefinitionIsolationChecks` covers conflicting, identical and case-varied duplicate car definitions, duplicate fields, inherited versus explicit display modes, malformed boundaries, legal raw anti-roll-bar steps, generation/staging/export parity, unrelated gearing conflicts, required gearing conflicts, strict saved-baseline duplicates and stale sources. Fixtures cover packed and unpacked cars. The full runner passes 341 regression groups. Focused `--pit-setup` and `--gearing` WPF runs exercise the actual handlers with conflicting brake-bias definitions and visible explanations (31 and 27 assertions respectively). These tests do not apply anything in the game.

## Corner gearing (local preview.29)

`CornerGearingChecks` adds packed/unpacked joint-goal ranking and final-drive-only export, per-goal limiter rejection, compromise/no-op results, stale second-gear evidence, fixed/individual/preset gearbox detection, RPM curve estimation/refusals/source changes, old/new target persistence, unit defaults, two-goal history/exposure, changed-target comparisons and recorded-speed helper gates. Estimates are hypotheses for a test; tests do not establish better driving behavior.

The focused WPF run is `dotnet run --project tests/AtomicDriftTuner.LayoutTests -- . artifacts/corner-gearing-wpf --gearing`. It checks the production window's single/two-goal workflow, preset explanations, conversion/persistence, automatic/manual RPM provenance, invalidation, theme colors and narrow/portrait/wide layouts using isolated stores.
## Telemetry Intelligence 2.0 verified tests

`dotnet run --project tests/AtomicDriftTuner.RegressionTests -c Release -- --intelligence-v2` covers wrong/missing/extra setting changes, exact planned values, metadata-only edits, manual/CSP coverage, baseline/goal identity, driver-defined experiments, intended-metric credit, immutable persistence, analysis versions, and brief/sustained backward travel. The original four audit failures were demonstrated before the correction. These checks also run in the full suite.

`dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/intelligence-v2-wpf --intelligence-v2` exercises real recorder context capture, edited descriptions, custom-test provenance and narrow layouts, plus focused setup save/stage, comparison, goal, recording-recovery and companion workflows. All histories and file changes use isolated fixtures; no live game or hardware changes occur.
