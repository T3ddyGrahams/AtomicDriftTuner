param(
    [string]$SimHubPath = "C:\Program Files (x86)\SimHub\"
)

$ErrorActionPreference = "Stop"

$project = Join-Path `
    $PSScriptRoot `
    "AtomicDriftTuner.SimHubBridge\AtomicDriftTuner.SimHubBridge.csproj"

$simHubPluginsDll = Join-Path $SimHubPath "SimHub.Plugins.dll"
$log4netDll = Join-Path $SimHubPath "log4net.dll"

if (-not (Test-Path $SimHubPath)) {
    throw "SimHub folder was not found at '$SimHubPath'. Pass the correct folder with -SimHubPath."
}

if (-not (Test-Path $simHubPluginsDll)) {
    throw "SimHub.Plugins.dll was not found at '$simHubPluginsDll'. Pass the actual SimHub install folder with -SimHubPath."
}

if (-not (Test-Path $log4netDll)) {
    throw "log4net.dll was not found at '$log4netDll'. Pass the actual SimHub install folder with -SimHubPath."
}

Write-Host "Building ADT SimHub Bridge against:" -ForegroundColor Cyan
Write-Host $SimHubPath

dotnet build `
    $project `
    -c Release `
    -p:SimHubInstallPath="$SimHubPath"

if ($LASTEXITCODE -ne 0) {
    throw "ADT SimHub Bridge build failed with exit code $LASTEXITCODE."
}

$outputDll = Join-Path `
    $PSScriptRoot `
    "AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll"

if (-not (Test-Path $outputDll)) {
    throw "Bridge build completed without producing '$outputDll'."
}

Write-Host "`nADT SimHub Bridge build complete." -ForegroundColor Green
Write-Host "Output: $outputDll"
