# Atomic Remote / iPhone Companion

Version: **v0.8.0-beta.1**

This beta adds a local-network mobile companion to the existing
Atomic Drift Tuner Windows application. It is intentionally implemented as a
mobile web app served by the Windows process so it can be tested immediately
from Safari on an iPhone without an App Store/native-iOS build.

## Architecture

```text
iPhone / Safari
      |
      | local private LAN only
      v
Atomic Remote HTTP API (inside AtomicDriftTuner.exe)
      |
      +--> current Atomic tune context
      +--> Assetto Corsa shared-memory telemetry
      +--> Atomic SimHub Bridge / AZOM readback
      |
      `--> existing AzomLiveController write pipeline
             - one live batch at a time
             - known range validation
             - exact AZOM commit first
             - duplicate/rate guards remain in the bridge/client
             - live readback verification
             - stop on first unverified write
```

The phone never talks directly to SimHub, AZOM, MOZA software, or the wheelbase.
Windows Atomic remains authoritative.

## What the beta can do

- Start/stop an Atomic local-network server from the Windows UI.
- Pair a browser using a six-digit one-time/session code.
- Issue a strong bearer token after successful pairing.
- Reject clients outside loopback/private IPv4/IPv6 ranges.
- Show current Atomic wheelbase, wheel, pack, car, intent and generated tune scores.
- Read live Assetto Corsa physics telemetry from the Windows app.
- Show speed, slip angle, steering angle, FFB output and simple drift detection.
- Read selected live AZOM core/wheelbase settings through the existing Atomic SimHub Bridge.
- Optionally request a limited set of AZOM numeric changes from the phone.
- Verify each remote AZOM change through the existing Atomic guarded write/readback pipeline.
- Revert the last remote change from the current Atomic run.
- Refresh an already-open Full AZOM Settings window after a remote change.
- Automatically follow the active Assetto Corsa car and inferred drift pack.
- Change the Windows Drift Target / session intent.
- Request Windows Atomic to Generate Tune without auto-applying it.
- Review generated AZOM/MOZA and AC FFB recommendations on the phone.
- Edit/save per-car Desired Behavior targets and presets.

## Deliberate beta limits

Remote writes are **OFF by default** every time the server starts.

The remote write allow-list is currently limited to:

- Game FFB Strength: 0..100%
- Base Torque Output: 50..100%
- Wheel Rotation Angle: 60..2700 degrees
- Maximum Wheel Speed: 0..200%
- Interpolation: 0..10
- Wheel Damper: 0..100%
- Wheel Friction: 0..100%
- Natural Inertia: 100..500
- High-Speed Damping: 0..100%
- High-Speed Trigger: 0..400 kph

Preferences, toggles, EQ bands, FFB curve nodes, AC setup writes and arbitrary API
commands are intentionally not exposed remotely in this first test.

## Pairing / security model

1. The server is stopped by default.
2. Start it from **REMOTE / IPHONE TEST**.
3. Atomic listens on the selected port (default `5190`) on the local machine.
4. Atomic only accepts loopback/private-network client addresses.
5. Safari opens the LAN address shown by Atomic.
6. Enter the six-digit pairing code shown in the Windows window.
7. Atomic returns a random 256-bit session token.
8. The browser stores that token locally and sends it in `X-Atomic-Token` for API requests.
9. Five failed pairing attempts cause a 30-second pairing lockout.
10. Pairing credentials rotate whenever the server starts or the user clicks **NEW PAIRING CODE**.
11. Remote AZOM writes require a second, explicit Windows-side opt-in and reset to OFF on server start.

This is a **same-LAN beta**, not an Internet-facing remote-access design. Do not port-forward
the Atomic Remote port or expose it directly to the public Internet.

## Windows + iPhone setup

1. Build/run Atomic normally in Visual Studio.
2. Start SimHub and AZOM if live AZOM read/write testing is desired.
3. Open **REMOTE / IPHONE TEST** in Atomic.
4. Leave remote writes OFF initially.
5. Click **START REMOTE**.
6. If Windows Firewall prompts, allow the app only on the private network used by the rig/iPhone.
7. Put the iPhone on the same LAN/Wi-Fi as the Windows PC.
8. Open the first address shown by Atomic in Safari, for example `http://192.168.1.50:5190/`.
9. Enter the six-digit pairing code.
10. Confirm the phone displays the correct Atomic hardware/car context.
11. Start an Assetto Corsa driving session and confirm live telemetry moves on the phone.
12. Confirm AZOM readback appears when SimHub/AZOM/Atomic Bridge are available.
13. Only after read-only testing succeeds, enable **Allow remote AZOM writes for this Atomic run** on Windows.
14. Test one conservative setting change on the phone.
15. Confirm the phone reports verified readback and the Windows Full AZOM window refreshes if it is open.
16. Test **REVERT LAST REMOTE CHANGE**.

## iPhone Home Screen

Once paired in Safari, use Safari's Share menu and **Add to Home Screen** if desired.
This beta does not include an offline service worker or native iOS package; the
Windows Atomic server must be running and reachable on the LAN.

## Bridge compatibility

This app-side remote feature does **not** change the Atomic SimHub Bridge implementation.
The required bridge remains **v0.7.2**.
