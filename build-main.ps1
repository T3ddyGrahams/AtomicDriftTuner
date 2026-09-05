$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "src\AtomicDriftTuner\AtomicDriftTuner.csproj"

Write-Host "Building ADT..." -ForegroundColor Cyan

dotnet build $project -c Release

Write-Host "`nADT main app build complete." -ForegroundColor Green
