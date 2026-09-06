# ADT regression checks

Run from the repository root on Windows with the .NET SDK installed:

```powershell
dotnet run --project tests/AtomicDriftTuner.RegressionTests -c Release
```

This dependency-free console suite exits nonzero on failure. It uses generated files in a unique temporary directory and anonymous simulated shared-memory maps. It does not connect to or write to a wheelbase, start ADT's UI, or edit the user's saved settings/calibrations. Reflection isolates existing storage implementations without changing their production constructors. Temporary fixture locations are printed for inspection.

Coverage: telemetry recovery/freshness, session preservation, behavior storage and identity, setup baseline consistency, saved-tune and portable-share round trips, calibration backup recovery, and the AZOM stale-source guard. An output-range sweep covers all 8,000 built-in hardware/wheel/car/intent combinations; it is not a driving-quality test.

Other checks:

```powershell
dotnet build src/AtomicDriftTuner/AtomicDriftTuner.csproj -c Release
dotnet build bridge/AtomicDriftTuner.SimHubBridge/AtomicDriftTuner.SimHubBridge.csproj -c Release -p:SimHubInstallPath=E:\SimHub
node share-api/selftest.js
```

Use the actual SimHub installation path on other machines. The share API self-test launches an isolated localhost service and uses temporary registry data. If a constrained Windows environment prevents Node from resolving ancestor directories, set `NODE_OPTIONS=--preserve-symlinks --preserve-symlinks-main` for that test process.
