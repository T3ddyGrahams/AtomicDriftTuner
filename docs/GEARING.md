# Gearing planner — first version

Public beta `0.9.0-preview.17` includes the final-drive planner first developed in preview.5. This is a separate step in **AC Setup → Gearing • Final-drive planner**.

## Try it

1. Select your installed car in ADT. Save a setup for that car in Assetto Corsa.
2. Open **AC Setup**, choose the baseline, then open **Gearing**. If you already generated handling changes, save those first and use that generated setup as the gearing baseline.
3. Enter the gear you want to hold, your minimum/maximum road speed, and your desired engine RPM range. Choose km/h or mph. The initial numbers are examples, not a detected power band.
4. Click **Calculate final drive**. Review the actual current/suggested ratios, predicted RPM at both speed endpoints, and the reason for the choice. Expand the supported-ratio list to compare alternatives.
5. **Save target for this car** remembers your choices. **Save gearing setup…** writes a separate `.ini`, changing only `[FINAL_RATIO] VALUE`. It is disabled when the current ratio is already the best match.
6. Load that new setup in Assetto Corsa's Setup menu. Test the same section, gear and driving conditions against your baseline. Watch for limiter contact or dropping below the usable RPM range; adjust your target and repeat.

There is no automatic in-game application. Saving a target does not change a setup. Saving a gearing setup preserves the source setup and every unrelated setting.

## What the estimate means

The planner uses the selected gear, actual supported final-drive ratios, nominal driven-tyre radius and engine limiter. It predicts RPM from road speed with **no tyre or clutch slip**:

`RPM = (km/h ÷ 3.6) ÷ (2π × tyre radius in metres) × 60 × gear ratio × final drive`

Ratios that hit the limiter within the requested speed range are excluded. Ranking prefers ratios whose endpoints fit inside the requested RPM band, then minimizes the squared proportional mismatch to the two requested RPM endpoints. Equal/negligibly different results keep the existing ratio. If no ratio spans the entire RPM band, the result explicitly says it is a partial fit. If all ratios reach the limiter, change the gear or speed target.

A numerically larger final drive is shorter: it increases engine RPM and torque multiplication in every gear. A smaller ratio is taller. This describes gearing multiplication, not a prediction of available traction or delivered engine torque. Drift wheelspin, clutch slip, tyre deformation and custom physics can materially change RPM. Version 1 does not diagnose gearing from telemetry, infer an engine power band, optimize individual gears, or score improvement from runs.

Gearing targets are saved separately from the seven existing Desired Behavior handling goals. The handling and telemetry algorithms are unchanged. Existing run/tune history can record a saved setup through the normal workflow; the gearing planner itself does not claim that a recommendation improved a run.

## Supported car data

- Unambiguous unpacked `data/setup.ini`, `drivetrain.ini`, `tyres.ini`, `engine.ini`, and local `.rto` files.
- RWD and FWD; fixed gears, explicitly selected gearsets, and individually adjustable gears whose selected ratio indexes can be decoded.
- A saved setup with matching `[CAR] MODEL`, `[FINAL_RATIO] VALUE`, and `[TYRES] VALUE`; gearsets also require `[GEARSET] VALUE`, adjustable gears require their `INTERNAL_GEAR_n` selection.
- Fixed engine RPM limiter and a resolvable driven-tyre compound/radius.

Packed/encrypted data, both packed and unpacked sources together, AWD, adjustable limiters, duplicate ratio labels, malformed or incomplete files, and unresolved selections receive an explanation instead of a guessed recommendation. ADT does not unpack or modify a car's physics. Do not alter a car's data merely to enable this planner; use a supported car for initial testing.

Saving rechecks content fingerprints for the baseline and every physics/ratio file used in the estimate, recomputes the recommendation, and uses the existing guarded setup writer. A changed baseline or car file requires recalculation. Targets persist under `%LOCALAPPDATA%/AtomicDriftTuner/gearing-targets/`, keyed by the same car/pack identity used for behavior profiles.

## Verification

- Synthetic regressions cover non-monotonic ratio lists, gearsets, adjustable gears, tyre compound/drive axle, invalid inputs, limiter exclusion, partial/no-op results, stale data, wrong-car baselines, source preservation, single-parameter exports and per-car persistence.
- WPF checks exercise real calculation handlers, stale-result invalidation, unit switching, persistence, themed text/buttons and narrow/portrait/ultrawide controls. They use isolated fixtures.
- Read-only installed-car check: `687_Bros_Toyota_GR_86_drift` with its existing `generic/last.ini` successfully decoded the fixed third gear, current final drive and selected tyre. The test export was written to a temporary test folder, not the game or user setup folder.
- `bdb_nissan_laurel_c33` was correctly blocked because two final-drive entries reuse the same label. No mapping was guessed and no car data was edited.
- Live driving and subjective before/after improvement remain for the driver to test.

Developer reference: Content Manager's [CarSetupEntry](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs) maps ratio selections to zero-based indexes; [RtoDataFile](https://github.com/gro-ove/actools/blob/master/AcTools/DataFile/RtoDataFile.cs) reads ratio labels/values; [CarSetupValues](https://github.com/gro-ove/actools/blob/master/AcManager/Pages/Selected/CarSetupValues.cs) maps `GEAR_n` to saved `INTERNAL_GEAR_(n+1)`. ADT preserves list order and refuses ambiguous labels rather than silently collapsing them.
