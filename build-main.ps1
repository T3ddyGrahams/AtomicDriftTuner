$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\AtomicDriftTuner\AtomicDriftTuner.csproj"
Write-Host "Building Atomic Drift Tuner..." -ForegroundColor Cyan
dotnet build $project -c Release
Write-Host "`nMain app build complete." -ForegroundColor Green
