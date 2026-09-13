# ADT regression checks

## Adaptive touchscreen (local preview.9)

The touchscreen suite covers a cross-origin SimHub launcher leaving its scaled fixed canvas, actual fullscreen entry/exit and a rejected request, responsive layouts across nine landscape/portrait/ultrawide sizes, reachable recording actions and long confirmations on short displays, and resizing while recording without replaying a command. HTTP checks retain framing protection for authenticated controls and allow only the public launcher to be embedded. Installer checks cover generated LAN addresses, port changes, destination validation, backups, and preference for routed physical interfaces.

Use the render and browser commands below, or choose `artifacts/adaptive-browser` as the output directory. The desktop layout runner also checks the separate PC and Pi/tablet address controls.

## Recorder recovery (local preview.8)

The WPF runner exercises actual recorder start/stop/save handlers with an anonymous telemetry map and isolated history/calibration stores. It records a baseline, applies its calculated recommendation to the fixture calibration, then starts a linked comparison run. Checks cover brief frozen-frame recovery without duplicate samples, unchanged time gaps, prolonged loss, late recovery, manual stopping during an outage, successive starts, car/track changes, packet/time resets and saved stop reasons. The original brief-freeze case failed against preview.7 before the fix. No named AC maps or hardware writes are used.

Run just these checks with `dotnet run --project tests/AtomicDriftTuner.LayoutTests -c Release -- . artifacts/recording-recovery --recorder-recovery`. They also run in the full WPF suite. Live driving is still required to confirm the cause of the user-reported second-run stop.

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

## Final-drive gearing

The regression runner includes 16 gearing scenarios (68 total). `GearingChecks` covers actual ratio/index mapping, selected gearsets/gears/tyres, malformed and unavailable data, wrong-car baselines, limiter exclusion, no-op/partial fits, per-car goals and preservation of all unrelated setup settings. Stale-source checks include the entire baseline and each data file.

The WPF runner has 14 additional gearing workflow/theme assertions and 307 total geometry assertions. It exercises production calculation/invalidation/unit/persistence handlers against isolated fixtures and renders gearing screenshots. It does not drive Assetto Corsa or interact with user setup/settings folders.

Optional installed-car verification: set `ADT_GEARING_TEST_CAR` to an installed car folder and `ADT_GEARING_TEST_SETUP` to an existing saved setup before running the regression project. This adds one scenario that reads the real data and writes any test export only under the runner's temporary fixture directory. Leave those variables unset for a fully synthetic run. See [Gearing](../docs/GEARING.md) for supported formats and driving-test steps.
