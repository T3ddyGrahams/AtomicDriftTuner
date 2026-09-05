param(
    [Parameter(Mandatory = $true)]
    [string]$SimHubPath,
    [string]$InnoSetupPath = ""
)

$ErrorActionPreference = "Stop"
$version = "0.8.1-beta.1"

Write-Host "Building ADT $version GitHub release assets..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "distribution\build-beta-package.ps1") `
    -SimHubPath $SimHubPath `
    -Version $version `
    -InnoSetupPath $InnoSetupPath

Write-Host "`nRelease assets are in artifacts\release" -ForegroundColor Green
Write-Host "Expected portable asset: AtomicDriftTuner-$version-portable.zip"
Write-Host "Expected installer asset (when Inno Setup 6 is installed): AtomicDriftTuner-$version-setup.exe"
