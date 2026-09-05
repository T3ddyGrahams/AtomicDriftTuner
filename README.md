# Atomic Drift Tuner

**Atomic Drift Tuner** is an open-source Windows tuning assistant for **Assetto Corsa drifting**, built around hardware-aware wheelbase tuning, **MOZA + AZOM/SimHub live settings**, per-car setup recommendations, telemetry analysis, and driver-defined behavior targets.

> **Current public beta:** `v0.8.1-beta.1`  
> **Required Atomic SimHub Bridge for live AZOM writes:** `v0.7.2`  
> **Status:** Public beta / active development


Atomic is designed to answer a practical drifting question:

> **Given this wheelbase, this steering wheel, this car, this drift pack, and the way I want the car to behave — what should I change?**

It is not just a preset list. Atomic combines hardware characteristics, car/pack data, driver intent, saved calibration, AC setup information, and recorded telemetry to generate and refine recommendations.

---

## Atomic Remote — iPhone / browser companion

`v0.8.0-beta.1` includes **Atomic Remote**, a local-network companion UI served
directly by the Windows Atomic application. An iPhone or other modern browser on
the same private LAN can pair with Atomic and use a mobile dashboard without a
separate App Store install.

Current remote capabilities include:

- live Assetto Corsa speed, slip angle, steering angle, FFB output and drift detection;
- automatic display of the active AC car and inferred drift pack;
- current wheelbase, wheel, car, pack and Drift Target context;
- change the Windows **Drift Target / session intent**;
- request **Generate Tune** on the authoritative Windows app;
- review generated AZOM/MOZA and Assetto Corsa FFB recommendations;
- view self-steer, stability and detail scores plus estimated peak wheel torque;
- edit and save per-car **Desired Behavior** targets and presets;
- read selected live AZOM values;
- optionally request a limited allow-list of numeric AZOM changes;
- revert the last remote AZOM change from the current Atomic run.

The phone never talks directly to SimHub, AZOM, MOZA software or the wheelbase.
Windows Atomic remains authoritative.

Remote AZOM writes are **OFF by default every time the remote server starts**.
They require a separate Windows-side opt-in and still pass through Atomic's
existing range validation, single-flight write gate, duplicate/rate protection,
exact AZOM commit path and live readback verification.

Atomic Remote is currently intended for **same-LAN/private-network use only**.
Do not port-forward its HTTP port or expose it directly to the public Internet.

See [`docs/REMOTE_IPHONE_TEST.md`](docs/REMOTE_IPHONE_TEST.md) for the architecture,
pairing/security model and testing notes. The required Atomic SimHub Bridge
remains **v0.7.2**.

## What Atomic can do

### Hardware-aware drift tuning

Atomic generates a starting tune from:

- wheelbase torque capability,
- steering-wheel diameter and inertia,
- drift pack,
- specific car,
- driver intent,
- saved per-combination calibration.

The tuning engine currently generates recommendations for:

- AZOM/MOZA Base settings,
- Assetto Corsa FFB,
- self-steer / stability / detail balance,
- wheel-speed and damping behavior,
- wheel/rim inertia compensation.

Calibration is keyed to the exact:

`wheelbase + steering wheel + drift pack + car`

so feedback from one setup does not silently affect another.

---

### Supported built-in wheelbase profiles

Current built-in profiles include:

- MOZA R3
- MOZA R5
- MOZA R9
- MOZA R12 / R12 V2
- MOZA R16
- MOZA R21
- MOZA R25 Ultra
- Custom direct-drive base

Custom hardware values can be edited where appropriate.

### Supported built-in steering-wheel profiles

- MOZA CS Pro
- MOZA KS Pro
- MOZA CS V2P
- MOZA RS V2
- MOZA KS
- MOZA ES / ES Lite / ESX
- MOZA Vision GS
- MOZA GS V2P GT
- MOZA TSW
- Custom / aftermarket wheel

Wheel diameter and estimated inertia are part of the generated tune.

---

## Drift pack support

Atomic currently includes built-in tuning baselines for:

- VDC Public 5.0
- Gravy Garage V2
- Team SWARM V3.2
- ADL Elite / Pro-Am
- WDT / WDTS
- Deathwish Garage
- Custom / Other

Pack values are **tuning baselines**, not claims of exact mod physics. When Atomic can read useful data from the installed car, that data takes priority over a generic template.

---

## Assetto Corsa car scanner

Atomic can scan an Assetto Corsa installation and build car profiles from installed content.

Where available, it reads:

- `ui/ui_car.json`
- unpacked `data/car.ini`
- unpacked `data/tyres.ini`

Atomic tracks confidence for values such as:

- mass,
- power,
- steering lock,
- caster,
- tire width,
- grip assumptions.

If a car only has packed `data.acd`, Atomic **does not unpack it automatically**.

The user can review/edit detected values and mark corrected values as verified.

---

## Automatic active car + drift-pack detection

Atomic can automatically scan the configured Assetto Corsa installation and
track the currently loaded on-track car through AC's read-only shared-memory
identity page.

When enabled, Atomic:

- scans installed cars at startup and after AC path changes;
- matches the active AC car to the exact installed `content\cars\<folder>`;
- prefers exact folder identity and a normalized exact fallback rather than risky fuzzy matching;
- automatically selects the detected installed car;
- applies the existing pack-inference rules for VDC, Gravy Garage, Team SWARM,
  ADL, WDT/WDTS, Deathwish Garage, or **Custom / Other**;
- updates the Windows selection and Atomic Remote context together.

If pack evidence is insufficient, Atomic falls back to **Custom / Other**
instead of guessing.

Both automatic scanning and automatic active-car/pack selection can be disabled
from the Windows UI.

---

## AC Car Setup Tuner

Atomic can load an existing saved Assetto Corsa setup and recommend changes for drifting.

The setup tuner:

- starts from a real saved `.ini` baseline,
- reads legal `MIN / MAX / STEP` information from unpacked `data/setup.ini` when available,
- separates tires, alignment, suspension, dampers, differential, brakes, gearing, aero, fuel, electronics, and other recognized groups,
- shows **Current → Recommended → Delta**,
- explains why a change is being recommended,
- applies range/click-aware clamping,
- writes a **new `Atomic_*.ini` file** instead of overwriting the original setup.

Atomic intentionally avoids silently modifying the user's baseline setup.

---

## Desired Car Behavior

Per-car **Desired Behavior** lets the driver tell Atomic how they want a particular car to act.

Current behavior axes include:

- Front-end bite — calmer ↔ aggressive
- Rear grip — loose ↔ planted
- Self-steer speed — slower ↔ faster
- Transition speed — smooth ↔ quick
- Angle stability — lively ↔ stable
- Throttle steering — less ↔ more rotation on throttle
- Initiation — progressive ↔ sharp

Each axis uses a bounded `-2` to `+2` target.

Presets currently include:

- Neutral
- Stable & Forgiving
- Fast Tandem
- Fast + Stable
- Aggressive Rotation
- Custom

### Behavior blending

Atomic does not blindly stack every requested behavior change.

When multiple behavior goals affect the same setup parameter, Atomic can:

- damp same-direction stacking,
- detect opposing goals,
- cancel overlapping conflict,
- preserve the stronger remaining direction,
- reduce a behavior contribution when it conflicts with the higher-level session intent,
- explain the compromise in the setup table.

The final value still goes through the normal setup range/click safety layer.

---

## Telemetry Recorder

Atomic reads Assetto Corsa shared-memory telemetry locally and can record sessions for analysis.

Current channels include:

- speed,
- throttle / brake / clutch,
- gear and RPM,
- steering angle and steering rate,
- body slip angle,
- yaw rate,
- lateral / longitudinal G,
- wheel slip,
- wheel load,
- final FFB,
- tire pressure.

The analyzer currently reports items such as:

- detected drift time,
- average / peak drift angle,
- steering-rate behavior,
- yaw-rate behavior,
- transition count and crossover time,
- oscillation heuristics,
- extreme-angle events,
- FFB clipping/headroom.

Telemetry heuristics are evidence for tuning decisions — they are not treated as perfect measurements of driver intent.

---

## Tuning Assistant

The **Tuning Assistant** connects saved telemetry to the rest of Atomic.

Flow:

```text
Desired Behavior
      +
Saved Telemetry
      +
Wheelbase / Wheel
      +
Drift Pack / Car
      +
Session Intent
      ↓
Atomic Assessment
      ↓
Preserve what is already working
      +
Bounded calibration suggestions
      +
Temporary AC setup guidance
```

The current beta can evaluate telemetry-backed evidence for:

- transition speed,
- self-steer speed,
- angle stability,
- oscillation control,
- FFB clipping/headroom.

Some behavior axes are deliberately shown as **target-only** when the current telemetry model cannot isolate them reliably enough. Atomic is intended to say when it does not have enough evidence rather than inventing precision.

### Before / After

When multiple matching sessions exist, Atomic can compare:

- detected drift percentage,
- average transition time,
- oscillation rate,
- extreme-angle event rate,
- FFB clipping.

This is intended for repeated tuning runs with similar driving conditions — not as a universal drift score.

---

# Live AZOM / SimHub integration

Atomic can compare generated settings with the AZOM plugin running inside SimHub and apply supported changes in real time.

## What Atomic does **not** do

Atomic does **not**:

- edit an AZOM configuration file,
- implement the MOZA hardware protocol itself,
- continuously write settings while you move normal Atomic sliders,
- claim a hardware change succeeded without live readback.

## Architecture

```text
Atomic Drift Tuner (.NET 8 / WPF)
            ↓
local named pipe
            ↓
Atomic SimHub Bridge (.NET Framework 4.8 / x86)
            ↓
running AZOM plugin inside SimHub
            ↓
AZOM setting commit
            ↓
live AZOM readback
            ↓
verified target or failure
```

The main Atomic application intentionally has **no SimHub SDK dependency**.

The bridge is isolated so SimHub/plugin integration issues do not break the normal tuning application.

For implementation details, see:

[`docs/AZOM_LIVE_INTEGRATION.md`](docs/AZOM_LIVE_INTEGRATION.md)

---

## AZOM write safety

Because Atomic's compatibility path can enter AZOM through its internal Base-setting commit path, Atomic has its own write guards instead of assuming every public AZOM UI/action guard is in the call chain.

Current safeguards include:

- **explicit Apply / Revert only** for the current UI,
- one Apply/Revert batch at a time,
- one direct bridge write at a time,
- duplicate live-target suppression,
- minimum spacing between direct compatibility commits,
- fresh live readback after each requested change,
- stop the batch at the first unverified setting,
- pre-apply snapshot for Revert,
- Last Batch audit/reporting.

Atomic also contains a dedicated **500 ms last-value-wins debounce service** for any future write-on-slider UI.

A sequence such as:

```text
20 → 21 → 22 → 23 → 24 → 25
```

during the debounce window is designed to produce one eventual target request for `25`, not six writes.

### Important

AZOM integration depends on another actively developed plugin. Internal AZOM changes can break compatibility even when Atomic itself has not changed. Treat live integration as beta functionality and review proposed values before applying them.

---

## Full AZOM Settings

Atomic models the observed AZOM Base controls in typed groups including:

- Core
- Gearshift Vibration
- Wheelbase Effects
- Game Effects
- Protection
- Soft Limit
- High Speed Damping
- Miscellaneous
- FFB Equalizer
- FFB Output Curve

Preference-style settings are kept separate from performance tuning so switching cars does not unexpectedly change unrelated device preferences.

Atomic does not invent undocumented setting ranges/options.

---

# Appearance and accessibility

Atomic has an application-wide theme system built with WPF `DynamicResource` brushes.

Users can customize:

- application background/surfaces,
- panels,
- input fields,
- primary / secondary / muted text,
- accent colors,
- table rows,
- table headers,
- selected table rows,
- grid lines,
- tab headers and active tabs,
- dropdowns,
- checkbox text,
- checkbox background/border/check mark.

The Appearance window includes live previews and contrast checks for major text/background pairs.

### Live theme editing

Appearance is modeless. It can remain open beside Full AZOM Settings, the AC Car Setup Tuner, Telemetry Recorder, Tuning Assistant, Diagnostics, and other normal Atomic windows.

Theme preview changes only WPF application resources. It does **not** change:

- AZOM values,
- tuning inputs,
- calibration,
- telemetry,
- Desired Behavior,
- AC setup recommendations.

---

# Setup, paths, and diagnostics

Atomic separates machine-specific paths from tuning/profile data.

The first-run wizard can detect or browse to:

- SimHub,
- Assetto Corsa installation,
- Assetto Corsa user-data/Documents folder.

Redirected and OneDrive Documents locations are supported.

## System Diagnostics

Diagnostics can check items such as:

- Atomic version,
- Windows / architecture / .NET runtime,
- SimHub installation,
- installed and packaged bridge versions,
- Assetto Corsa installation,
- installed car count,
- AC user-data path,
- AC telemetry availability,
- live Atomic bridge / AZOM readback where available.

## Support package

Atomic can export a local support ZIP containing redacted diagnostics and logs.

It intentionally excludes by default:

- telemetry sessions,
- saved tune profiles,
- AC setup files,
- per-car Desired Behavior contents.

User-profile paths are redacted from the support package.

Atomic does **not** automatically upload the support package.

---

🗺️ Roadmap

ADT is actively evolving during the public beta. Current development priorities include improving tuning intelligence, expanding hardware and car validation, refining the tuning workflow, and making ADT easier to use from setup through final tune.

Future plans include deeper telemetry analysis, automatic Assetto Corsa setup application, a modernized UI, SimHub touchscreen controls, tune sharing, and more.

➡️ [`View the full ADT Development Roadmap `](ROADMAP.md)

Have an idea that isn’t on the roadmap? Feature requests and feedback are welcome through GitHub and the ADT Discord community.

- - -

# Requirements

## For normal beta users

- Windows 10/11 x64
- Assetto Corsa
- SimHub for live bridge functionality
- AZOM for live MOZA/AZOM integration
- Supported or custom wheelbase/wheel profile

A self-contained release package does **not** require Visual Studio or the .NET SDK.

## For source builds

- Windows
- Visual Studio 2022 with **Desktop development with .NET**, or the .NET 8 SDK
- SimHub installed locally if building the bridge
- AZOM enabled in SimHub if testing live integration

---

# Installing a beta release

For packaged releases:

1. Download the installer or portable ZIP from the matching GitHub Release.
2. Start Atomic Drift Tuner.
3. Complete **Setup & Paths**.
4. Confirm the SimHub, Assetto Corsa install, and AC user-data paths.
5. Fully exit SimHub.
6. Use **Install / Repair Packaged Bridge**.
7. Restart SimHub.
8. Enable **Atomic Drift Tuner Bridge** in SimHub.
9. Select your wheelbase, wheel, pack, car, and intent.
10. Generate a tune and review it before applying live AZOM changes.

See the beta testing guide:

[`distribution/README-BETA-TESTERS.md`](distribution/README-BETA-TESTERS.md)

---

# Building from source

Clone the repository, then open:

```text
AtomicDriftTuner.sln
```

in Visual Studio 2022.

Or build the main application from PowerShell:

```powershell
dotnet build AtomicDriftTuner.sln
dotnet run --project .\src\AtomicDriftTuner\AtomicDriftTuner.csproj
```

## Build the SimHub bridge

The bridge is intentionally separate from the main WPF solution.

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\bridge\build-bridge.ps1 -SimHubPath "E:\SimHub"
```

Replace `E:\SimHub` with your actual SimHub installation.

To install the developer-built bridge, **fully exit SimHub first**, then:

```powershell
.\bridge\install-bridge.ps1 -SimHubPath "E:\SimHub"
```

Restart SimHub afterward.

> Do not commit or redistribute SimHub/AZOM/MOZA third-party DLLs unless their licenses explicitly allow it.

---

# Building a tester package

The distribution script can produce a self-contained portable release and, when Inno Setup 6 is installed, a Windows installer.

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\distribution\build-beta-package.ps1 -SimHubPath "E:\SimHub"
```

Outputs are written under:

```text
artifacts\release\
```

The release builder compiles the bridge against the release builder's own SimHub installation and stages the resulting Atomic bridge payload. Testers should not need Visual Studio, PowerShell, the SimHub SDK, or bridge compilation for normal packaged use.

---

# Local data

Atomic stores user-specific data under the user's local application-data folder rather than inside the repository.

Examples include:

```text
%LOCALAPPDATA%\AtomicDriftTuner\settings.json
%LOCALAPPDATA%\AtomicDriftTuner\calibrations.json
%LOCALAPPDATA%\AtomicDriftTuner\car-behavior-targets.json
%LOCALAPPDATA%\AtomicDriftTuner\TelemetrySessions\
%LOCALAPPDATA%\AtomicDriftTuner\Logs\
```

Machine-specific paths are intentionally kept separate from portable tuning/profile information.

---

# Known beta limitations

- Windows only.
- Assetto Corsa is the current simulator target.
- Live wheelbase integration is centered on MOZA + AZOM/SimHub.
- Telemetry diagnosis uses heuristics and should be compared across representative runs.
- Some Desired Behavior axes are not yet isolated reliably enough for telemetry-driven automatic corrections.
- Packed `data.acd` files are not automatically unpacked.
- AC setup recommendations are strongest when the car exposes usable `setup.ini` range/step data.
- AZOM updates can change internal compatibility behavior.
- Atomic currently models the supplied six-band EQ layout for automatic writes; additional reported bands are handled conservatively until frequency-safe mapping is known.
- This is beta software. Review hardware-related changes before applying them.

---

# Safety

Direct-drive wheelbases can generate substantial force.

When testing new settings:

- review the proposed values first,
- start conservatively,
- keep the emergency-stop/power control accessible where applicable,
- stop testing if the wheel behaves unexpectedly.

Atomic's live write verification confirms that AZOM reported the requested value; it cannot guarantee that a particular force level is appropriate for every driver, rig, wheel, firmware version, or physical setup.

---

# Privacy and transparency

Atomic is designed as a local Windows application.

The current source does not contain an automatic telemetry/support-package upload workflow. Telemetry recordings, settings, calibrations, support exports, and logs remain local unless the user chooses to share them.

One reason for publishing Atomic's source is to make hardware-related behavior inspectable by users and other developers.

---

# Repository structure

```text
AtomicDriftTuner/
├─ src/
│  └─ AtomicDriftTuner/              # .NET 8 WPF desktop app
├─ bridge/
│  └─ AtomicDriftTuner.SimHubBridge/ # SimHub bridge
├─ docs/
│  ├─ ARCHITECTURE.md
│  └─ AZOM_LIVE_INTEGRATION.md
├─ distribution/
│  ├─ build-beta-package.ps1
│  ├─ AtomicDriftTuner.iss
│  └─ README-BETA-TESTERS.md
├─ AtomicDriftTuner.sln
├─ CHANGELOG.md
├─ OPEN_SOURCE_RELEASE_CHECKLIST.md
└─ README.md
```

---

# Contributing

Atomic is in active beta development.

Useful contributions include:

- testing on different MOZA wheelbases/wheels,
- testing different AC drift packs/cars,
- reproducible bug reports,
- telemetry-analysis improvements,
- UI/accessibility fixes,
- safer integration handling,
- documentation,
- review of car/setup tuning assumptions.

For a bug report, please include:

- Atomic version,
- bridge version,
- wheelbase and steering wheel,
- drift pack and car,
- expected behavior,
- actual behavior,
- steps to reproduce,
- System Diagnostics support ZIP when relevant.

Please avoid posting personal paths, private telemetry, or third-party proprietary files publicly.

---

# Development principles

Atomic follows a few rules intentionally:

1. **Do not silently overwrite user AC setups.**
2. **Do not invent undocumented AZOM ranges/options.**
3. **Do not claim a live hardware setting changed until readback verifies it.**
4. **Keep SimHub/AZOM integration isolated from the core tuning application.**
5. **Keep hardware/wheel calibration separate by exact setup combination.**
6. **Keep per-car Desired Behavior separate from hardware calibration.**
7. **Prefer small, explainable telemetry corrections over uncontrolled automatic tuning.**
8. **Preserve settings that telemetry indicates are already working.**
9. **Keep machine paths out of portable tune/profile data.**
10. **Make UI theming/accessibility independent from tuning and hardware logic.**

More detail is available in:

[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

---

# Roadmap

Current development direction includes:

- broader telemetry-assisted car-behavior diagnosis,
- stronger front/rear grip and initiation telemetry models,
- more robust AC setup intelligence,
- expanded before/after tuning history,
- profile sharing/comparison,
- additional hardware validation,
- continued AZOM compatibility hardening,
- public-beta testing across more hardware and car combinations.

Automatic live telemetry-to-wheelbase tuning is **not** enabled. Any future live-edit path is expected to keep Atomic's debounce, duplicate-suppression, serialization, and readback-verification safeguards.

---

# Third-party projects and trademarks

Atomic Drift Tuner is an independent community project.

Unless explicitly stated otherwise by the respective rights holders, Atomic Drift Tuner is **not affiliated with, sponsored by, or endorsed by**:

- SimHub
- AZOM
- MOZA Racing
- Kunos Simulazioni
- Assetto Corsa
- the creators of third-party drift packs referenced by the application

Product, plugin, game, and mod names are used for compatibility/identification purposes.

The public repository should not include third-party proprietary binaries unless redistribution is explicitly permitted by their licenses.

---

# License

Atomic Drift Tuner is released under the **MIT License**.

See [`LICENSE`](LICENSE).

---

# Changelog

See [`CHANGELOG.md`](CHANGELOG.md) for detailed beta revision notes.

---

If you are testing Atomic Drift Tuner, thank you for helping validate it across more hardware, cars, and drift styles. The most useful feedback is specific, reproducible, and includes what you expected the car/wheel to do versus what actually happened.


## Automatic active-car detection (remote test 5)

When enabled, Atomic scans the configured Assetto Corsa `content\cars` folder automatically and reads AC's read-only static shared-memory page while a session is active. The session car model is matched to the exact installed car folder, then Atomic selects the already-inferred drift pack and installed car profile.

Auto detection does not modify Assetto Corsa. If no known pack signature is found in the car folder / `ui_car.json` metadata, Atomic keeps the car under **Custom / Other Pack** instead of guessing. Manual pack and car selection remain available.
