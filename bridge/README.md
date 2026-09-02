# Atomic Drift Tuner SimHub Bridge

This is an **optional, isolated SimHub plugin**. The normal Atomic Drift Tuner application does not reference SimHub assemblies and continues to build/run without this bridge.

The bridge is intentionally read-only: it reads AZOM's exposed SimHub properties and sends a snapshot to Atomic through a local named pipe. Atomic performs writes using SimHub's documented `SimHubWPF.exe -triggeraction ActionName` command-line interface. This avoids copying or calling AZOM internals.

## Build/install

1. Install SimHub (default path `C:\Program Files (x86)\SimHub\`).
2. Open `AtomicDriftTuner.SimHubBridge.sln` in Visual Studio 2022.
3. Build **Release**.
4. Copy `AtomicDriftTuner.SimHubBridge.dll` from `AtomicDriftTuner.SimHubBridge\bin\Release\` into the SimHub install folder.
5. Restart SimHub.
6. In SimHub **Add/remove features**, enable **Atomic Drift Tuner Bridge** if it is not enabled automatically.
7. Ensure AZOM is enabled and your MOZA base is connected.
8. In Atomic Drift Tuner: **Full AZOM Settings → Live AZOM → Read Live AZOM**.

If SimHub is installed elsewhere, set the MSBuild property `SimHubInstallPath` or edit the default in the bridge `.csproj`.

## Why a separate project?

SimHub hosts plugins as x86 .NET Framework 4.8 DLLs, while Atomic Drift Tuner is a modern .NET 8 WPF desktop app. Keeping the bridge separate prevents SimHub dependencies from destabilizing the main tuner build.
\n## Easier PowerShell build/install\n\nFrom this `bridge` folder:\n\n```powershell\n.\\build-bridge.ps1\n.\\install-bridge.ps1\n```\n\nIf SimHub is in a custom folder:\n\n```powershell\n.\\build-bridge.ps1 -SimHubPath "D:\\SimHub\\"\n.\\install-bridge.ps1 -SimHubPath "D:\\SimHub\\"\n```\n\nClose SimHub before running the install script.\n

### Current SimHub dependency note

The bridge references `log4net.dll` from the SimHub installation because types exposed by `SimHub.Plugins.dll` depend on log4net. Do not add a separate NuGet log4net package unless you intentionally want to manage SimHub SDK version compatibility yourself.
