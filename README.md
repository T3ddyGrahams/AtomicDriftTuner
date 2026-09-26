# Atomic Drift Tuner

**Atomic Drift Tuner (ADT)** is an open-source Windows tuning assistant for **Assetto Corsa drifting**. Tell ADT how you want your car to behave, record a drive, review one supported setup change, then compare the next run using telemetry and your feedback.

ADT combines per-car Desired Behavior, supported car physics and setup definitions, recorded driving evidence, FFB recommendations and tune history. You can choose **Car tuning only** or **Car + FFB**. SimHub is optional for car tuning and telemetry recording.

## Download the current beta

| Component | Current version |
| --- | --- |
| ADT desktop | **0.9.0-preview.38** |
| Bundled ADT Companion | **0.5.0-preview.1** |
| Bundled ADT SimHub Bridge | **0.9.0-preview.38**; bridge logic unchanged in this update |

**[Windows installer](https://github.com/T3ddyGrahams/AtomicDriftTuner/releases/download/v0.9.0-preview.38/AtomicDriftTuner-0.9.0-preview.38-setup.exe)** · **[Portable ZIP](https://github.com/T3ddyGrahams/AtomicDriftTuner/releases/download/v0.9.0-preview.38/AtomicDriftTuner-0.9.0-preview.38-portable.zip)** · **[Release notes and all downloads](https://github.com/T3ddyGrahams/AtomicDriftTuner/releases/tag/v0.9.0-preview.38)**

Both desktop downloads include the companion and optional SimHub bridge. The release page also provides the separate companion ZIP and SHA256 checksums. Extract the portable ZIP fully before running `AtomicDriftTuner.exe`.

**Upgrading for track tools? Update ADT Companion to 0.5.0-preview.1 with the AC driving session closed, then start a fresh session.** Use **Remote → Install / Update Companion** or import the included companion ZIP through Content Manager. Older companion versions cannot supply the new position channel.

Windows x64 packages are self-contained: no Visual Studio or .NET SDK is needed. This is a public beta; installers are currently unsigned. See the [code signing policy](CODE_SIGNING_POLICY.md).

## HUGE UPDATE — what's new in preview.38

This release includes the work since public preview.33:

- **Quick driver review:** answer Better / Same / Worse / Couldn't judge for questions tied to your saved goals and the setup change you tested. Record new problems, save feedback and get a clearer next step.
- **Track & sections:** view your recorded route, mark a section, set line/angle/gear goals and compare complete passes. Track tools and feedback are now in the main app.
- **Exact setup-test verification:** check that the captured change matches the intended controls and values before judging the recommendation.
- **Diagnosis by driving condition:** examine initiation, transitions, sustained drift, normal cornering and FFB saturation by speed and direction, with available pedal context.
- **Verified setup limits:** use supported legal values and steps consistently through generation, export and pit staging.
- **Adjustable rev limits in gearing:** use supported limiter selections from the saved setup when estimating RPM and comparing final drives.

These are further **Telemetry Intelligence 2.0** milestones. ADT does not yet learn optimal numeric settings automatically from accumulated reviews. Read the [full release notes](docs/releases/v0.9.0-preview.38.md) and [track and feedback guide](docs/TRACK_TOOLS.md).

## Start here

### Choose the workflow you need

| What you want to use | What you need |
| --- | --- |
| Car tuning, saved setups and core telemetry | Windows 10/11 x64 and Assetto Corsa. SimHub/AZOM is not required. |
| In-game recording controls, setup capture, pit actions and track positions | ADT Companion and compatible Custom Shaders Patch (CSP). Recording/setup/pit controls use Remote pairing; position capture does not require pairing. |
| Live SimHub/AZOM FFB control | SimHub, AZOM and the ADT SimHub Bridge. |
| MOZA Pit House FFB | Manual entry, or the experimental SDK connection with compatible Pit House and user-supplied MOZA SDK files. See [Pit House setup](docs/PITHOUSE.md). |
| Logitech G27 FFB | The legacy Logitech Gaming Software / Profiler workflow. ADT stores a manual plan; it does not read or apply Logitech settings. |
| Phone, tablet or Raspberry Pi controls | A browser on the same private network and ADT Remote running on the PC. SimHub dashboard access is optional. |

### Your first tuning session

1. **Install and select your hardware/car.** Close ADT before updating. Open **Setup & Paths**, confirm the AC installation and Documents paths, and choose **Car tuning only** or **Car + FFB**. If including FFB, select the software you actually use.
2. **Describe your goal.** Select the correct car and set its **Desired Behavior**. Keep those goals fixed while comparing a baseline and test run.
3. **Prepare the actual baseline.** Save and load a named setup in AC. In Recorder, capture it through the companion or attach that exact saved setup, then confirm the settings actually in use. Enter your driver and driving conditions.
4. **Record, stop and save.** Follow the recording evidence banner. Useful time accumulates across shorter sections; missing entry/cornering/angle evidence remains visible. Ready to Review or Ready for Partial Review does not stop the recording automatically.
5. **Choose one test.** Open **Tuning Assistant → Your next step → Review one car setup change**. Review the current/proposed values, the reason and the possible tradeoff. If evidence or supported setup definitions are missing, ADT explains what is needed.
6. **Load or explicitly apply the test setup.** Save a separate setup and load it in AC, or stage it and use **ADT Companion → Pit setup → Save & Apply Tune** while stationary in editable pits. **Saving goals, generating recommendations and staging a tune do not change the car live.**
7. **Record a comparable repeat and review it.** Keep the driver, car, goals and conditions comparable. Open **Before / After**, choose the baseline, inspect the captured changes and answer **Quick driver review**. Click **Save quick review** to retain your answers.

For a track section, record fresh runs with position data and open **Tuning Assistant → Track & sections**. Use the [track walkthrough](docs/TRACK_TOOLS.md) to mark a reference, compare passes and save section feedback.

If using SimHub/AZOM, fully exit SimHub before **Install / Repair Packaged Bridge**, then restart SimHub and enable the ADT bridge. Other workflows can skip bridge installation. More help: [guided workflow](docs/GUIDED_WORKFLOW.md), [pit setup actions](docs/PIT_SETUP.md) and [beta testing guide](docs/BETA_TESTING.md).

## What ADT can do

### Desired Behavior and setup recommendations

Save per-car targets for front response, rear grip, self-steer response, transitions, angle stability, throttle rotation and initiation, plus an optional sustained body-angle goal. ADT blends overlapping goals and explains compromises.

The setup tuner starts with your saved `.ini`, shows readable **before → recommended → difference** values and reasons, and preserves a separate original baseline. Supported controls use verified ranges, steps and saved-value mappings. Unsupported or ambiguous controls remain unchanged with an explanation.

The focused test workflow isolates a supported adjustment and carries its exact intended values into the next recording. Before/after review checks actual captured changes, comparable evidence and the intended measurement. A file change alone, an extra adjustment or a result on an unrelated metric cannot validate that test.

### Telemetry Intelligence 2.0 and history

ADT records AC telemetry locally, including speed, RPM, gear, steering, body slip angle, yaw, throttle/brake/clutch, wheel-slip context and FFB output where available.

Saved-run review covers initiation, transitions, sustained angle, front-response and axle-slip proxies, steering response, oscillation, pedal use and FFB saturation. **Advanced telemetry → Phase Evidence → Where the pattern happens** adds speed/direction and available input context. Conflicting conditions can lead to a request for another comparable run instead of a whole-car setup change.

**Before / After** shows both run measurements and captured setup differences. **Tune & Run History** retains snapshots and earlier reviews. Quick driver review combines goal-specific answers with the available evidence to suggest keeping and verifying, repeating, reviewing a tradeoff, considering the baseline or reviewing another supported test. An answer does not automatically apply or restore settings.

Wheel slip and steering response are proxies, not direct tyre-force or hands-off self-steer measurements. Incomplete evidence remains inconclusive. See [telemetry intelligence](docs/TELEMETRY_INTELLIGENCE.md).

### Track tools

The normal companion supplies optional read-only world positions alongside native telemetry. **Track & sections** lets you:

- view the route actually recorded;
- mark a continuous section and save a reference;
- set an optional body-angle band, preferred forward gear and line offset;
- compare complete passes using paths, line/angle/gear measurements, speed and pedal context;
- save feedback tied to the exact section revision and selected passes.

Optional AI spline data is map context, not a recommended drift line. An offset is a driver-selected target; ADT does not infer the outside of a corner or usable road width. Body angle and line width are different measurements. Same-run passes describe consistency, and section comparisons do not establish a tuning cause.

Core recording remains available without position data. Old recordings without positions cannot produce a route map. See [track tools and driver feedback](docs/TRACK_TOOLS.md).

### Gearing and ECU review

Choose the gear and usual speed range you want for **tight corners** and **long sweepers**, with a saved mph/km/h preference. If you do not know the speeds, choose a suitable recording to fill typical speeds when enough evidence exists in the requested gears.

ADT reads supported gear ratios, final-drive presets, gearbox selections, base engine curves and saved adjustable limiter values. The planner compares available final drives against both goals and explains compromises. A saved gearing setup changes only the final-drive selection; it does not optimize individual gears or gearbox presets.

**Gearing & ECU** review shows recorded RPM/speed exposure and supported saved-setting meanings. ECU labels and configured curve values are file context, not measured horsepower or verified live script behavior. See [gearing](docs/GEARING.md) and [car data](docs/CAR_PHYSICS.md).

### Installed cars and packed physics

ADT can scan installed cars, match the active AC car and suggest a drift-pack profile. Manual selection remains available. Built-in pack profiles are starting baselines, not exact mod physics.

With car-data guidance enabled, ADT can read supported unpacked files and ordinary packed **`data.acd`** archives. Packed content is decoded into a private memory cache; you do not need to unpack supported archives through Content Manager first, and installed car files are not changed.

Readable definitions provide context for suspension, tyres, drivetrain, brakes, gearing and supported ECU selections. Conflicting packed/unpacked sources, protected/corrupt archives and unknown mappings remain explained limitations. Base definitions do not prove what is currently loaded in game. See [car physics, values and sources](docs/CAR_PHYSICS.md).

### FFB and wheelbase workflows

ADT supports **SimHub/AZOM**, **MOZA Pit House**, **Logitech G27 legacy Profiler** and manual FFB workflows.

- **SimHub/AZOM:** review generated targets against supported live values, then explicitly apply selected changes. The separate bridge uses AZOM's commit/readback path, with write serialization, duplicate protection, verification and a pre-apply snapshot for Revert. Normal ADT sliders do not continuously write to the wheelbase. [Integration details](docs/AZOM_LIVE_INTEGRATION.md).
- **Pit House:** manual entry is available. The optional experimental SDK connection provides guarded read/apply operations in a separate helper process to contain native failures. Actual SDK/firmware/wheelbase compatibility still needs hardware testing. Vendor DLLs are not bundled. [Pit House instructions](docs/PITHOUSE.md).
- **G27:** enter the five numeric controls and three switches from legacy Logitech software, save the plan and compare it across runs. Enter and verify settings manually in Logitech and AC. G27 calibration adjusts AC gain only; ADT does not model its physical torque or automatically tune its spring/damper/rotation controls. [G27 walkthrough](docs/GUIDED_WORKFLOW.md#logitech-g27-and-legacy-profiler--local-preview33).

Built-in wheelbase profiles include MOZA R3, R5, R9, R12 / R12 V2, R16, R21 and R25 Ultra, a custom direct-drive base, and Logitech G27. Rim profiles include the existing MOZA selection, the G27 integrated rim and custom/aftermarket wheels. Direct-drive recommendations use the selected hardware characteristics; the G27 follows its separate manual workflow.

Calibration stays associated with the matching wheelbase, wheel, drift pack and car. Built-in pack baselines include VDC, Gravy Garage, Team SWARM, ADL, WDT/WDTS, Deathwish Garage and Custom / Other. A listed profile is not a claim that every hardware/mod version has been validated.

### In-game companion, phone and touchscreen

**ADT Companion** exposes recording controls, connection status, findings, your next step, setup capture, comparison and explicit pit actions inside AC/CSP. Preview.38 also uses it for track position capture.

**ADT Remote** and the adaptive **`/dash`** view provide browser controls on a phone, tablet, Raspberry Pi or PC. The touchscreen layout adapts to screen size and orientation, supports fullscreen and can be opened through the optional SimHub Control Center entry. The companion and dashboard use the same desktop recording state.

Start Remote on the gaming PC, use the address shown for the other device and pair on the same private network. Default port: **5190**. On a Pi or phone, `127.0.0.1` refers to that device; use the gaming PC's LAN address instead. Keep ADT running. Remote AZOM writes require a separate desktop opt-in and are off by default each time the server starts. Keep Remote on the private LAN; do not expose its HTTP port publicly.

See [touchscreen setup](docs/TOUCHSCREEN.md) and [Remote pairing and tests](docs/REMOTE_IPHONE_TEST.md).

### Appearance and diagnostics

Resize and scroll ADT windows for narrow, portrait and ultrawide displays. Appearance settings customize backgrounds, surfaces, text, accents, inputs, tables, tabs and control colors, with live preview and contrast checks. Theme changes do not alter tuning values.

**System Diagnostics** checks versions, paths and available connections. It can create a local redacted support ZIP; telemetry recordings, tune profiles, AC setup files and per-car goal contents are excluded by default. Support exports are not uploaded automatically.

## Beta testing and known limits

Use the **[preview.38 testing checklist and compatibility tracker](docs/testing/v0.9.0-preview.38-checklist.md)**. The release passed **443 regression groups, 259 WPF UI assertions and 565 companion Lua assertions**. Automated results do not establish live driving quality or compatibility with every car, CSP version or wheelbase.

- ADT uses evidence-based heuristics and supported file mappings; it is not a full vehicle simulation or an optimal-tune solver.
- Unsupported setup controls, arbitrary CSP/ECU scripts and protected or malformed car data can limit recommendations.
- Saved setup files and generated FFB plans are not automatically proof of the active in-game or hardware settings.
- Track capture needs fresh valid positions from a compatible companion/CSP session. Gaps, mismatched identities and ambiguous routes can prevent pass comparisons.
- G27 settings are manual; Pit House SDK hardware support remains experimental; AZOM changes can affect live integration compatibility.
- Accumulated learning of optimal per-car/per-driver settings remains future work. See the [roadmap](ROADMAP.md) and [Intelligence 2.0 completion plan](docs/TELEMETRY_INTELLIGENCE.md).

Report reproducible problems or successful tests through [GitHub Issues](https://github.com/T3ddyGrahams/AtomicDriftTuner/issues) or the [ADT Discord](https://discord.gg/XphUD738t). Include ADT/companion/CSP versions, wheel software, car and track/layout, expected behavior, actual behavior and the steps to reproduce. Share logs or recordings only when you choose to, with private paths/tokens removed. More guidance: [beta testing](docs/BETA_TESTING.md).

## Building from source

Use Windows with the .NET 8 SDK, or Visual Studio 2022 with **Desktop development with .NET**. The WPF application is in `AtomicDriftTuner.sln`; the optional SimHub bridge is separate.

```powershell
dotnet build AtomicDriftTuner.sln -c Release
dotnet run --project .\src\AtomicDriftTuner\AtomicDriftTuner.csproj -c Release
```

Build the bridge against your own SimHub installation. Replace `C:\SimHub` with its actual location:

```powershell
.\bridge\build-bridge.ps1 -SimHubPath "C:\SimHub" -Version "0.9.0-preview.38"
```

Fully exit SimHub before using `bridge/install-bridge.ps1`, then restart it. See [bridge integration](docs/AZOM_LIVE_INTEGRATION.md). Third-party proprietary binaries are not part of the repository or release payload.

To build the portable package and an installer when Inno Setup 6 is available, supply an explicit version:

```powershell
.\distribution\build-beta-package.ps1 -SimHubPath "C:\SimHub" -Version "0.9.0-preview.38"
.\distribution\build-companion.ps1
```

Outputs go to `artifacts/release/`. Use a new version for a new published build; do not replace an existing release's files. Normal users can use the packaged downloads without compiling a bridge or obtaining developer tools.

For test commands and fixtures, see [tests/README.md](tests/README.md). Contributions to telemetry analysis, supported setup mappings, hardware validation, accessibility and documentation are welcome.

## Local data and privacy

Installed and portable main builds use the current Windows user's **`%LOCALAPPDATA%\AtomicDriftTuner`** folder. Examples include:

```text
settings.json
calibrations.json
car-behavior-targets.json
TelemetrySessions/
RunHistory/
TrackSections/
Logs/
```

Updating ADT preserves existing main-app data. Separate developer-build data is not automatically migrated over it. Release packages contain no developer settings, recordings or credentials. Recordings, feedback, settings and support exports stay local unless you choose to share them. See the [privacy policy](PRIVACY.md).

## Project information

- [Changelog](CHANGELOG.md) · [Roadmap](ROADMAP.md) · [Architecture](docs/ARCHITECTURE.md)
- [Guided workflow](docs/GUIDED_WORKFLOW.md) · [Track and feedback guide](docs/TRACK_TOOLS.md) · [Release testing checklist](docs/testing/v0.9.0-preview.38-checklist.md)
- [Code signing policy](CODE_SIGNING_POLICY.md) — SignPath signing is not enabled; current release installers are unsigned.
- [MIT License](LICENSE)

Atomic Drift Tuner is an independent community project. It is not affiliated with, sponsored by or endorsed by SimHub, AZOM, MOZA Racing, Logitech, Kunos Simulazioni, Assetto Corsa or the creators of referenced drift packs. Product and mod names identify compatibility. Proprietary third-party binaries must not be committed or redistributed without permission from their rights holders.

Review proposed wheelbase settings before applying them and start conservatively. Keep the rig's emergency-stop or power control accessible where applicable. Readback confirms a reported setting, not that a force level is appropriate for every driver or rig.
