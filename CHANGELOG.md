# Atomic Drift Tuner — Changelog

## v0.8.0-beta.1 — Atomic Remote + Automatic AC Context

This is the first public v0.8 beta, promoted from the fully tested remote
development branch.

### Atomic Remote

- Added a same-LAN mobile/browser companion hosted by the Windows Atomic app.
- Added six-digit pairing and a random browser bearer token.
- Remote credentials rotate when the server starts or a new pairing code is requested.
- Remote server accepts only loopback/private-network clients.
- Remote AZOM writes reset to OFF every server start and require an explicit Windows-side opt-in.
- Added a mobile Dashboard / Tune / Behavior / AZOM tab layout.
- Added live AC speed, slip angle, steering angle, FFB output and drift detection.
- Added one shared process-wide AC telemetry stream for both Windows and Remote clients.
- Added current hardware, wheel, pack, car and Drift Target context on the phone.
- Added remote Drift Target selection that updates the authoritative Windows UI.
- Added remote **Generate Tune** requests; tune generation still runs entirely in Windows Atomic.
- Added mobile review of generated AZOM/MOZA and Assetto Corsa FFB recommendations.
- Added mobile self-steer, stability and detail scores, estimated peak wheel torque and tune notes.
- Added per-car Desired Behavior editing and existing behavior presets from the phone.
- Desired Behavior saves through the existing car-behavior profile store and does not directly write the wheelbase.
- Added live AZOM readback for a conservative allow-list of known numeric settings.
- Remote AZOM Apply continues to use the existing guarded/verified `AzomLiveController` path.
- Added **Revert Last Remote Change** for the current Atomic run.

### Automatic active car + drift-pack detection

- Automatically scans installed Assetto Corsa cars when enabled.
- Rescans after AC path changes / successful path auto-detection.
- Reads the active AC car model and track from the read-only `Local\acpmf_static` shared-memory page.
- Matches the active car to the installed AC car folder.
- Uses exact folder identity first and a normalized exact fallback; no risky fuzzy substring matching.
- Automatically selects the matched installed car when enabled.
- Uses Atomic's existing pack inference for VDC, Gravy Garage, Team SWARM, ADL,
  WDT/WDTS, Deathwish Garage, or Custom / Other.
- Falls back to Custom / Other when evidence is insufficient.
- Windows and Atomic Remote context update together.
- Automatic scanning and automatic active-car/pack selection can each be disabled.

### Existing systems retained

- Existing hardware-aware tuning engine and car/setup logic remain intact.
- Existing telemetry analysis and Tuning Assistant remain intact.
- Existing AZOM single-flight, duplicate suppression, spacing, exact commit,
  live readback and stop-on-first-unverified safeguards remain intact.
- Existing AC setup export remains non-destructive.
- Required Atomic SimHub Bridge remains **v0.7.2**.

### Remote safety note

Atomic Remote is intended for private same-LAN use in this beta. Do not
port-forward the Atomic Remote HTTP port or expose it directly to the public
Internet.

---

# Atomic Drift Tuner v0.7.3-beta.1 — Checkbox Readability + Live Theme Editing

This release fixes the remaining platform-theme readability issue around WPF
checkboxes and makes Appearance usable alongside normal Atomic tool windows.


## v0.7.3-beta.2 — Compile fix

- Restored `MainWindow.Number(string value, string label)`, which was
  accidentally removed while converting normal tool windows to modeless
  operation in beta.1.
- Fixes the repeated compiler error:
  `The name 'Number' does not exist in the current context`.
- No tuning, telemetry, theme, AZOM, or bridge behavior changed.
- Required SimHub bridge remains v0.7.2.

## Checkbox theming

Customize Appearance now exposes:

- checkbox label/text,
- checkbox box background,
- checkbox border,
- checkbox check mark.

Atomic uses an explicit application-wide `CheckBox` template. Full AZOM Settings
also inherits that template even though it applies its own checkbox spacing.

The Appearance preview includes checked, unchecked and indeterminate examples.
The contrast checker evaluates checkbox text against the common panel/surface
backgrounds.

## Modeless Appearance window

`Customize Appearance` now opens with `Show()` instead of `ShowDialog()`.

Normal tool windows are also opened modeless so they do not disable Appearance:

- Full AZOM Settings,
- AC Car Setup Tuner,
- Telemetry Recorder,
- Tuning Assistant,
- System Diagnostics,
- Setup & Paths when opened after first-run.

Only the initial first-run machine-setup wizard remains modal.

Atomic keeps one instance of each normal tool window at a time to avoid duplicate
stateful views. Re-clicking a tool button restores/activates its existing window.

### Tool-state safety

This change is UI-only:

- ThemeWindow changes application-level `DynamicResource` brushes.
- Saving Appearance writes only `AppSettings.Theme`.
- It does not mutate `TuneInput`, calibration, telemetry sessions, AC setup
  recommendations, Desired Behavior, or AZOM settings.
- Tool windows continue using the hardware/car/tune snapshot they received when
  they were opened.
- Live AZOM write serialization/duplicate/rate guards from v0.7.2 remain intact.

## Bridge

No bridge code changed in v0.7.3. The required bridge remains `0.7.2`.

If bridge v0.7.2 is already installed and working, do not reinstall it for this
UI revision.

---


# Atomic Drift Tuner v0.7.2-beta.1 — AZOM Write Guard Hardening

This revision hardens the Live AZOM write path before a wider public beta.

The AZOM integration still uses the proven exact commit/readback architecture,
but Atomic now explicitly protects the compatibility path instead of assuming
AZOM's outer public-action write guards are always in the call chain.

## New safeguards

### Current explicit Apply/Revert workflow

- one Atomic Apply/Revert batch at a time;
- one direct bridge request at a time;
- duplicate live targets are suppressed before an internal AZOM commit;
- bridge direct commits are separated by at least 120 ms without blocking
  SimHub's DataUpdate thread;
- each setting still waits across the bridge snapshot refresh boundary and is
  verified from live AZOM readback;
- the batch still stops at the first unverified setting.

The current UI does **not** write AZOM continuously while a slider/value is being
edited.

### 500 ms interactive debounce

A new `AzomInteractiveWriteService` is the required path for any future
write-on-slider behavior.

It waits until a specific setting has been unchanged for 500 ms. If values arrive
during that window, the older requests are superseded and never sent.

Example:

`20 -> 21 -> 22 -> 23 -> 24 -> 25`

within the debounce window produces one eventual request for `25`.

This gives Atomic:

`last value wins + 500 ms debounce + single-flight direct writes + duplicate suppression + live readback verification`

## Bridge revision

The bridge version is now `0.7.2` because bridge-side duplicate suppression and
direct-write spacing were added.

Unlike v0.7.1, testing this revision requires rebuilding/reinstalling the bridge.

---


# Atomic Drift Tuner v0.7.1-beta.1 — UI Readability & Theme Control

v0.7.1-beta.1 keeps the v0.7.0 telemetry-assisted tuning behavior and the known
working v0.6.0 SimHub bridge integration, while fixing a major UI-readability
problem: WPF/Windows default table, tab and field colors can produce combinations
such as white text on a white background.

## Global readability theming

Customize Appearance now exposes independent colors for:

### Input fields
- field background,
- field text,
- field border.

### Tables / DataGrids
- normal row background,
- alternating row background,
- cell text,
- column-header background,
- column-header text,
- selected-row background,
- selected-row text,
- grid lines.

### Tabs / section navigation
- tab background,
- tab text,
- active-tab background,
- active-tab text,
- tab border.

### Dropdowns
The existing closed/popup/highlight dropdown controls remain independently
themeable.

All of these values are applied as `DynamicResource` brushes at application
scope. Full AZOM Settings, the AC Car Setup Tuner, Diagnostics, Tuning Assistant
and future DataGrid/TabControl/TextBox instances inherit the same readable
templates unless a view intentionally overrides them.

## Readability preview

The Appearance window now previews:
- a normal input field,
- an AZOM-style table including a selected row,
- tab headers,
- dropdowns.

The contrast readout checks the major text/background pairs and flags any pair
below 4.5:1, including table headers and selected rows.

Existing theme files remain compatible. Missing v0.7.1 fields receive safe
defaults from the `ThemeSettings` property initializers.

## Bridge

No SimHub bridge behavior changed in this release. If the existing bridge is
working, there is no reason to reinstall it just to test the UI revision.

---


# Atomic Drift Tuner v0.7.0-beta.1 — Telemetry-Assisted Tuning

v0.7.0 connects the telemetry recorder, per-car Desired Behavior, calibration
engine, AC FFB generation and AC Car Setup Tuner into a single review workflow.

The working v0.6.0 SimHub bridge implementation remains unchanged.

## Tuning Assistant

The main window now includes **OPEN TUNING ASSISTANT**.

The assistant loads saved telemetry sessions matching the exact:

- wheelbase,
- steering wheel,
- drift pack,
- car.

It also loads that car's saved Desired Behavior target.

### Desired vs Observed

v0.7.0-beta.1 currently evaluates these telemetry-backed areas:

- transition speed,
- self-steer speed,
- angle-stability evidence,
- oscillation control,
- FFB clipping/headroom.

Front-end bite, rear grip, throttle steering and initiation remain visible as
saved targets, but the assistant explicitly marks them as not isolated reliably
enough for automatic telemetry correction yet.

This is intentional: Atomic should say when telemetry cannot support a conclusion
instead of inventing precision.

### Preserve-good-settings logic

When a measured area is already on target, the assistant adds it to a Preserve
recommendation rather than changing related parameters merely because telemetry
exists.

### AZOM / AC FFB recommendations

The assistant converts telemetry evidence into the existing bounded Atomic
calibration model.

Examples include:

- wheel speed,
- wheel damper,
- wheel friction,
- high-speed damping,
- interpolation,
- base-torque calibration,
- AC gain.

**Apply Calibration Recommendation** updates Atomic's saved calibration only.
It does not directly write AZOM. The generated tune can still be reviewed/applied
through Full AZOM Settings -> Live AZOM.

FFB clipping is estimated from detected-drift samples at or above 98% absolute
FFB output. Sustained clipping can propose a small AC Gain reduction.

### AC Car Setup guidance

When telemetry indicates the car behavior does not match Desired Behavior,
the assistant can propose a one-step temporary behavior correction.

**Open AC Setup with Guidance** passes those temporary Desired Behavior values
into the existing AC setup tuner.

The existing behavior-blending and setup.ini range/click safeguards remain in
control of the actual parameter recommendations.

Telemetry guidance is **not saved automatically**. The user must explicitly
choose Save for This Car and/or save a generated AC setup.

### Before / After

When multiple matching saved sessions exist, the assistant compares the selected
session with the previous one using:

- detected drift percentage,
- average transition crossover,
- oscillation rate per drift minute,
- extreme-angle rate per drift minute,
- FFB clipping percentage.

These comparisons are context, not universal performance scores. Track layout,
driver task and driving style can change the metrics.

### Session quality / confidence

Recommendations expose LOW / MEDIUM / HIGH confidence based on available drift
time, transition count, drift entries and effective sample rate.

Some heuristics intentionally cap confidence because driver steering technique or
intentional high-angle entries can affect the signal.

## Beta packaging

The v0.6.3 first-run setup, diagnostics, support export, portable package and
Inno Setup workflow remain available.

To build tester packages on the developer PC:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\distribution\build-beta-package.ps1 -SimHubPath "E:\SimHub"
```

The bridge source remains the known-working v0.6.0 implementation.

---

# Atomic Drift Tuner v0.6.3-beta.1 — Beta Distribution Branch

v0.6.3-beta.1 keeps the v0.6.2 tuning/behavior-blending logic and focuses on
making Atomic Drift Tuner testable on PCs that do not share the developer's
folder layout.

## Beta-distribution features

### First-run Setup & Paths wizard

On first launch Atomic now detects or lets the tester choose:

- SimHub root,
- Assetto Corsa install root,
- Assetto Corsa user-data/Documents root.

Machine paths are stored only in `%LOCALAPPDATA%\AtomicDriftTuner\settings.json`
and are not embedded in shared tune/profile files.

SimHub detection now checks:

1. saved path,
2. a running `SimHubWPF` process,
3. Windows uninstall registry entries,
4. common Program Files locations,
5. manual Browse.

Assetto Corsa detection retains Steam-library scanning and manual Browse.

The AC setup tuner now respects the configured Assetto Corsa user-data root
instead of assuming a hard-coded Documents location.

### In-app bridge install/repair

Release packages contain:

`BridgePayload\AtomicDriftTuner.SimHubBridge.dll`

**Setup & Paths** can install/repair that DLL into the detected SimHub folder.

Atomic refuses to replace the bridge while SimHub is running. If Windows denies
write access to the SimHub folder, Atomic can request UAC elevation for the
bridge-copy operation only.

Regular beta testers therefore do not need to build the bridge, install Visual
Studio, or change PowerShell execution policy.

### System Diagnostics

The main window now includes **SYSTEM DIAGNOSTICS**.

Diagnostics check:

- Atomic build,
- Windows/process/.NET environment,
- SimHub detection,
- installed bridge file,
- packaged bridge payload,
- Assetto Corsa install,
- installed-car count,
- AC user-data location,
- AC telemetry shared memory,
- live Atomic bridge/AZOM readability when SimHub is running.

### Support-package export

Diagnostics can export `AtomicSupport_*.zip` containing:

- `diagnostics.json`,
- `diagnostics.txt`,
- `settings-redacted.json`,
- Atomic `.log` files with the Windows user path redacted,
- a privacy note.

The support exporter intentionally excludes:

- telemetry CSV/JSON sessions,
- saved tune profiles,
- Assetto Corsa setups,
- per-car behavior profiles.

### Distribution build

`distribution\build-beta-package.ps1`:

1. publishes Atomic as self-contained Windows x64,
2. builds the SimHub bridge against the release builder's SimHub installation,
3. stages the bridge as a packaged payload,
4. creates a portable ZIP,
5. builds an Inno Setup installer when Inno Setup 6 is available.

The installer defaults to:

`%LOCALAPPDATA%\Programs\AtomicDriftTuner`

so the main application itself does not require administrator installation.

## Developer build

Main app:

```powershell
dotnet build AtomicDriftTuner.sln
```

Bridge development builds remain separate.

## Produce tester packages

From a PowerShell window opened in the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\distribution\build-beta-package.ps1 -SimHubPath "E:\SimHub"
```

That command builds the bridge for inclusion in the beta package; it does not
install or replace the bridge in your own SimHub folder.

Outputs are written under `artifacts\release`.

---

# Atomic Drift Tuner v0.6.2 — Desired Behavior Blending

Atomic Drift Tuner is a Windows WPF application for generating and refining MOZA/AZOM settings, Assetto Corsa FFB settings, and per-car AC setup recommendations for drifting.

## v0.6.2 highlights — conflict-aware behavior blending

v0.6.2 builds directly on the per-car Desired Behavior controls from v0.6.1.

Individual behavior axes were already producing the intended setup direction. This
release focuses on what happens when several requested behaviors affect the same
setup parameter at once.

### What changed

The AC Car Setup Tuner now tracks each desired-behavior contribution separately
instead of simply summing every requested adjustment.

When multiple goals push a parameter in the **same direction**, Atomic applies
**diminishing returns** to the additional contributions. This keeps combinations
such as front-end bite + faster self-steer + sharper initiation from blindly
stacking into an oversized toe/setup change.

When goals push a parameter in **opposite directions**, Atomic:

1. identifies the competing behavior goals,
2. cancels their overlapping influence,
3. softens the remaining net request as the conflict becomes stronger,
4. keeps the stronger net direction rather than arbitrarily picking one goal,
5. explains the compromise in the row's **Reason** text.

Example:

`quick transitions + high angle stability`

can pull rear ARB, rear spring, rebound and coast-differential behavior in
opposite directions. v0.6.2 blends those requests instead of stacking both
unchanged.

### Session intent still has priority

Training / Realistic / Fast Self-Steer / Tandem / Competition remains the
higher-level driving intent.

If a per-car Desired Behavior target requests the opposite direction from the
selected session intent on a recognized parameter, Atomic keeps the session
intent dominant and reduces the per-car behavior contribution to 65%.

That compromise is explicitly reported rather than hidden.

### New UI feedback

The Desired Car Behavior panel now includes a **BEHAVIOR BLEND** preview.

Before Generate it shows likely interaction groups for the current sliders. After
Generate it shows the actual blend report for the loaded car/setup.

The setup table also gains a **Blend** column with states such as:

- Single goal
- Aligned / damped
- Compromise
- Intent compromise
- Behavior + intent compromise
- Balanced out

The **Reason** column contains the detailed explanation.

A new **Fast + Stable** preset intentionally combines fast transition/self-steer
goals with higher angle stability so the blend layer can resolve the competing
rear-platform requests rather than forcing the user to choose only one character.

### Persistence

Per-car Desired Behavior persistence is unchanged:

`%LOCALAPPDATA%\AtomicDriftTuner\car-behavior-targets.json`

Existing v0.6.1 profiles remain compatible.

### Bridge status

**No bridge code changed in v0.6.2.**

If Live AZOM is already reading, applying and verifying correctly, do not rebuild
or reinstall the bridge. Rebuild only the main `AtomicDriftTuner.sln`.

## v0.6.0 highlights

### Live AZOM integration

The **Full AZOM Settings → Live AZOM** page can now:

- read current AZOM Base properties from SimHub,
- compare live values to the generated Atomic target,
- show which settings differ,
- estimate how many SimHub actions are needed,
- apply only changed, supported settings,
- read the values back after applying,
- save a pre-apply snapshot,
- revert the last Atomic apply.

Live reading uses the bundled, isolated **Atomic Drift Tuner SimHub Bridge** plugin. The bridge only reads AZOM properties and exposes them to the desktop app over a local named pipe.

Writes are performed by the desktop app through SimHub's supported command-line action trigger (`SimHubWPF.exe -triggeraction ...`). Atomic does not reference or call AZOM internals.

### What live apply controls

Performance controls currently supported include:

- Game FFB Strength
- Base Torque Output
- Rotation
- Maximum Wheel Speed
- Interpolation
- Wheel Damper / Friction / Inertia / Spring
- Game Damper / Friction / Inertia / Spring
- Steering Wheel Inertia
- Soft Limit Stiffness
- High Speed Damping + Trigger Speed
- Road Sensitivity
- six-band legacy EQ when the firmware exposes a six-band layout
- FFB output curve X/Y nodes

Optional preference apply can additionally control the AZOM-exposed toggles/settings such as Hands-Off Protection, Retain Game FFB, FFB Reversal, Base Status LED, Bluetooth, Standby Mode, and Gearshift Vibration intensity.

Atomic deliberately leaves controls that AZOM does not currently expose as public SimHub properties/actions alone (for example the Base-page neutral-vibration/debounce host options and standby-timer dropdown).

### Safe apply behavior

- Live apply is opt-in and requires confirmation.
- Only differing settings are written.
- Preference toggles are excluded by default.
- A pre-apply live snapshot is saved to `%LOCALAPPDATA%\AtomicDriftTuner\azom-last-apply-backup.json`.
- **Revert Last Apply** targets only the properties changed by Atomic.
- If the optional bridge is not installed, the rest of Atomic Drift Tuner still works normally.

### UI appearance customization

The main window now has **CUSTOMIZE APPEARANCE**.

Built-in presets:

- Atomic Cyan
- Drift Orange
- Neon Purple
- Race Red
- Ice Blue
- Monochrome

Users can also edit individual `#RRGGBB` values for:

- application background,
- surfaces,
- panels,
- inputs,
- borders,
- primary/secondary/muted text,
- accent color,
- accent text.

Themes are stored only in Atomic's `settings.json`; tuning/calibration logic is completely separate.

## Main application build

Requirements:

- Windows 10/11
- Visual Studio 2022
- .NET 8 SDK
- **Desktop development with .NET** workload

Open:

`AtomicDriftTuner.sln`

Then **Build → Rebuild Solution**, and press **F5**.

Command line:

```powershell
dotnet build AtomicDriftTuner.sln
dotnet run --project .\src\AtomicDriftTuner\AtomicDriftTuner.csproj
```

## Build/install the optional SimHub bridge

The bridge is intentionally a separate project so SimHub dependencies cannot break the main .NET 8 application.

1. Make sure SimHub is installed and closed before copying the DLL.
2. Open `bridge\AtomicDriftTuner.SimHubBridge.sln`.
3. Build **Release**.
4. Copy `bridge\AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll` into your SimHub installation folder (normally `C:\Program Files (x86)\SimHub\`).
5. Restart SimHub.
6. In **Add/remove features**, enable **Atomic Drift Tuner Bridge** if required.
7. Enable AZOM and connect the MOZA base.
8. In Atomic: **OPEN FULL AZOM SETTINGS → Live AZOM → READ LIVE AZOM**.

The bridge targets x86 .NET Framework 4.8 because it runs inside SimHub. If SimHub is installed somewhere else, build with the `SimHubInstallPath` MSBuild property or edit the bridge `.csproj` default.

## Existing features retained

- adaptable wheelbase and steering-wheel profiles,
- VDC / Gravy Garage / SWARM / ADL / other pack profiles,
- installed AC car scanning,
- confidence tracking for scanned car data,
- per hardware + wheel + pack + car calibration,
- AC in-game car setup tuner and safe `Atomic_*.ini` export,
- AC shared-memory telemetry recorder,
- drift/session analysis,
- telemetry-generated calibration suggestions,
- saved Atomic tune profiles.

## Important note

Atomic's tuning formulas remain transparent heuristics and calibration logic, not a claim of a universally optimal drift setup. Live AZOM integration changes the delivery mechanism: it does not turn an experimental recommendation into a proven one. Review the comparison before applying and test changes progressively.
\n## Quick bridge setup\n\nAfter the main app builds, open PowerShell in `bridge` and run:\n\n```powershell\n.\\build-bridge.ps1\n.\\install-bridge.ps1\n```\n\nClose SimHub before installing the bridge, then restart SimHub and enable **Atomic Drift Tuner Bridge** under Settings > Plugins.\n

## v0.6.0 bridge build fix

The SimHub bridge now references the `log4net.dll` shipped with the same SimHub installation as `SimHub.Plugins.dll`. This fixes CS0012 errors involving `ILog` when building against current SimHub versions. The bridge build script also validates that both DLLs exist before compiling.


## v0.6.0 Live AZOM permission fix

v0.6.0 fixes a Windows named-pipe permission issue that could show **"Access to the path is denied"** when SimHub and Atomic Drift Tuner were running at different elevation levels.

The SimHub bridge now creates its read-only local pipe with an explicit ACL:
- current Windows user: Full Control
- local authenticated users: Read/Write to the pipe

This does not grant direct wheelbase write access. The bridge remains read-only; setting changes still go through SimHub/AZOM actions.

After updating:
1. Rebuild the bridge against your SimHub install.
2. Close SimHub completely.
3. Reinstall the bridge DLL.
4. Restart SimHub and confirm the bridge is enabled.
5. Reopen Atomic Drift Tuner and use **Full AZOM Settings → Live AZOM → Read Live AZOM**.


## v0.6.0 AZOM property compatibility diagnostics

v0.6.0 improves Live AZOM discovery.

The bridge now:
- enumerates the property names SimHub is actually publishing,
- detects the modern `AZOM.*` namespace,
- detects the older `Moza.*` namespace used by early AZOM release candidates,
- reports `BaseConnected`,
- reports the number of Base-setting properties available,
- gives a specific error for an old AZOM build versus an unconnected wheelbase.

For safety, Live Apply/Revert remains enabled only for the modern `AZOM.*` namespace because current AZOM documents those action names. Legacy `Moza.*` installations can be detected/read where possible, but should be updated before automatic writes.


## v0.6.0 — AZOM 1.5.x direct-read fallback

On some SimHub builds, a sibling plugin can successfully connect to SimHub but
`PluginManager.GetPropertyValue("AZOM.FfbStrength")` does not resolve AZOM's
attached delegates from that plugin context.

v0.6.0 keeps the public-property reader, but first attempts a same-process
read of AZOM's live `MozaPlugin.Data` object using reflection. This does not
modify AZOM and does not add a compile-time dependency on `MozaPlugin.dll`.

The raw AZOM fields are converted with the same scaling used by AZOM 1.5.x:
FFB/damper/friction/speed/spring/inertia in tenths, game-effect gains from
0–255 to percent, steering `Limit * 2`, road-sensitivity register to preset
0–10, and the soft-limit stiffness affine mapping.

Writes remain unchanged: Atomic still invokes AZOM's documented SimHub actions.
The bridge remains read-only.


## v0.6.0 crash-safety change

v0.6.0 changes how Live AZOM reads are performed after reports that v0.6.0 could freeze/exit during **Read Live AZOM**.

Key changes:
- the named-pipe server thread no longer touches AZOM or SimHub property getters;
- AZOM values are captured at ~5 Hz on SimHub's own `DataUpdate` thread and stored as an immutable snapshot;
- exact documented `AZOM.*` property names are queried directly rather than globally enumerating properties;
- the fallback reads only named fields from AZOM's data object and runs only on SimHub's update thread;
- the desktop read has a hard 2.5-second timeout and response-size cap;
- WPF dispatcher/task exceptions are logged under `%LOCALAPPDATA%\AtomicDriftTuner\Logs\atomic-crash.log` instead of silently closing the UI when recoverable.

The bridge remains read-only. Automatic setting changes still go through AZOM's documented SimHub actions.


## v0.6.0 Live AZOM comparison-grid crash fix

v0.6.0 fixes a WPF crash that occurred immediately after a successful Live AZOM read.

Root cause:
- `AzomApplyPlanItem.IsDifferent` is a computed read-only property.
- `DataGridCheckBoxColumn` created a TwoWay binding by default.
- WPF therefore attempted to write back to `IsDifferent`, threw `InvalidOperationException`, and retriggered the same exception during layout.

Fix:
- `IsDifferent` is now explicitly `Mode=OneWay`.
- The `Writable` checkbox column is also explicitly OneWay.
- All Live AZOM comparison-grid bindings are now explicitly display-only.
- No tuning, telemetry, AZOM action, or bridge communication logic was changed for this fix.

If the v0.6.0 bridge is already installed and working, rebuilding/reinstalling the bridge is not required for this specific UI fix. Rebuild the main `AtomicDriftTuner.sln` first.


## v0.6.0 — in-process AZOM action relay

Live Apply/Revert no longer launches `SimHubWPF.exe -triggeraction`.

Atomic now sends an action request through its named-pipe bridge. The bridge:
- accepts only action names beginning with `AZOM.`,
- queues the request,
- executes `PluginManager.TriggerAction(actionName)` on SimHub's `DataUpdate` thread,
- returns success/error to Atomic,
- leaves the existing inter-action delay and post-apply readback in place.

This avoids the `SimHubWPF.exe` helper-process exit-code `-1` issue seen on some
installations, while still using AZOM's registered SimHub actions rather than
writing MOZA hardware registers directly.

**Important:** v0.6.0 changes the bridge protocol, so rebuild and reinstall the
bridge before testing Apply/Revert.


## v0.6.0 — readback-verified Live AZOM Apply/Revert

Atomic no longer assumes that a SimHub action call changed AZOM.

For each setting:
1. Trigger the action through the in-process Atomic bridge.
2. Wait for the live AZOM snapshot to refresh.
3. Verify the exact property reached the requested target.
4. If it did not, retry through SimHub's documented
   `SimHubWPF.exe -triggeraction` command.
5. Ignore the helper process exit code as a success/failure signal.
6. Verify AZOM again using the live readback.
7. If the setting still did not move, stop the apply batch and report the
   exact setting/current/target instead of continuing blindly.

This release is intended to diagnose and work around installations where
`PluginManager.TriggerAction` returns without producing an AZOM setting change,
and installations where the SimHub CLI helper returns `-1` even after handing
off the command.


## v0.6.0 — exact AZOM compatibility write fallback

Observed on a real installation:
- Live AZOM reads worked.
- `PluginManager.TriggerAction("AZOM.TorqueDownCoarse")` returned without error,
  but the live `AZOM.Torque` value did not move.
- `SimHubWPF.exe -triggeraction ...` returned exit code `-1` and also did not move
  the live value.
- A generated Base Torque target such as 53% also cannot be reached exactly by
  AZOM's public ±5/±10 step actions from a 95% starting point.

v0.6.0 adds a **third, last-resort compatibility path**.

When both documented/public transports fail readback verification, the bridge:
1. stays on SimHub's `DataUpdate` thread;
2. locates the loaded AZOM (`MozaPlugin`) runtime;
3. constructs AZOM's own internal `SimHubRegistrar`;
4. for modern AZOM builds, locates the matching `BaseSettingCatalog` definition;
5. invokes AZOM's own `StepBaseSetting(def, exactDelta)` or `SetToggle(def, target)`;
6. lets AZOM perform its normal live-data update, wheelbase command write, and
   `SaveSettings()` path;
7. returns control to Atomic;
8. Atomic reads the live AZOM value back and accepts success only if the target
   was actually reached.

Compatibility fallbacks are allow-listed to known Base-page properties and are
never used for arbitrary reflection calls.

Older AZOM builds are supported for FFB Strength / Torque / Rotation through
their older private step methods when present.

### Safety

The fallback is **not** the primary transport. It is attempted only after both
public action transports failed verification. Atomic still stops the batch on
the first setting that cannot be verified.

Because v0.6.0 changes the bridge protocol, rebuild and reinstall the bridge
before testing Apply/Revert.


## v0.6.0 — AZOM 1.5.7 exact core-setting fallback

v0.6.0 assumed a generic `BaseSettingCatalog` conversion API for exact numeric
writes. On AZOM 1.5.7, the core Base controls instead use explicit private
`SimHubRegistrar` commit methods:

- `StepFfbStrength(int deltaPct)`
- `StepTorque(int deltaPct)`
- `StepRotation(int deltaDeg)`

Those methods update AZOM's live data, issue the same wheelbase command used by
the UI/action path, and call `SaveSettings()`.

v0.6.0 therefore tries those real AZOM 1.5.7 methods first. For example, if
live torque is 95% and Atomic requests 53%, the compatibility fallback invokes
`StepTorque(-42)` and then accepts success only if live AZOM readback becomes
53%.

The generic catalog path remains only as a compatibility path for AZOM builds
that actually expose it.

Because the bridge code changed, rebuild and reinstall the bridge before
testing Apply/Revert.


## v0.6.0 — correct AZOM BaseSettingCatalog reflection

The v0.6.0 error `AZOM numeric setting conversion methods were not found`
came from two incorrect reflection assumptions.

Current AZOM stores:
- `NumericSetting.GetRaw` as a `Func<MozaData,int>` field,
- `NumericSetting.ToDisplay` as a `Func<int,int>` field,

not as CLR methods.

AZOM also retains its initialized SimHub registrar in the private
`MozaPlugin._simHubRegistrar` field.

v0.6.0:
1. reuses AZOM's existing live `_simHubRegistrar`;
2. reads `GetRaw` and `ToDisplay` as delegate fields and invokes them;
3. locates the actual `BaseSettingCatalog.Numeric` definition by AZOM property name;
4. invokes AZOM's own private `StepBaseSetting(def, exactDelta)`;
5. verifies the exact live AZOM property after the commit;
6. tries the old explicit `StepTorque`/`StepFfbStrength`/`StepRotation` path only
   for older AZOM builds;
7. leaves the SimHub CLI transport as the final fallback instead of running it
   before the exact AZOM commit.

For 95% → 53% Base Torque, the intended current-AZOM path is:
`StepBaseSetting(TorqueDefinition, -42)` followed by live `AZOM.Torque == 53`
verification.

Because this release changes bridge code, rebuild and reinstall the bridge.


## v0.6.0 — verified exact AZOM batches + theme/color-wheel upgrade

### Live AZOM

v0.6 promotes the exact AZOM commit path proven on AZOM 1.5.7 to the primary
write transport. Public SimHub actions and the CLI are now fallbacks.

- Select each setting individually with the **Apply?** checkbox.
- **Select All Differences**, **Clear Selection**, or select one AZOM section.
- Only selected, writable, differing rows are sent to the wheelbase.
- Every setting is read back before the batch proceeds to the next row.
- The batch stops on the first unverified setting.
- The normal pre-apply snapshot/revert record is still saved.
- The new **Last Batch** tab records Before → Target → Actual After, verification,
  and the transport used for each selected row.
- Exact commits can land on values such as 53% even when the public AZOM action
  grid only exposes ±5/±10 steps.

### Appearance

- Added a native WPF **HSV color wheel** with brightness control.
- Click any theme color field and edit it visually on the wheel.
- Added independent dropdown/ComboBox colors for:
  - closed background and text,
  - popup background and text,
  - hover/selected highlight and highlight text,
  - dropdown border.
- Added a custom ComboBox template so Windows' default light popup no longer
  makes dark themes unreadable.
- Added a live dropdown preview and contrast-ratio warning.
- Theme data remains isolated from tuning/calibration/telemetry logic.

### AZOM source alignment

The exact numeric bridge path matches current AZOM's declarative
`BaseSettingCatalog.Numeric` definitions and `SimHubRegistrar.StepBaseSetting`:
AZOM converts display↔raw values, clamps to the setting/firmware range, writes
the correct command(s), and persists the active settings. Road Sensitivity keeps
its separate preset+EQ commit path.
