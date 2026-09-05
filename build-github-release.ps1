param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$SimHubPath,

    [string]$InnoSetupPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Building ADT $Version GitHub release assets..." -ForegroundColor Cyan

& (Join-Path $PSScriptRoot "distribution\build-beta-package.ps1") `
    -SimHubPath $SimHubPath `
    -Version $Version `
    -InnoSetupPath $InnoSetupPath

Write-Host "`nRelease assets are in artifacts\release" -ForegroundColor Green
Write-Host "Expected portable asset: AtomicDriftTuner-$Version-portable.zip"
Write-Host "Expected installer asset (when Inno Setup 6 is installed): AtomicDriftTuner-$Version-setup.exe"
