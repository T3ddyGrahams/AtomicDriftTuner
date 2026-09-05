# Atomic Drift Tuner SimHub Bridge

The **ADT SimHub Bridge** is an optional, isolated SimHub plugin that connects Atomic Drift Tuner (ADT) with SimHub and supported AZOM functionality.

The main ADT application does **not** directly reference SimHub assemblies and continues to build and run without the bridge.

The bridge exists specifically to keep SimHub/AZOM integration isolated from ADT's core tuning application.

---

## What the Bridge Does

The bridge runs inside SimHub and provides a local communication path between SimHub/AZOM and the Windows ADT application.

Current bridge responsibilities include:

- reading supported live AZOM values;
- exposing live AZOM state to ADT;
- receiving supported AZOM change requests from ADT;
- committing supported changes through the compatible AZOM integration path;
- reading the resulting live AZOM value back for verification;
- returning success or failure information to ADT;
- communicating with ADT through a local named pipe.

The bridge does **not** independently decide what settings should be changed.

ADT remains responsible for generating recommendations, validating requested values, controlling write sequencing, and determining whether a requested change should be sent.

---

## Architecture

```text
Atomic Drift Tuner (.NET 8 / WPF)
            ↓
local named pipe
            ↓
ADT SimHub Bridge (.NET Framework 4.8 / x86)
            ↓
running AZOM plugin inside SimHub
            ↓
supported AZOM setting commit
            ↓
live AZOM readback
            ↓
verified target or failure
```

Keeping this integration in a separate bridge prevents SimHub or plugin dependencies from becoming dependencies of the main ADT application.

---

## Write Safety

Live hardware-related changes require additional safeguards.

ADT currently controls AZOM writes using protections including:

- explicit user-requested Apply/Revert operations;
- one Apply/Revert batch at a time;
- serialized bridge writes;
- duplicate-target suppression;
- minimum spacing between compatibility commits;
- validation of supported values before they are sent;
- fresh live readback after each requested change;
- stopping a batch when a requested value cannot be verified;
- pre-apply snapshots for supported Revert operations.

The bridge reports the result of a requested operation back to ADT.

A successful write means that the live AZOM value was read back and matched the requested target. It does **not** mean that a particular setting is physically appropriate for every wheelbase, rim, firmware version, rig, or driver.

Always review hardware-related recommendations before applying them.

---

## Why a Separate Project?

SimHub hosts plugins using a different runtime environment from the main ADT application.

The bridge targets the environment required by SimHub, while Atomic Drift Tuner itself is a modern .NET 8 WPF desktop application.

Keeping the bridge separate provides several advantages:

- the main ADT application has no SimHub SDK dependency;
- ADT can run without SimHub or the bridge;
- SimHub/plugin compatibility problems are isolated from the core tuner;
- bridge updates can be developed and validated separately;
- third-party integration code remains clearly separated from ADT's tuning engine.

---

## Normal Beta Installation

Normal beta users should **not need to build the bridge manually**.

Packaged ADT releases include the supported bridge payload.

To install or repair the packaged bridge:

1. Install or extract the matching ADT beta release.
2. Start Atomic Drift Tuner.
3. Complete **Setup & Paths** if required.
4. Confirm the configured SimHub path.
5. Fully exit SimHub.
6. In ADT, use **Install / Repair Packaged Bridge**.
7. Restart SimHub.
8. In SimHub **Add/remove features**, enable **Atomic Drift Tuner Bridge** if it is not already enabled.
9. Ensure AZOM is enabled when testing AZOM integration.
10. Use ADT's diagnostics or live AZOM functionality to verify communication.

The packaged bridge version should match the version expected by the corresponding ADT release.

---

## Developer Build

The bridge is intentionally separate from the main WPF solution.

Before running the PowerShell build scripts in a PowerShell session, temporary script execution permission can be granted with:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

This changes the execution policy only for the current PowerShell process.

From the repository root, build the bridge with:

```powershell
.\bridge\build-bridge.ps1 -SimHubPath "C:\Program Files (x86)\SimHub"
```

If SimHub is installed elsewhere, replace the path with the actual SimHub installation.

For example:

```powershell
.\bridge\build-bridge.ps1 -SimHubPath "D:\SimHub"
```

---

## Developer Installation

**Fully exit SimHub before installing or replacing the bridge DLL.**

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\bridge\install-bridge.ps1 -SimHubPath "C:\Program Files (x86)\SimHub"
```

For a custom SimHub installation:

```powershell
.\bridge\install-bridge.ps1 -SimHubPath "D:\SimHub"
```

Restart SimHub after installation.

Then:

1. Open SimHub.
2. Confirm **Atomic Drift Tuner Bridge** is enabled.
3. Confirm AZOM is enabled if AZOM integration is being tested.
4. Start ADT.
5. Use ADT diagnostics or the live AZOM interface to confirm bridge communication.

---

## Visual Studio Build

Developers can also build the bridge directly with Visual Studio 2022.

1. Install SimHub.
2. Open the bridge solution/project in Visual Studio 2022.
3. Build the bridge in **Release** configuration.
4. Fully exit SimHub.
5. Copy the resulting `AtomicDriftTuner.SimHubBridge.dll` into the appropriate SimHub installation location.
6. Restart SimHub.
7. Enable **Atomic Drift Tuner Bridge** in SimHub if required.

If SimHub is installed in a custom location, configure the appropriate `SimHubInstallPath` MSBuild property for the bridge project.

The PowerShell build/install scripts are recommended because they provide a more repeatable development workflow.

---

## SimHub Dependency Note

The bridge references dependencies supplied by the local SimHub installation.

In particular, it references `log4net.dll` from SimHub because types exposed by `SimHub.Plugins.dll` depend on log4net.

Do **not** add or redistribute a separate copy of SimHub, AZOM, MOZA, or other third-party proprietary binaries unless their licenses explicitly permit redistribution.

Do not add a separate NuGet `log4net` package unless you intentionally want to take responsibility for managing compatibility with the SimHub SDK/runtime version being targeted.

---

## Failure Behavior

The bridge should fail **visibly, safely, and recoverably**.

A failed or unavailable bridge should not prevent normal non-bridge ADT functionality from operating.

Examples of conditions that should be treated as integration failures include:

- SimHub is not running;
- the ADT bridge is not loaded;
- AZOM is unavailable;
- a requested setting is unsupported;
- a requested value fails validation;
- communication with the bridge fails;
- a live value cannot be read;
- a requested change cannot be verified through readback;
- bridge and ADT versions are incompatible.

ADT should report these conditions rather than silently assuming a hardware-related operation succeeded.

---

## Additional Documentation

For more information about the live integration architecture and supported behavior, see:

[`../docs/AZOM_LIVE_INTEGRATION.md`](../docs/AZOM_LIVE_INTEGRATION.md)

For the overall ADT architecture, see:

[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md)

For beta testing procedures, see:

[`../docs/BETA_TESTING.md`](../docs/BETA_TESTING.md)

---

## Important

The ADT SimHub Bridge interfaces with independently developed third-party software.

Changes to SimHub or AZOM can affect compatibility even when the bridge itself has not changed.

Live AZOM integration should therefore be treated as beta functionality.

**Never assume a hardware-related change succeeded unless ADT receives and verifies the corresponding live readback.**
