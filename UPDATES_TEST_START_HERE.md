# Atomic Drift Tuner v0.8.1-updates-test.2 — START HERE

This test is built on top of Share Codes Phase 1. Share Codes Phase 2 / Oracle
short-code API work is paused, not discarded.

## What to test

1. Extract into a fresh folder.
2. Open `AtomicDriftTuner.sln` in Visual Studio 2022.
3. **Build → Rebuild Solution**.
4. Press **F5**.
5. Confirm normal v0.8 functionality and Share Codes Phase 1 still open normally.
6. Click **UPDATES** on the main window.
7. Leave **Include public beta / pre-release versions** enabled.
8. Click **CHECK FOR UPDATES**.
9. Confirm the window loads the latest matching GitHub Release from
   `T3ddyGrahams/AtomicDriftTuner` and displays its release notes.
10. Because this is a `0.8.1` development build and the current public release is
    expected to be `v0.8.0-beta.1`, the status should normally explain that the
    development build is newer than the latest public release.
11. Confirm the published installer and portable ZIP are detected if they are
    attached to the release.
12. Download one asset to a temporary/test folder.
13. Confirm download progress reaches 100%, the final file exists, and Atomic
    displays a SHA-256 value.
14. Confirm Atomic does **not** launch the installer or close/restart itself.
15. Disable the pre-release checkbox and check again. If no stable release exists,
    Atomic should say no stable release was found rather than treating a beta as stable.

## Safety checks

- Update checks are manual only in this test.
- No telemetry, settings, calibration, credentials, or local files are uploaded.
- Only official HTTPS GitHub release asset URLs are accepted.
- A failed/canceled download should remove its `.part` file.
- The updater never writes AZOM, SimHub, the Atomic bridge, Assetto Corsa files,
  or the running Atomic executable.
- Required bridge remains v0.7.2; no bridge rebuild/reinstall is needed.
