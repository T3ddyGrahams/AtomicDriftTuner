# Gearing planner

The final-drive planner is a separate step in **AC Setup → Gearing • Final-drive planner**. The current development build adds read-only support for ordinary packed `data.acd` files alongside unpacked data. Public beta `0.9.0-preview.17` has the earlier unpacked-only planner; packed support requires the updated build.

## Try it

1. Select your installed car in ADT. Save a setup for that car in Assetto Corsa.
2. Open **AC Setup**, choose the baseline, then open **Gearing**. If you already generated handling changes, save those first and use that generated setup as the gearing baseline.
3. Enter the gear you want to hold, your minimum/maximum road speed, and your desired engine RPM range. Choose km/h or mph. The initial numbers are examples, not a detected power band.
4. Click **Calculate final drive**. Review the decoded current/suggested ratios, predicted RPM at both speed endpoints, and the reason for the choice. The source text identifies packed or unpacked data and the saved selections used. Expand the supported-ratio list to compare alternatives.
5. **Save target for this car** remembers your choices. **Save gearing setup…** writes a separate `.ini`, changing only `[FINAL_RATIO] VALUE`. It is disabled when the current ratio is already the best match.
6. Load that new setup in Assetto Corsa's Setup menu. Test the same section, gear and driving conditions against your baseline. Watch for limiter contact or dropping below the usable RPM range; adjust your target and repeat.

There is no automatic in-game application. Saving a target does not change a setup. Saving a gearing setup preserves the source setup and every unrelated setting.

For a supported packed car, no manual Content Manager unpacking is needed. ADT reads it into a bounded private memory cache and leaves the installed `data.acd` intact. It does not create a `data` folder, run mod scripts, or redistribute the extracted physics. If reading or mapping cannot be verified, the planner explains the missing or unsupported evidence.

## What the estimate means

The planner uses the selected gear, decoded supported final-drive ratios, nominal driven-tyre radius and the base limiter from `engine.ini`. It predicts RPM from road speed with **no tyre or clutch slip**:

`RPM = (km/h ÷ 3.6) ÷ (2π × tyre radius in metres) × 60 × gear ratio × final drive`

Ratios that hit the base limiter within the requested speed range are excluded. Ranking prefers ratios whose endpoints fit inside the requested RPM band, then minimizes the squared proportional mismatch to the two requested RPM endpoints. Equal/negligibly different results keep the existing ratio. If no ratio spans the entire RPM band, the result explicitly says it is a partial fit. If all ratios reach the base limiter, change the gear or speed target. An ECU, CSP controller or script may change the active limiter; that behavior is not simulated here. Confirm the usable RPM range in game.

A numerically larger final drive is shorter: it increases engine RPM and torque multiplication in every gear. A smaller ratio is taller. This describes gearing multiplication, not a prediction of available traction or delivered engine torque. Drift wheelspin, clutch slip, tyre deformation and custom physics can materially change RPM. Version 1 does not diagnose gearing from telemetry, infer an engine power band, optimize individual gears, or score improvement from runs.

Gearing targets are saved separately from the seven existing Desired Behavior handling goals. The handling and telemetry algorithms are unchanged. Existing run/tune history can record a saved setup through the normal workflow; the gearing planner itself does not claim that a recommendation improved a run.

## Supported car data

- One complete source: ordinary supported `data.acd`, or an unpacked `data` folder. Both may be present when ADT verifies that all supported INI/LUT/RTO/Lua file sets and bytes match exactly. It must supply readable `setup.ini`, `drivetrain.ini`, `tyres.ini`, `engine.ini` and referenced local ratio files.
- RWD and FWD; fixed gears, explicitly selected gearsets, and individually adjustable gears whose selected ratio indexes can be decoded.
- A saved setup with matching `[CAR] MODEL`, `[FINAL_RATIO] VALUE`, and `[TYRES] VALUE`; gearsets also require `[GEARSET] VALUE`. Individually adjustable forward gear `n` requires its saved `INTERNAL_GEAR_(n+1)` selection because AC's internal numbering includes reverse.
- Fixed engine RPM limiter and a resolvable driven-tyre compound/radius.

Packed does not automatically mean protected. Ordinary supported AC archive records can be read; additional protection, unknown archive layouts, binary or otherwise unreadable physics remain unsupported. Conflicting packed/unpacked copies, AWD, explicit adjustable limiters, duplicate ratio labels, malformed or incomplete files, and unresolved selections receive an explanation instead of a guessed recommendation. Verified matching copies are shown in the source explanation; ADT uses the complete packed snapshot without inferring which source the game selected. Referenced files must be flat files inside the selected source; external paths and scripts are not followed or executed. Keep the installed car intact.

Reading is bounded: at most 1 MiB per car-data file, 16 MiB of decoded data, 64 MiB per archive and 1,024 entries. A renamed packed-car folder can make the archive unreadable because the ordinary format depends on the car identifier. An accepted saved-value mapping does not prove the setup was loaded in game, validate every CSP override, or establish that the tune will feel better.

Saving separately rechecks the baseline fingerprint and the source snapshot, reloads the definitions, recomputes the recommendation, and uses the existing guarded setup writer. Packed evidence covers the entire archive. Unpacked evidence covers the supported INI/LUT/RTO/Lua files, including ratio lists. Verified matching copies fingerprint and recheck both sources, even when the archive is cached. Changed files, a missing source or newly conflicting copies require recalculation. Fingerprints do not monitor external CSP files or prove active in-game physics. Targets persist under `%LOCALAPPDATA%/AtomicDriftTuner/gearing-targets/`, keyed by the same car/pack identity used for behavior profiles.

## Gearing & ECU run review (local preview.27)

1. Open **AC Setup → Gearing**. Choose your actual drift gear, speed range and preferred RPM range, then **Save target for this car**. The example RPM range is not a detected power band.
2. Prepare a fresh recording with the correct car and setup. Confirm the setup in use, record the same section with normal inputs, stop and save. The run retains its target, readable gear definitions and supported ECU curve; later changes do not rewrite it.
3. Open **Tuning Assistant → Gearing & ECU**. The first view explains what the run shows and the next test. Expand the measurements for typical RPM/speed by gear, drifting portions of initiation/transition, and individual input episodes.
4. For a gearing test, keep ECU fixed. Use the existing final-drive planner to review an option and save a separate test setup. Load or explicitly apply that setup in the pits, confirm it in the recorder, then drive the same section again. The evidence review itself never changes a tune.
5. For an ECU test, return to the same gearing and change only the ECU selection in the pits. Confirm the new setup, record again and choose the original run in **Before / After**. Return to **Gearing & ECU** and expand its comparison. Compare speed retained and control alongside RPM, and repeat before drawing conclusions.

Typical ranges are the time-weighted 10th–90th percentiles, not single peaks. Below/above-target seconds use only the saved gear and speed window. A concrete gearing test needs reliable telemetry, at least 20s of usable forward RPM evidence, at least 10s in the target gear, verified recorded final/gear ratios, known extended and travel-direction signals, and confirmed car/setup identity. Repeated patterns require at least three same-gear episodes totaling 3s; an episode lasts at least 0.5s without a large raw clutch change. Low-RPM and near-limiter hypotheses require high throttle and little braking. These provisional thresholds identify a test, not insufficient engine power or actual limiter activation.

The near-limiter column means RPM at or above 97% of the recorded **base** limiter definition. Adjustable `ENGINE_LIMITER`/`LIMITER` controls make this reference unavailable for that measurement; the planner also refuses their unsupported active value. ECU, turbo, controllers or scripts can alter behavior beyond the base definition.

Supported ECU mappings retain their ordered RPM/torque-multiplier curve. The review looks up configured values only inside its recorded range, using linear interpolation and no extrapolation. It does not measure horsepower, reconstruct delivered torque or verify that a map was active. Unnamed VALUE fields, nonmatching LUT indexes and unsupported scripts remain unknown. In particular, older S13 test files with unnamed ECU values cannot establish which map caused a change. No new ECU writer is included.

Old recordings can show raw RPM but lack these new snapshots. Today's physics and targets are never backfilled into them. The new comparison is descriptive and does not enter the existing improvement scoring or recommendation-credit logic; those separate audit findings remain open. Existing handling, pedal, angle and FFB calculations are preserved. The new detailed analysis is skipped during live recording-readiness checks.

## Verification

- Synthetic regressions cover non-monotonic ratio lists, gearsets, adjustable gears, tyre compound/drive axle, invalid inputs, limiter exclusion, partial/no-op results, stale data, wrong-car baselines, source preservation, single-parameter exports and per-car persistence.
- Packed fixture checks cover the same saved-index mapping, gearsets, individual-gear numbering and driven compounds; they also reject changed archives, changed baselines, newly ambiguous sources and invalid references. The export check compares original archive bytes and confirms no installed `data` folder or other file was created.
- WPF checks exercise real calculation handlers, stale-result invalidation, unit switching, persistence, themed text/buttons and narrow/portrait/ultrawide controls. They use isolated fixtures.
- Read-only installed-car check: `687_Bros_Toyota_GR_86_drift` with its existing `generic/last.ini` successfully decoded the fixed third gear, current final drive and selected tyre. The test export was written to a temporary test folder, not the game or user setup folder.
- `bdb_nissan_laurel_c33` was correctly blocked because two final-drive entries reuse the same label. No mapping was guessed and no car data was edited.
- Live driving and subjective before/after improvement remain for the driver to test.

Developer reference: Content Manager's [CarSetupEntry](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs) maps ratio selections to zero-based indexes; [RtoDataFile](https://github.com/gro-ove/actools/blob/master/AcTools/DataFile/RtoDataFile.cs) reads ratio labels/values; [CarSetupValues](https://github.com/gro-ove/actools/blob/master/AcManager/Pages/Selected/CarSetupValues.cs) maps `GEAR_n` to saved `INTERNAL_GEAR_(n+1)`. ADT preserves list order and refuses ambiguous labels rather than silently collapsing them.
