param(
    [string]$SimHubPath = "C:\Program Files (x86)\SimHub\"
)
$ErrorActionPreference = "Stop"
if (-not $SimHubPath.EndsWith("\\")) { $SimHubPath += "\\" }
$source = Join-Path $PSScriptRoot "AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll"
$dest = Join-Path $SimHubPath "AtomicDriftTuner.SimHubBridge.dll"
if (-not (Test-Path $source)) {
    throw "Bridge DLL has not been built yet. Run .\\build-bridge.ps1 first."
}
if (-not (Test-Path (Join-Path $SimHubPath "SimHubWPF.exe"))) {
    throw "SimHubWPF.exe was not found in '$SimHubPath'."
}
if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) {
    throw "SimHub is currently running. Close SimHub before installing/updating the bridge, then run this script again."
}
Copy-Item $source $dest -Force
Write-Host "Installed: $dest" -ForegroundColor Green
Write-Host "Start SimHub, enable 'Atomic Drift Tuner Bridge' under Settings > Plugins, and restart SimHub once if prompted."
