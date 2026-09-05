param(
    [string]$SimHubPath = "C:\Program Files (x86)\SimHub\"
)

$ErrorActionPreference = "Stop"

$source = Join-Path `
    $PSScriptRoot `
    "AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll"

$simHubExe = Join-Path $SimHubPath "SimHubWPF.exe"
$dest = Join-Path $SimHubPath "AtomicDriftTuner.SimHubBridge.dll"

if (-not (Test-Path $SimHubPath)) {
    throw "SimHub folder was not found at '$SimHubPath'. Pass the correct folder with -SimHubPath."
}

if (-not (Test-Path $source)) {
    throw "Bridge DLL has not been built yet. Run .\build-bridge.ps1 first."
}

if (-not (Test-Path $simHubExe)) {
    throw "SimHubWPF.exe was not found at '$simHubExe'. Pass the actual SimHub install folder with -SimHubPath."
}

if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) {
    throw "SimHub is currently running. Close SimHub before installing or updating the bridge, then run this script again."
}

Write-Host "Installing ADT SimHub Bridge..." -ForegroundColor Cyan
Write-Host "Source:      $source"
Write-Host "Destination: $dest"

Copy-Item `
    $source `
    $dest `
    -Force

if (-not (Test-Path $dest)) {
    throw "Bridge installation failed. The destination DLL was not created."
}

$sourceHash = (Get-FileHash $source -Algorithm SHA256).Hash
$destHash = (Get-FileHash $dest -Algorithm SHA256).Hash

if ($sourceHash -ne $destHash) {
    throw "Bridge installation verification failed. Source and destination SHA-256 hashes do not match."
}

Write-Host "`nADT SimHub Bridge installed and verified." -ForegroundColor Green
Write-Host "Installed: $dest"
Write-Host "SHA-256:   $destHash"

Write-Host "`nStart SimHub, enable 'Atomic Drift Tuner Bridge' under Settings > Plugins, and restart SimHub once if prompted."
