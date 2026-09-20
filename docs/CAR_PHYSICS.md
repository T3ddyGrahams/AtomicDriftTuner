# Readable car physics in ADT

Local preview.20 adds read-only base-car context to **AC Setup** and a file fingerprint to new run/tune snapshots.

## Start here

1. Select your installed car in ADT. Open **AC Setup**.
2. Leave **Use readable car physics for setup guidance** checked. The status tells you whether an accessible `data` folder was found.
3. Load the saved baseline setup you intend to use. This also identifies the selected tyre compound when the setup contains a valid `TYRES` value.
4. Expand **Car physics — values and sources** to review the imported facts. Every fact names its file, section and field.
5. Set your Desired Behavior, generate, and review the **Base car context** column alongside each recommendation.
6. Save the generated setup or explicitly stage it for the companion's pit action. Load/confirm it in game before testing. Importing physics does not apply a tune.
7. Record comparable before/after runs with the same driver, car, track, conditions and goals. Keep the baseline files and test one change at a time where practical.

**Refresh car physics** rereads the files and invalidates the previous recommendation. Generate again before saving or staging. Turning import on or off also requires regeneration. The checkbox controls this AC Setup window; run-history fingerprint capture is automatic and separate.

## What changes

ADT can display supported mass, wheelbase, weight distribution, suspension type, track width, spring/damper/camber/toe values, anti-roll bars, driven wheels, differential values, final drive, engine limiter, brakes and selected-compound tyre dimensions/pressures. These come from seven fixed files: `car.ini`, `suspensions.ini`, `tyres.ini`, `drivetrain.ini`, `engine.ini`, `brakes.ini` and `setup.ini`.

Mapped recommendation rows now show the relevant base value and source. When readable `setup.ini` does not expose a saved control, ADT holds that control instead of treating a base physics field as an adjustable pit setting. Imported FWD/AWD/AWD2 drivetrains hold the current rear-drive differential advice for manual review. Existing numeric tuning heuristics and telemetry analysis remain in place.

Before generation, export and pit-plan staging, ADT checks that imported files have not changed. If they changed, reload the baseline and regenerate. New tune/run snapshots store a fingerprint of these files. Different fingerprints, or a known fingerprint on only one side, prevent a fair before/after verdict. Two older/unavailable snapshots retain the existing comparison rules with an explicit unknown-physics limitation. Raw car files and local car-folder paths are not added to history by this feature.

## Three different sources

| Source | What it tells ADT |
| --- | --- |
| Car `data` files | Base physics definitions and available setup controls |
| Saved setup INI | Selected numeric `VALUE` fields; these may be clicks or indexes |
| Fresh companion setup capture | Supported values actually sampled from the current in-game setup |

For example, a base spring rate of **80,000 N/m** does not mean a saved spring `VALUE=12` should become 80,000. ADT keeps the saved value and shows the base rate separately. Tyre model ideal pressure is not automatically a cold setup pressure target.

## Availability and limits

- ADT does not unpack, decrypt, delete or change car physics. It reads an existing accessible, unambiguous `data` folder. A packed-only car, or a folder containing both `data.acd` and `data`, falls back to saved-setup guidance with an explanation.
- Missing, malformed, oversized or linked files are not used. Valid supported files can still provide partial context. Unknown values and missing compounds stay unknown; ADT does not silently pick another tyre compound.
- A selected baseline is still needed. Base definitions do not prove what is currently loaded in the pits.
- This is not a full physics simulation or a new optimal-tune solver. ADT does not derive caster from geometry, evaluate tyre curves/effective grip, resolve spring motion ratios, or simulate CSP overrides. The file fingerprint covers only the seven listed files, at snapshot time; it does not cover referenced LUTs, every mod asset or continuous in-game physics.
- File changes during a drive can only be detected when another snapshot is taken. Matching fingerprints alone do not prove that a recommendation caused an improvement.
- Existing gearing, Desired Behavior, telemetry evidence thresholds and FFB providers remain available. Preview.18 SDK crash containment and preview.19 wheel-slip/initiation fixes are included.

## First tester check

Use a car with accessible unpacked data. Confirm a known spring/differential fact and the selected compound, then generate and review the source column. Check import off/on, refresh, saving and staging. Change a *copy* of a fixture car file between generation and export to confirm the reload message; never edit the active online car for this test. Confirm saved setup values and original physics files remain unchanged by import. Real driving and pit acceptance still need testing on the tester's installation.
