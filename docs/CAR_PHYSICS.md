# Understanding car data and saved settings in ADT

Local preview.21 can read ordinary packed `data.acd` archives as well as supported unpacked car data. It uses the car's definitions to explain supported gearing and ECU selections in your saved setup. It also keeps the base-car context introduced in preview.20.

## Start here

1. Select your installed car in ADT. Open **AC Setup**.
2. Leave **Use car data for setup guidance (packed or unpacked)** checked. Read the status below it. You do not need to unpack an ordinary supported archive in Content Manager first.
3. Choose your saved setup and press **Load Baseline**. Use the same setup that you will load in game. A valid saved `TYRES` selection also identifies the tyre compound for the displayed base facts.
4. Open **What my saved settings mean**. ADT shows each supported selection's meaning, its source and whether the interpretation is verified, partial or unsupported. Check any warnings before drawing conclusions about an ECU change.
5. Open **Car physics — values and sources** if you want the suspension, tyre and other base facts. Each fact names its file, section and field.
6. Set your Desired Behavior, generate, and review the **Base car context** column alongside each recommendation. Use **Gearing • Final-drive planner…** when you want to plan final drive around a drift gear, speed and RPM range.
7. Save the generated setup or explicitly stage it for the companion's pit action. Load/confirm it in game before testing. Reading car data or decoding a saved setting does not apply a tune.
8. Record comparable before/after runs with the same driver, car, track, conditions and goals. Keep the baseline and test one change at a time where practical.

**Refresh car physics** rereads the files and invalidates the previous recommendation. Generate again before saving or staging. Turning import on or off also requires regeneration. The checkbox controls this AC Setup window; run-history fingerprint capture is automatic and separate.

## What the interpretation labels mean

| Label | What you can conclude |
| --- | --- |
| Verified mapping | The saved selection maps to the displayed ratio, gearset or supported engine-map definition in these files. This does not prove that the setup is active in game. |
| Partial | Some information is readable, but the selection or its effect cannot be fully established. Read the explanation; possible interpretations are not confirmed settings. |
| Unsupported | ADT has no reliable decoder for this control or its definitions are unavailable, malformed or outside the supported formats. The saved value is preserved. |

ADT can decode final-drive choices, supported individual forward-gear choices and explicitly defined gearsets. Ratio lists are read in their original order: a saved index such as `5` is a selection, not a claim that the ratio is 5:1.

For supported CSP `ENGINE_MAPS` controls, ADT can show the selected map and its configured torque-multiplier range when the selection and referenced files can be verified. A label such as **Race Fuel** alone does not prove a horsepower increase. Custom mappings with ambiguous row/value conventions remain partial; missing definitions and unsupported formats stay unknown. Configured multipliers are not measured power, and ADT does not simulate scripts, boost controllers or all other engine effects.

A saved ECU setting needs a named section that ADT can identify. An isolated `VALUE=2` without a section name is preserved and flagged; ADT does not assume it belongs to the ECU or attach it to a nearby control. Confirm the selection in game and obtain a complete named setup capture before attributing an improvement to it.

## Base context and tuning guidance

ADT can display supported mass, wheelbase, weight distribution, suspension type, track width, spring/damper/camber/toe values, anti-roll bars, driven wheels, differential values, final drive, engine limiter, brakes and selected-compound tyre dimensions/pressures. The base facts come from `car.ini`, `suspensions.ini`, `tyres.ini`, `drivetrain.ini`, `engine.ini`, `brakes.ini` and `setup.ini`; setting decoders can also read supported local ratio and lookup tables referenced by those definitions.

Mapped recommendation rows now show the relevant base value and source. When readable `setup.ini` does not expose a saved control, ADT holds that control instead of treating a base physics field as an adjustable pit setting. Imported FWD/AWD/AWD2 drivetrains hold the current rear-drive differential advice for manual review. Existing numeric tuning heuristics and telemetry analysis remain in place.

Decoding provides explanations and validation; it does not automatically retune the ECU, replace the existing numeric tuning heuristics or prove why a car felt better. Telemetry analysis remains part of the testing workflow.

## Three different sources

| Source | What it tells ADT |
| --- | --- |
| Car `data.acd` or unpacked `data` files | Base physics definitions, available setup controls and supported selection mappings |
| Saved setup INI | Selected numeric `VALUE` fields; these may be clicks or indexes |
| Fresh companion setup capture | Supported values actually sampled from the current in-game setup |

For example, a base spring rate of **80,000 N/m** does not mean a saved spring `VALUE=12` should become 80,000. ADT keeps the saved value and shows the base rate separately. Tyre model ideal pressure is not automatically a cold setup pressure target.

## Read-only handling and comparisons

Packed data is decoded into a bounded, private **memory cache inside ADT**. It is not extracted into the installed car, written to a disk cache or uploaded. Closing ADT discards that cache. ADT does not change, delete or repack the archive. Lua files may be recognized as present, but scripts are never executed or used to guess their effects.

Before generation, export and pit-plan staging, ADT checks that the imported source has not changed. If it changed or became unavailable, reload the baseline and generate/calculate again. **Refresh car physics** also invalidates the previous recommendation.

New run/tune snapshots store a fingerprint covering the whole packed archive, or the supported flat INI/LUT/RTO/Lua files read from an unpacked `data` folder. Raw car files and local car-folder paths are not added to history by this feature. Different fingerprints, or a known fingerprint on only one side, prevent a fair before/after verdict. Two older/unavailable snapshots retain the existing comparison rules with an explicit unknown-physics limitation.

**Start a fresh baseline after upgrading from preview.20 when you want a fair physics-aware comparison.** Preview.21 fingerprints cover a broader source set and use a new format, so an earlier fingerprint can differ even when you did not edit the car. Your old history remains available.

Fingerprints describe files at snapshot time. They do not continuously monitor a drive, cover every mod asset or verify which CSP overrides were active. Matching fingerprints alone do not prove that a recommendation caused an improvement.

## Availability and limits

- Ordinary packed data and an unambiguous unpacked `data` folder are supported. If both `data.acd` and `data` are present, ADT does not guess which is active or mix their contents. It falls back with an explanation.
- Protected, corrupt or unsupported archives are not forced open. A failed read does not prove protection: a renamed car folder, unsupported encoding or damaged data can produce a similar failure. Existing saved-setup and telemetry workflows remain available when import is unavailable.
- The packed reader supports safe, flat ASCII file names and car identifiers, with UTF-8/ASCII text content. Legacy ANSI text inside a packed archive is currently unsupported. Nested references, inline expressions and custom script-based settings are not decoded.
- Reads are bounded: up to 64 MiB per archive, 1 MiB per file, 16 MiB of decoded data and 1,024 entries. Unsafe names, duplicates, malformed records and linked source files are refused. Other unsupported settings can still leave useful partial context; missing values and tyre compounds stay unknown.
- The same saved baseline actually used in game is still needed. Base definitions and a verified file mapping do not prove what is loaded in the pits. A fresh companion capture can provide supported in-game values, but it does not guarantee readback for every custom control.
- This is not a full physics simulation or an optimal-tune solver. ADT does not derive caster from geometry, evaluate tyre curves/effective grip, resolve spring motion ratios or simulate arbitrary CSP overrides.
- Existing Desired Behavior, telemetry evidence thresholds and FFB providers remain available. The PitHouse crash-containment and wheel-slip/initiation fixes remain included.

## First tester check

Use a car with ordinary packed data or accessible unpacked data. Load a known saved setup and compare a decoded final drive against the pits. If the car has a supported named ECU control, check its label and read the limitations. Confirm a base suspension fact and selected tyre compound separately. Check import off/on, refresh, saving and staging. For a file-change check, use a separate fixture car copy; never edit the active online car for this test. Confirm that import leaves the saved setup and original car files unchanged. Real driving and pit acceptance still need testing on the tester's installation.
