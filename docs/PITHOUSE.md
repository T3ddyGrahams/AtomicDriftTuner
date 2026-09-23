# MOZA Pit House — experimental support in preview.30

## Choose your wheelbase software

In **Setup & Paths**, choose **Car + FFB**, then select **SimHub / AZOM**, **MOZA Pit House**, or **Other wheelbase software / manual**. Click **Save & Continue**. Existing installations retain SimHub/AZOM by default, including their previous connection answers. Car tuning only does not require any FFB connection.

The choice changes the instructions and the **Wheelbase FFB / Wheelbase Settings** view. Selecting Pit House does not install it, load the SDK, connect to a wheelbase, or apply settings. SimHub is still usable for a dashboard; the selected FFB provider is separate from telemetry recording and the Content Manager companion.

## Start with manual entry

1. Save or screenshot your current Pit House profile.
2. Select your actual car, MOZA base and rim in ADT. Save Desired Behavior and generate an FFB recommendation.
3. Open **Wheelbase Settings**. Enter matching supported values in Pit House. Enter AC's FFB values separately in **Controls → Force Feedback**.
4. Keep unmatched controls fixed. Confirm the values actually in use, then record a baseline.

ADT's diagnosis, car tuning, gearing, calibration and FFB generation algorithms are unchanged. The adapter translates the supported output controls. Switching software starts a fresh guided baseline; old recordings, goals and reviews remain saved. Comparisons across different FFB providers are inconclusive. New Pit House tune snapshots contain generated, in-range core targets and AC FFB, not a claim that every AZOM feature was applied or a continuous hardware readback.

## Optional SDK connection — experimental

This is implemented and tested with a simulated wheelbase. **Actual Pit House / wheelbase acceptance is still pending.** The primary development PC did not have Pit House installed, and no vendor DLL or real wheelbase operation was executed during these checks.

Since preview.18, ADT runs SDK operations in a separate hidden helper process. If the SDK exits or stops responding, ADT displays a connection error and remains usable. An individual SDK request times out after 20 seconds. This contains native failures; it does not establish compatibility with a particular Pit House/SDK/firmware combination. During Read no settings are written. During Apply, an interrupted response can mean some settings changed: inspect Pit House and review the saved backup before another action. ADT never automatically retries an uncertain write.

On the tester's wheelbase PC:

1. Use **SDK-compatible MOZA Pit House**. The supplied MOZA SDK documentation specifically requires the SDK version; compatibility with an arbitrary regular Pit House version is not established.
2. Obtain the SDK from MOZA: [MOZA SDK archive](https://cdn.gudsen.vip/simulation_game/rs21repository/installer/MOZA_SDK.zip). Extract it outside ADT's application folder.
3. In ADT select `MOZA_SDK/SDK_CSharp/x64` for the packaged 64-bit build. The folder must contain `MOZA_API_C.dll` and `MOZA_SDK.dll` of matching architecture. A 32-bit ADT build would need the x86 folder. Vendor binaries are user supplied and are not redistributed in this ADT package. The Microsoft Visual C++ runtime and MOZA drivers may also be required by those binaries.
4. Start Pit House, power/connect the base, and verify it is detected there. Prevent AZOM or other tools from writing to the base during the test.
5. In ADT **Wheelbase Settings**, click **Read Pit House**. No setting writes occur during Read. Unreadable controls remain unavailable.
6. Review **current → proposed** values. Select only the settings to test. Confirm the detected device is your physical base and the car is stationary. The SDK supplies a device name, not a verified unique serial number.
7. Choose **Apply selected changes**. ADT rereads the selected source values, saves original values locally, applies the selection, then checks individual and final readback. A stale preview, changed value/device, unsupported range, missing backup or provider change blocks further writes. Preview expiry is two minutes.
8. Check the values in Pit House before driving. A failed or uncertain result can mean some settings changed: do not assume rollback or immediately retry. Read again, inspect Pit House, and use **Review backup to restore** if needed.

## Supported core controls

| Control | SDK range |
|---|---:|
| Game FFB strength | 0–100% |
| Maximum output torque | 50–100% |
| Maximum wheel speed | 10–100% |
| Natural damping | 0–100% |
| Natural friction | 0–100% |
| Natural inertia | 100–500 |
| Spring strength | 0–100% |
| Steering wheel inertia ratio | 100–4000 |
| Speed-dependent damping | 0–100% |
| Speed damping start | 0–400 km/h |

ADT never silently clamps an out-of-range target. In particular, AZOM's wheel-speed range is wider. Steering limits, road sensitivity/EQ presets and bands, FFB curves, interpolation, game effects, soft limits, vibration and preference toggles are not sent by this adapter. No motor movement, reboot or synthetic force-effect APIs are used.

Backups are under `%LOCALAPPDATA%/AtomicDriftTuner/PitHouseBackups`. Each file contains the original and intended values for the selected controls. **Review backup to restore** reads current settings and shows original values as a new preview. Select rows and explicitly Apply to restore them; restoration itself gets a new backup and readback verification. Backups may include a batch that stopped partway through. They are recovery evidence, not proof the batch succeeded.

Pit House operations are desktop-only in this preview. Touchscreen AZOM controls are disabled while Pit House/manual is selected; recording and car setup controls remain available. Cached AZOM windows also cannot write after a provider switch. Do not run multiple ADT builds or separate tuning tools as simultaneous writers.

## First hardware acceptance test

Record the ADT, Pit House, SDK and firmware versions, base/rim, and each result:

- Select each provider, save, reopen, and confirm the right guide and controls appear.
- With Pit House closed or the base disconnected, Read must show an actionable error without applying anything.
- Read the connected base; compare every available value against Pit House.
- At rest, test one small selected change, verify ADT and Pit House agree, then review its backup and restore it.
- Repeat Read after unplug/reconnect, then test a fresh preview. Never continue using an old preview after a device swap.
- Confirm unselected settings, hands-off protection, reversal, steering limits and EQ remain as expected in Pit House.
- Switch back to SimHub/AZOM and verify its established workflow; new recordings should identify the chosen provider.

Record the exact hardware/software combination and actions tested. This preview does not claim verified support for every MOZA base or Pit House version.
