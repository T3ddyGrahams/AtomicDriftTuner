# ADT Companion — first preview

Recording controls, AC telemetry status and **Your Next Step** inside Assetto Corsa.

Requires desktop **ADT 0.9.0-preview.4 or newer** running on the same PC, and Custom Shaders Patch with Lua app support. The public ADT preview 3 does not have these companion endpoints. No SimHub/AZOM installation is needed for AC telemetry recording.

## Install with Content Manager

1. Keep the companion ZIP intact and drag it into Content Manager. Review the installation entry for **ADT Companion** and install it. This package contains `apps/lua/ADTCompanion`.
2. If your CM version does not recognize the archive, extract its `apps` folder into your Assetto Corsa installation folder (the folder containing `acs.exe`). This adds `apps/lua/ADTCompanion`; it does not replace car files.
3. Ensure CSP is active. Enter an AC session and open **ADT Companion** from the in-game apps sidebar. Exact Lua app menus vary by CM/CSP version.

## Prepare once on the PC

1. Start desktop ADT preview 4 and select your car, rig and driver. Follow **Your Next Step** to prepare the baseline or recommendation test.
2. Open **Telemetry Recorder**. Enter your driver and the conditions/driving task. Attach the setup used if available and confirm tune use only if those settings are actually in use. These details are captured when recording starts.
3. Open **ADT Remote**, press **START**, and leave ADT running. You can minimize ADT. You do not need to enable remote AZOM writes.

## Record from inside the game

1. In **ADT Companion**, enter the Remote port (normally `5190`) and its six-digit pairing code. Click **Pair with ADT**. The token stays in memory and must be paired again after ADT Remote restarts or its code is regenerated.
2. Confirm the panel shows the intended recorder car/driver and **AC TELEMETRY: LIVE**. Click **Start recording**.
3. Drive your run, then click **Stop recording** and **Save session**. ADT saves the same JSON/CSV files as its desktop recorder.
4. Read **Your Next Step**. Use desktop ADT for analysis, setup attachments, tune-use confirmation, baseline selection and comparison. If you changed settings, update those details before the next recording.

## What the messages mean

- **Connected to desktop ADT**: the companion can communicate with ADT; it does not mean AC telemetry is live.
- **AC TELEMETRY: WAITING / STALE**: telemetry is missing or no longer fresh. Enter a live AC session. Interrupted recordings are retained for review and can be saved when analysis succeeds.
- **Prepare in ADT**: the desktop recorder has not been opened, or its car/rig/driver context needs updating. Preparing the recorder does not automatically apply a tune.
- **Unsaved**: save the stopped run before starting another. The companion never discards a recording.
- **Outcome unknown / reconnecting**: an action response was lost. The panel refreshes authoritative state; it never automatically repeats a recording command.

Disconnecting or hiding this panel does not stop a recording in ADT. Keep desktop ADT running and use its recorder if the in-game connection is unavailable. This first preview displays the next step; its confirmation/action buttons remain in desktop ADT.

## Validation status

The transport/client tests and desktop recorder tests cover pairing, malformed requests, stale commands, overlap, timeouts, disk-save failure/retry, and duplicate saves. The Lua source has been compiled and exercised under Lua 5.1 and checked against the installed CSP app SDK. Content Manager drag-and-drop installation, actual CSP rendering and live-driving operation still need hands-on validation. No claim of verified compatibility across CSP versions is made yet.

Report the desktop ADT version, companion version, CM/CSP versions, the operation attempted and the panel's error text. Do not share pairing codes/tokens.
