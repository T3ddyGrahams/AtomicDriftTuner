# Gearing for your corners

Local preview.29 adds tight-corner and long-sweeper goals to **AC Setup → Gearing**. ADT reads supported packed or unpacked car data, compares the final-drive choices the car actually offers, and writes a separate setup when there is a change to test.

## Start to finish

1. Select your installed car. Save the setup you intend to drive in Assetto Corsa. If you want to keep ADT's handling changes, save those and use that file as the gearing baseline.
2. Open **AC Setup → Gearing** and choose that baseline. ADT identifies the gearbox and available final-drive presets. For a new target it also tries to read the engine curve for an RPM starting estimate. **Read car and suggest RPM range** refreshes this estimate explicitly.
3. Choose **mph** or **km/h (kph)**. **Use these units by default** remembers the choice for new car targets. Existing saved car choices are preserved. Switching units converts both speed ranges; it does not change their physical meaning.
4. For **Tight corners**, choose the gear you want to hold and the slow/fast ends of your usual road-speed range. Do the same for **Long sweepers**. These names describe the driver's intended sections, not detected corner geometry or drift angle. Starter speeds are examples. Turn off the two-goal checkbox to use a single target; older saved profiles open that way automatically.
5. If speeds are uncertain, expand **Not sure about your speeds? Use a recorded run**, find runs, choose the intended driver's comparable run, and click **Use this run's speeds**. ADT needs at least 10 seconds of reliable drift evidence with a meaningful speed range in every requested gear. It fills the typical middle-80% speeds by gear; check that those ranges represent your intended sections. It does not identify corner shape or infer an engine power band from a run.
6. Review the RPM target summary. A readable base curve supplies a provisional starting estimate. Open **Advanced** to inspect its basis or enter your own known range. If no suitable curve is available, manual RPM values are required; there is no hidden generic RPM fallback.
7. Click **Find gearing for my corners**. Read the result for both sections. Expand **Compare available final-drive presets** to see actual ratios, RPM differences from the current final drive, predicted RPM in each section and excluded choices. Labels never substitute for actual numeric ratios.
8. **Save targets for this car** remembers both goals and the RPM range for new recordings. It does not change a tune. **Save gearing setup…** creates a separate `.ini` with only the final-drive selection changed. Load that exact setup in AC's Setup menu and test both sections against the baseline. Keep ECU and other settings fixed for a gearing test.

There is no automatic in-game application from this window. Existing setups, car physics and individual gear selections are preserved. If the current final drive is the best match, there is no new gearing setup to save.

## What ADT detects

- **Fixed individual gears, adjustable final drive:** compare the car's final-drive presets while retaining its individual gears.
- **Individually adjustable gears:** decode the selected ratio and identify its available choices; this planner retains that saved selection while comparing final drives. It does not optimize or write individual gears.
- **Preset gearboxes:** decode both requested gears from the selected gearbox. Changing gearbox presets is not part of the exported recommendation.
- **Fixed final drive:** assess the requested sections against the one defined ratio. ADT does not invent editable presets or export a nonexistent change.

One final drive affects every gear. If the driver wants second for tight sections and third for sweepers, both must be assessed together. No choice may satisfy both speed/RPM targets; the result then explains which section falls below or above the target band. Changing the requested gear or speed range can be more appropriate than accepting that compromise.

## Calculation and RPM estimate

The road-speed estimate uses the selected gear ratio, actual final-drive ratio and nominal driven-tyre radius:

`RPM = (km/h ÷ 3.6) ÷ (2π × tyre radius in metres) × 60 × gear ratio × final drive`

It assumes no tyre or clutch slip. Wheelspin, tyre deformation and custom physics change the relationship during a real drift. A larger final drive is shorter: RPM and gearing torque multiplication increase in every gear. This is not a prediction of traction or delivered engine torque.

A candidate reaching the base engine limiter in **either** requested section is excluded. Ranking first prefers a full fit for all requested sections, then minimizes the average squared proportional mismatch at both speed endpoints, weighting each section equally. Ties/negligible differences retain the current ratio. If no eligible preset exists, no recommendation is produced. Adjustable limiter formats remain unsupported by this planner.

For automatic RPM guidance, ADT reads `engine.ini [HEADER] POWER_CURVE` and a bounded, ordered, numeric local LUT. Its provisional rule samples base torque at 100-RPM intervals, computes proportional base power as RPM × torque, and selects the contiguous region around the sampled peak with at least 80% of that peak. Sampling stays inside the curve's domain, above the defined engine minimum (or 500 RPM when absent), and at or below 95% of the base limiter. At least a 500-RPM-wide region is required. No extrapolation or fabricated torque values are used.

This is a **base-curve starting estimate**, not a measured or optimal drift power band. Turbo boost, selected ECU multipliers, hybrid systems, controllers and scripts are not included. A rev limit alone is not enough to derive a power band. Confirm the range by driving and override it under Advanced when needed. The curve-based target records a source fingerprint; changed definitions require refreshing the estimate or explicitly choosing a manual band before calculation/export.

Primary format references: Content Manager's [setup entry loader](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs), [ratio file reader](https://github.com/gro-ove/actools/blob/master/AcTools/DataFile/RtoDataFile.cs) and [saved gear mapping](https://github.com/gro-ove/actools/blob/master/AcManager/Pages/Selected/CarSetupValues.cs). The AC Torque Helper's [base/turbo/ERS separation](https://github.com/gro-ove/ac-torque-helper/blob/master/src/resultingTorqueCurve.jsx) and [RPM–torque power conversion](https://github.com/gro-ove/ac-torque-helper/blob/master/src/acMath.jsx) support the base-curve interpretation. The 80%/95% thresholds are ADT's provisional heuristic, not a claim made by those sources. External code was not copied.

## Supported sources and preservation

One complete supported `data.acd` or unpacked `data` source must supply setup, drivetrain, selected tyres, engine limiter and any referenced ratio lists. Both may coexist only when all supported INI/LUT/RTO/Lua files and bytes match. ADT uses one complete snapshot and checks both copies again at export. It never unpacks to the installed car, mixes conflicting copies, executes scripts or edits physics.

Supported selections include RWD/FWD, explicitly selected gearsets and individual gear indexes (`GEAR_n` maps to saved `INTERNAL_GEAR_(n+1)`, including reverse). The baseline must identify the same car and its selected tyres; editable final drive and gear controls require their corresponding saved selections. Duplicate labels, unknown selections, protected/unreadable data, conflicting copies, AWD and unsupported adjustable limiters produce an explanation rather than a guessed mapping.

Saving rechecks the baseline and complete source fingerprints, reloads both requested gears, recalculates the joint result, and uses the guarded setup writer. It changes only `[FINAL_RATIO] VALUE`. Source and output restrictions from previous versions remain enforced. RPM-curve failures do not prevent a manually specified target when the gearing itself is readable.

Targets remain per car/pack under `%LOCALAPPDATA%/AtomicDriftTuner/gearing-targets/`; the shared unit default is in `speed-units.json`. Neither is shipped in release packages.

In local preview.30, a duplicate or malformed field in an unrelated, identifiable `setup.ini` section no longer blocks gearing. For example, conflicting `FRONT_BIAS` definitions are reported and left untouched while valid gearing definitions remain usable. ADT does not choose between conflicting definitions: a conflict in a required final-drive, selected gear or gearbox definition still blocks calculation. Limiter controls remain detected even when ambiguous. Malformed section boundaries and duplicate saved baseline sections remain unsupported. Handling generation, staging and export also hold conflicting controls unchanged while retaining independent valid ranges.

## Gearing and ECU run review

Save the targets, confirm the actual setup in the recorder and record comparable sections. New run history retains both goals, the RPM source and readable powertrain context. **Tuning Assistant → Gearing & ECU** counts below/above-target exposure only inside the requested gear/speed windows. Overlapping windows for the same gear count each frame once. Corner labels do not imply automatic track recognition. Later target edits do not reinterpret old snapshots, and comparisons flag changed goals.

For two-goal test guidance, both requested windows need at least 10 seconds of usable evidence, along with the existing confirmed setup/car, quality, extended-signal, direction and recorded-ratio checks. Repeated episodes require at least three episodes totaling three seconds. Conflicting low-RPM and near-limiter evidence leads to compromise guidance instead of contradictory shorter/taller recommendations. Stale automatic RPM estimates retain their observations but do not support a concrete gearing-test conclusion.

RPM differences and target exposure are descriptive, not improvement scores. Keep ECU fixed when testing gearing; keep gearing fixed when testing ECU. Configured ECU multipliers are not measured horsepower. Existing handling, FFB and recommendation-credit calculations are unchanged; their separate audit findings remain open.

## Verification

Regression fixtures cover joint ranking, limiter exclusion in either goal, packed/unpacked round trips, fixed/individual/preset gearbox detection, stale second-gear or curve data, curve coverage and malformed input, legacy profiles, unit preferences, goal snapshots, overlapping exposure and recorded-speed eligibility. WPF checks exercise real handlers, both goal cards, persisted targets, conversion without drift, inherited units, invalidation, theme colors and narrow/portrait/wide layouts. Installed-car validation reads data and leaves the game and user setup files unchanged. Driving is still required to assess the result.
