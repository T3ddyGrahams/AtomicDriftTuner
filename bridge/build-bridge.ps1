param(
    [string]$SimHubPath = "C:\Program Files (x86)\SimHub\"
)
$ErrorActionPreference = "Stop"
if (-not $SimHubPath.EndsWith("\\")) { $SimHubPath += "\\" }
$project = Join-Path $PSScriptRoot "AtomicDriftTuner.SimHubBridge\AtomicDriftTuner.SimHubBridge.csproj"
$dll = Join-Path $SimHubPath "SimHub.Plugins.dll"
$log4net = Join-Path $SimHubPath "log4net.dll"
if (-not (Test-Path $dll)) {
    throw "SimHub.Plugins.dll was not found at '$dll'. Pass your SimHub folder with -SimHubPath."
}
if (-not (Test-Path $log4net)) {
    throw "log4net.dll was not found at '$log4net'. Pass the actual SimHub install folder with -SimHubPath."
}
Write-Host "Building Atomic Drift Tuner SimHub Bridge against $SimHubPath" -ForegroundColor Cyan
dotnet build $project -c Release -p:SimHubInstallPath="$SimHubPath"
Write-Host "`nBridge build complete." -ForegroundColor Green
Write-Host "Output: $(Join-Path $PSScriptRoot 'AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll')"
