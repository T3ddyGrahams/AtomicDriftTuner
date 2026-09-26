# Setup-value audit corrections — local preview.36

This milestone addresses A1/A2/A7 from the [preview.21 audit](tuning-engine-preview21.md). It establishes whether an adjustment is a legal, correctly encoded change. It does not establish the best stiffness, alignment, pressure or differential setting for a particular car/driver.

## Shared value contract

Ordinary supported scalar controls use explicit per-control `SHOW_CLICKS`: mode 0 saves the control value, mode 1 saves value / STEP, mode 2 saves (value − MIN) / STEP. The implementation follows the conversion in [Content Manager's CarSetupEntry](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs) and the [mode enumeration](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupStepsMode.cs). No third-party code was copied.

Support covers standard corner pressure, toe, spring, bump/rebound and fast damper, rod-length and packer controls, front/rear ARBs, standard DIFF_POWER/COAST/PRELOAD, front brake bias, brake power multiplier and fuel. Not every supported mapping has an automatic tuning rule. Geometry, tyre force, custom script semantics and optimal mechanical deltas are not inferred from these units.

Missing local mode defaults to actual values only when global display metadata is absent or zero. A nonzero/unknown global-only mode remains unsupported because Content Manager's per-control default does not establish AC's global inheritance. Local modes can override global display metadata. Lookup tables, ratios in scalar definitions, malformed/duplicate definitions, unsupported keys and unknown modes remain held. The existing camber tenths/offset mapping stays separate. Final-drive file export re-decodes the old and proposed indexes using the existing ratio-list contract; it is not a scalar control and is not newly enabled for generic pit staging.

Generation, export and staging validate the same saved-coordinate bounds and legal step lattice. Endpoints are legal grid points, never a fractional last click. Off-grid or out-of-range baselines are held rather than repaired as part of a tuning request. Proposed values must serialize accurately in the existing setup writer. Unchanged unsupported fields remain preserved in file exports.

## Requests and explanations

Session intent and Desired Behavior blending are retained. Ordinary controls evaluate existing request coefficients in decoded setup units, then convert the resulting delta into saved units and quantize it. Camber retains its separate tenths-based request convention. Low differential values no longer cause magnitude-based guesses about click encoding.

Quantization cannot reverse the requested direction. Small requests that round to the current step remain unchanged, with a specific explanation. Reaching a limit also produces a hold. ADT does not multiply all legacy coefficients by a coarse STEP or force one large mechanical click just to produce a change. A changed row states the actual saved before/after values and step count; the proposed mechanical effect is explicitly an intended effect to test.

Both the focused test summary and before/after preview show decoded control values. File exports, pit commands and structured recommendation contracts carry the corresponding raw saved values. Existing history is not rewritten or assigned new interpretations.

Setup exposure is checked independently of readable base-physics facts. Missing suspension/tyre facts cannot authorize an unexposed control. Existing FWD/AWD differential restrictions and car/source identity checks continue to apply when writing or staging, including proposals modified after generation.

## Verification and driving acceptance

Five committed regression groups reproduce the original failures before implementation: illegal rebound end-stop export, off-grid ARB/toe direction reversal, misleading on-grid no-op, exposure bypass without base facts, and unknown/custom mapping increments. The expanded suite exercises packed/unpacked round trips, explicit modes 0/1/2, negative minima, fractional/coarse steps, partial endpoints, malformed ranges, global ambiguity, identity/provenance, precise output and equivalent encoded requests. Existing gearing exports remain covered.

WPF checks exercise focused test preview, save, pit staging and exact recommendation values with isolated mode-1/mode-2 setups at narrow width. They do not write user car files or apply anything in game.

Final verification: 402 regression groups, 192 WPF assertions plus responsive renders, and 482 mocked Lua assertions (including 273 pit setup assertions) pass. Production companion files are unchanged; the new runtime checks prove raw click apply/restore and reject incompatible live ranges. Logs are under `artifacts/setup-legality-*`. Cached NU1900 feed warnings and the existing MozaWorkerApi single-file IL3000 warning remain unrelated to these changes.

For live acceptance, load a fresh car baseline, inspect **Your setup changes**, save or stage one supported change, explicitly load/apply it and record the next comparable run. Check the real pit control matches the preview. A hold means no change was generated for that control; do not treat it as a tested adjustment. Validate more than one car and mod convention before broad release. Repeated driver tests and better mechanical amount calibration remain part of the wider 2.0 work.
