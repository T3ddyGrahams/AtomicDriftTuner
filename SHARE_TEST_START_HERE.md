# Atomic Drift Tuner v0.8.1-share-test.1 — START HERE

This is a **development test**, not a replacement public GitHub release.

Do not copy this entire folder over the current GitHub repository. The published
repo now also contains the Discord bot and other files that are not part of this
app-focused test package.

## What this test adds

- `ATOMIC SHARE CODES (TEST)` button in the Windows app
- portable `AT1-...` share codes
- copyable human-readable tune preview
- decode/review before import
- import context + regenerate locally
- optional Desired Behavior import, OFF by default
- strict schema/range/size validation
- no direct hardware write from a share-code import

## What this test deliberately does NOT add yet

- no public Atomic Share API
- no database
- no short server IDs like `AT-7K4D2P` yet
- no `/atomic tune` Discord lookup yet
- no Discord bot changes
- no bridge changes

The goal is to freeze and validate the portable payload format before the server
and Discord accept it.

## Build

1. Extract to a fresh folder.
2. Open `AtomicDriftTuner.sln` in Visual Studio 2022.
3. **Build → Rebuild Solution**.
4. If the rebuild is clean, press **F5**.

No SimHub Bridge rebuild/reinstall is required. Required bridge remains v0.7.2.

## Functional test

1. Launch Atomic normally.
2. Confirm existing v0.8 behavior first:
   - installed-car scanning works;
   - active AC car auto-detection works;
   - drift-pack inference works;
   - telemetry/remote still work.
3. Pick a car and Drift Target.
4. Click **ATOMIC SHARE CODES (TEST)**.
   - Atomic regenerates the current context first so the code cannot contain a stale result.
5. Confirm the **CREATE / COPY** tab shows:
   - current car/pack/hardware/target;
   - recommendation snapshot;
   - an `AT1-...` code;
   - code length no greater than 2,000 characters.
6. Click **COPY SHARE CODE**.
7. Open the **IMPORT** tab.
8. Paste the same code and click **DECODE + REVIEW**.
9. Confirm the decoded preview matches the source tune.
10. Change the main Atomic selections to something obviously different.
11. Return to the Share Codes window and click **LOAD CONTEXT + REGENERATE LOCALLY**.
12. Confirm:
    - hardware/wheel/pack/car/Drift Target change back to the shared context;
    - Atomic generates a new local result;
    - summary says `imported AT1 share context (regenerated locally)`;
    - **nothing is applied to AZOM**.
13. Repeat once with **Also save the shared Desired Behavior** enabled.
14. Open the AC Car Setup Tuner for that car and verify the imported behavior target is available.

## Tamper tests

These are expected to fail safely:

- delete a few characters from the end of the code;
- change `AT1-` to some unrelated prefix;
- paste ordinary text;
- paste more than 2,000 non-whitespace characters.

Atomic should show an invalid-code message and keep **LOAD CONTEXT + REGENERATE LOCALLY** disabled.

## Share-code privacy test

A decoded payload should never contain:

- `C:\Users\...`
- `SourceFolderPath`
- telemetry samples/sessions
- calibration history
- Discord tokens
- Atomic Remote pairing/browser tokens
- `.env` content
- SimHub credentials
- AZOM Bluetooth/LED/standby/protection preferences

The AC car **folder name** may be present by design for matching the same car on
another user's installation.

## After this passes

Phase 2 will use the same validated `atomic-share/v1` payload on the Oracle
server:

```text
Atomic Windows
    ↓
Atomic Share API
    ↓
AT-7K4D2P
    ↓
Discord /atomic tune code:AT-7K4D2P
```

Windows remains the only authority for hardware writes.
