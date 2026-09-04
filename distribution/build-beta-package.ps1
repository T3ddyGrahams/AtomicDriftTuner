param(
    [Parameter(Mandatory = $true)]
    [string]$SimHubPath,

    [string]$Version = "0.8.1-beta.1",

    [string]$InnoSetupPath = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo "artifacts"
$publish = Join-Path $artifacts "publish\win-x64"
$staging = Join-Path $artifacts "staging"
$output = Join-Path $artifacts "release"

Write-Host "Atomic Drift Tuner beta packaging" -ForegroundColor Cyan
Write-Host "Version: $Version"
Write-Host "SimHub build reference: $SimHubPath"

Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publish -ItemType Directory -Force | Out-Null
New-Item $staging -ItemType Directory -Force | Out-Null
New-Item $output -ItemType Directory -Force | Out-Null

Write-Host "`n[1/5] Publishing self-contained Windows x64 app..." -ForegroundColor Cyan
dotnet publish (Join-Path $repo "src\AtomicDriftTuner\AtomicDriftTuner.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $publish

Write-Host "`n[2/5] Building SimHub bridge..." -ForegroundColor Cyan
& (Join-Path $repo "bridge\build-bridge.ps1") -SimHubPath $SimHubPath

$bridgeDll = Join-Path $repo "bridge\AtomicDriftTuner.SimHubBridge\bin\Release\AtomicDriftTuner.SimHubBridge.dll"
if (-not (Test-Path $bridgeDll)) {
    throw "Bridge build completed without producing '$bridgeDll'."
}

Write-Host "`n[3/5] Staging tester payload..." -ForegroundColor Cyan
Copy-Item (Join-Path $publish "*") $staging -Recurse -Force

$bridgePayload = Join-Path $staging "BridgePayload"
New-Item $bridgePayload -ItemType Directory -Force | Out-Null
Copy-Item $bridgeDll (Join-Path $bridgePayload "AtomicDriftTuner.SimHubBridge.dll") -Force

Copy-Item (Join-Path $PSScriptRoot "README-BETA-TESTERS.md") (Join-Path $staging "README-BETA-TESTERS.md") -Force

$portable = Join-Path $output "AtomicDriftTuner-$Version-portable.zip"
Remove-Item $portable -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $portable -CompressionLevel Optimal
Write-Host "Portable package: $portable" -ForegroundColor Green

Write-Host "`n[4/5] Looking for Inno Setup..." -ForegroundColor Cyan
if ([string]::IsNullOrWhiteSpace($InnoSetupPath)) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $InnoSetupPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not [string]::IsNullOrWhiteSpace($InnoSetupPath) -and (Test-Path $InnoSetupPath)) {
    Write-Host "`n[5/5] Building installer..." -ForegroundColor Cyan
    & $InnoSetupPath `
        "/DMyAppVersion=$Version" `
        "/DRepoRoot=$repo" `
        (Join-Path $PSScriptRoot "AtomicDriftTuner.iss")

    Write-Host "Installer build complete. Check artifacts\release." -ForegroundColor Green
}
else {
    Write-Host "`n[5/5] Inno Setup not found; installer skipped." -ForegroundColor Yellow
    Write-Host "Install Inno Setup 6 and rerun this script, or pass -InnoSetupPath."
}

Write-Host "`nBeta distribution build finished." -ForegroundColor Green
