# v0.8.1-updates-test.2 — Validation Notes

Static/package validation performed in the build environment:

- all WPF XAML parses as XML;
- current app/distribution metadata is `0.8.1-updates-test.2`;
- Updates window + UpdateService integration is present;
- GitHub API source is hard-coded to `T3ddyGrahams/AtomicDriftTuner`;
- update asset downloads require HTTPS and the official GitHub Releases path;
- partial downloads use `.part` cleanup and final files receive local SHA-256;
- no automatic installer execution/self-replacement path was added;
- Share Codes Phase 1 remains present;
- protected tuning/telemetry/AZOM/remote/bridge files remain byte-for-byte unchanged
  from Share Test 1 except version-facing UI files where applicable;
- bridge remains v0.7.2.

This environment does not contain the Windows .NET/WPF SDK. Perform
**Build → Rebuild Solution** in Visual Studio 2022 before live testing.
