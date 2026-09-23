# Tuning-engine audit — preview.21

Completed September 22, 2026 (America/New_York; September 23 UTC). Audited source: `bd01b31dcc5e825142dbc4d1bdd648b7fdd1a219`.

## Decision

Correct setup-value semantics and recommendation attribution before expanding physics-driven tuning. Packed access is useful and the archive reader is not the cause of the defects below: it exposes more definitions to older tuning rules whose unit handling needs improvement.

Eight concrete correctness issues were reproduced against the existing preview.21 assemblies. None were fixed in this audit. Production source, installed cars, user setups, calibration and history were unchanged. No release was built or published.

Follow-up, September 23: local preview.27 addresses **A6** while adding a separate descriptive Gearing & ECU review. Regressions cover packed/unpacked sources, saved-only and definition-only ENGINE_LIMITER, fixed-limiter controls and changed limiter evidence at export. Other seven findings below remain open; the new review does not expand automatic tuning or improvement attribution. Its own forward-RPM filter excludes known backward travel without claiming to resolve A8 in the existing handling metrics.

Priority definitions: **P1** should be addressed before relying on the affected generated output or attributing learning to recommendations; **P2** is a demonstrated conditional correctness issue to include in the same corrective phase. These priorities do not assert hardware damage or successful in-game application of an invalid value.

| ID | Priority | Confirmed issue | Main effect |
| --- | --- | --- | --- |
| A1 | P1 | Saved click modes and physical limits are not modeled consistently | A generated setup can export a selection past a defined end stop |
| A2 | P1 | Fixed raw deltas are snapped to physical steps | Intended changes disappear or reverse direction while their explanation remains unchanged |
| A3 | P1 | Any changed setting can qualify as the named recommendation test | A gearing-only change can be credited to a handling recommendation |
| A4 | P1 | A manual setup hash-only change counts as a tune change | Metadata/comments can qualify a recommendation test without any captured setting changing |
| A5 | P2 | Manual setup field coverage is not matched | A missing captured field can be treated as an actual setting change |
| A6 | P2 | Adjustable-limiter guard checks the wrong setup key | The gearing planner accepts a control that its current scope intends to refuse |
| A7 | P2 | Setup exposure checks depend on unrelated base-fact availability | A control absent from valid setup definitions can still receive a recommendation |
| A8 | P2 | Backward-travel filtering changes with the user's angle goal | Backward samples can contaminate default-goal clean-drift and clipping evidence |

## Findings and acceptance criteria

### A1 — Represent saved values, clicks and physical values separately

Evidence: `Services/AssettoCorsaSetupService.cs:534–562` and `:1065–1071` reduce SHOW_CLICKS to a boolean recognizing only literal 1. `Models/CarSetupModels.cs:290` stores that boolean. `Engine/CarSetupTuningEngine.cs:1711–1774` bypasses numeric bounds for clicks or incompatible raw values. `Services/AssettoCorsaSetupService.cs:427` writes generated replacements without an independent semantic-range check.

Content Manager's [mode enum](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupStepsMode.cs) distinguishes actual values, normalized steps and offset steps. Its [saved-value loader](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs#L153) applies the step and, for offset steps, the minimum. Camber has additional serialization handling. These primary references were checked during the audit; no external code was copied.

Reproduced using the S13's rear-damper definition: mode 2, MIN 500, MAX 9000, STEP 500. Saved value 17 represents 9000. The engine recommends and successfully exports 18, representing 9500. The reproduction uses a synthetic local fixture with those definition values, not an edited installed car.

**Scope:** export is confirmed. Pit-plan staging has an independent strict range/step gate (`Services/PitSetupPlanService.cs:85–96`) which rejects this incompatible mapping. No successful illegal in-game application was demonstrated.

Correction: introduce an explicit, verified serialization-mode model shared by import, generation, export, decoding and pit staging. Decode, validate, adjust and re-encode through one contract. Hold controls whose mapping is unknown rather than treating absence of a mapping as permission to increment them.

Acceptance: modes 0/1/2; nonzero and negative minima; both bounds; camber special handling; legal round trips; unsupported modes held; generation/export/staging agree; an endpoint value cannot produce an out-of-range serialized output. Establish global-versus-local display semantics before adding inheritance rules.

### A2 — Generate legal step changes and explain the final result

Evidence: `Engine/CarSetupTuningEngine.cs:719`, `:736`, `:778` use fixed raw increments for toe, ARB and springs. `:1652–1667` rounds the proposed raw value onto a metadata grid. `:258–266` keeps the pre-rounding explanation.

Confirmed against the user's S13 `test3` definitions and saved values, using FastSelfSteer intent / Balanced aggressiveness:

| Control | Baseline | Declared step | Generated | Explanation conflict |
| --- | ---: | ---: | ---: | --- |
| Rear ARB | 5002 | 1000 | 5000 | Promises a stiffer rear, but the numeric value decreases |
| Front toe | -39 | 5 | -40 | Promises an increase, but the numeric value decreases |
| Rear ARB, synthetic on-grid control | 5000 | 1000 | 5000 | Promises a one-step change, but nothing changes |

The direction mismatch is a numeric defect independent of whether a particular mechanical recommendation benefits the car. An off-grid input must not be silently normalized in the opposite direction as part of a requested adjustment.

Correction: make requests in verified legal adjustment steps, distinguish baseline validation from a tuning action, and derive the explanation from the final encoded delta. Report boundary holds and no-ops honestly.

Acceptance: steps both smaller and larger than heuristic coefficients; off-grid inputs; signed values; saturation; no-op reporting; a requested increase cannot silently become a decrease. First define mapping through A1; simply multiplying every old heuristic by STEP is not a sufficient mechanical model.

### A3 — Match the actual change to a structured recommendation

Evidence: `Engine/RunComparisonEngine.cs:138–140` requires a linked baseline, nonempty recommendation text and any tune-change row. `Models/RunIntelligenceModels.cs:49–50` stores the session link and free text, without expected setting/direction/value fields.

Reproduction: a test described as a rear-rebound adjustment changes only FINAL_RATIO. Otherwise-valid synthetic comparison data and Better driver feedback produce `RecommendationTestTracked=True` and a conclusion supporting improvement in the recorded recommendation test.

A normal UI route can produce this mismatch: gearing saves propagate through `CarSetupWindow.xaml.cs:584`, `TuningAssistantWindow.xaml.cs:768` and the guided journey's setup-path update, while the earlier handling recommendation text can remain selected.

Correction: persist a recommendation ID, baseline fingerprint, intended controls and expected old/new values or bounded directions. Compare the actual captured delta to that contract. Keep user-created hypotheses separately identified. Unmatched changes remain observable run differences, not validation of a named recommendation.

Acceptance: wrong control, wrong direction, unrelated gearing-only change, several unexplained changes, correctly matched test, and explicit user-authored test. Preserve the distinction between association and causation even for matched tests.

### A4 — Do not count provenance rows as actual setting changes

Evidence: `Engine/RunComparisonEngine.cs:123–126` adds a display row when manually attached setup hashes differ but no numeric setup settings changed. `:136–140` treats any such row as a captured tune change.

Reproduction: identical settings with different manual-file hashes still satisfy tracked-test attribution. Improved synthetic telemetry and a Better rating then produce the same supportive recommendation-test conclusion. A comment or line-ending change is sufficient to alter a file hash.

Correction: separate verified setting deltas from metadata, coverage and provenance differences. A display row explaining an unknown file change must not satisfy the actual-change requirement.

Acceptance: comment-only, line-ending-only, CSP metadata-only and unknown nonnumeric changes cannot establish a tested tune change; real supported value changes remain visible and eligible when all other conditions hold.

### A5 — Require comparable coverage for manual setup attachments

Evidence: `Engine/RunComparisonEngine.cs:38–46` checks matching captured field sets only for CSP snapshots. The union of manual settings at `:103–116` emits added/removed rows which also satisfy the tracked-test condition.

Reproduction: remove PRESSURE_LF from the after snapshot, keep all common values identical, and comparison remains comparable/tracked. Missing capture does not establish what happened to that setting in game.

Correction: require matching relevant capture coverage for manual files as well, unless a specific documented default can be resolved with evidence. Preserve missing/added rows as descriptive information.

Acceptance: omissions, additions, incomplete files and capture-method changes block attribution appropriately; complete matched manual attachments continue working.

### A6 — Recognize ENGINE_LIMITER before trusting fixed-limiter gearing estimates

Evidence: `Services/GearingDataService.cs:73–75` looks for a setup section named LIMITER, then uses the base engine.ini limiter. Existing `tests/AtomicDriftTuner.RegressionTests/GearingChecks.cs:87–91` tests that same incorrect section name. ENGINE_LIMITER is identified as a percent control in [Content Manager's implementation](https://github.com/gro-ove/actools/blob/master/AcManager.Tools/Objects/CarSetupEntry.cs#L29), and that name was independently found in installed car definitions and saved setups during read-only inspection.

Reproduction: a synthetic ENGINE_LIMITER selection of 80 with base limiter 8000 is accepted. The planner recommends final ratio 5.3, estimates 7029 RPM at the top of the requested range and marks the target as fitting. Changing only the section name to LIMITER triggers the intended adjustable-limiter refusal.

The planner labels its estimates as base/no-slip estimates, which is useful, but the intended adjustable-limiter exclusion is still bypassed. No measured runtime limiter or vehicle damage is claimed.

Correction: recognize the real key in definitions and saved setups; refuse unsupported limiter adjustment until its encoding and active value can be validated. Preserve the separate warning about ECU/script overrides.

Acceptance: packed/unpacked sources, saved-only and definition-only ENGINE_LIMITER, fixed limiter control case, unsupported custom limiters, and changed limiter evidence on export.

### A7 — Apply setup-definition restrictions independently of base facts

Evidence: `Engine/CarPhysicsGuidance.cs:13` skips all guidance when Available is false, including the valid-definition exposure check at `:35–39`. `Services/CarPhysicsService.cs:102–107` defines Available as at least one imported fact, separately from HasSetupDefinition.

Reproduction: valid setup.ini exposes only PRESSURE_LF, other base facts are unavailable, and the saved baseline contains DIFF_POWER=60. HasSetupDefinition is true but Available is false. The engine recommends DIFF_POWER=64 despite that control not being exposed.

Correction: gate exposure checks on valid setup metadata. Gate base-value display on available facts separately. Unavailable or malformed unrelated physics files must not disable a restriction already established by readable definitions.

Acceptance: definition-only source, unreadable base files, partial facts and fully populated source; unknown controls stay held whenever valid setup definitions exclude them.

### A8 — Use known travel direction consistently for clean-drift evidence

Evidence: `Services/AssettoCorsaTelemetryReader.cs:310–329` deliberately folds longitudinal direction when calculating displayed slip angle. `Engine/DriftDiagnosisEngine.cs:277–278` consults the separate longitudinal-velocity channel only when an explicit angle goal raises the drift limit above 72 degrees. The reverse-gear exclusion at `:40` cannot catch backward travel with a forward gear selected. Clipping calibration at `:181–185` uses the resulting drift evidence.

Reproduction: a valid synthetic 30-second recording at 50 Hz, speed 60 km/h, forward gear 2, folded slip 30 degrees, longitudinal velocity -14.43 m/s and FFB magnitude .99 yields:

| Goal | Counted clean drift | AC gain suggestion |
| --- | ---: | ---: |
| Keep current angle | 29.98 s | -3 |
| Explicit extreme-angle goal; identical telemetry | 0 s | 0 |

This is an intentionally sustained boundary fixture; it establishes inconsistent admission of known backward samples, not that every rollback should be classified as a spin. Smaller accumulated intervals can contaminate the same measures.

Correction: apply known travel direction consistently to usable-drift and related input/calibration evidence, regardless of the goal. Retain backward movement for separate inspection/control evidence. Legacy recordings without direction remain unknown rather than receiving invented direction.

Acceptance: forward/reverse gear versus actual travel direction; default and explicit angle goals; brief and sustained backward segments; unchanged positive-velocity recordings; legacy-null channel; no false gain correction derived from excluded backward segments.

## What packed data currently changes—and what remains separate

| Area | Current implementation | Next useful connection |
| --- | --- | --- |
| AC setup | Baseline + session-intent coefficients + Desired Behavior; physics adds context and guards | Verified units/steps first, then validated per-control response models |
| Gearing | Decoded ratios, selected gear/compound and a disclosed static no-slip RPM estimate | Recorded RPM by gear/phase, applicable limiter evidence, pedal/clutch context |
| ECU | Supported map selection and descriptive multiplier/RPM ranges | Preserve evaluable curve points and provenance; distinguish file mapping from active selection |
| Telemetry | Phase, pedal, angle/control and clipping observations | Car-specific interpretation with explicit confounds and comparable repeated tests |
| History | Immutable snapshots, pairwise comparisons and saved driver feedback | Matched recommendation tests, then accumulated car/driver evidence |
| FFB | Existing hardware/intent/car-profile formulas and calibration | A common source model with confidence, rather than silently substituting base physics |

The FFB path still uses CarProfile fields (`Engine/TuningEngine.cs:108–129`). `Services/AssettoCorsaScanner.cs:647–815` separately reads unpacked car/tyre files and still marks packed-only physics unread. It does not use the new shared packed source. Imported setup-window facts therefore must not be described as automatically driving all FFB calculations. This is an integration gap, not an instruction to overwrite inferred/user profile values without validation.

Run reviews are persisted and displayed, but are not consumed by either numeric tuning engine as accumulated learning. Saved review verdicts also lack rule/decoder/analyzer provenance sufficient to distinguish an old judgment from a new reanalysis. Add that provenance before deriving learned recommendation effectiveness. Existing raw telemetry is reanalyzed when reopened; old saved reviews remain historical records.

Global DISPLAY_METHOD/SHOW_CLICKS inheritance remains a research item. ADT's range loader and ECU decoder treat it differently, while the inspected CM per-control loader reads the local mode. This audit does **not** establish that blindly inheriting a global flag into the ECU decoder is correct.

## Implementation order after this audit

1. **Correctness phase:** one serialization/range contract (A1/A2), independent exposure guard (A7), real limiter key (A6), consistent direction filtering (A8). Regression tests must initially demonstrate each failure, then verify the correction without weakening existing source/identity gates.
2. **Trustworthy test history:** typed real-setting deltas, comparable field coverage, structured recommendation contracts (A3–A5), versioned analysis provenance. Continue preserving immutable raw recordings and earlier reviews.
3. **Read-only gearing/power evidence:** show RPM distributions by forward gear and drift phase, proximity to a known applicable limiter, and repeated throttle/clutch context. Keep observed RPM separate from no-slip estimates; their difference alone cannot identify tyre slip, clutch slip or an active setup mismatch.
4. **Controlled recommendation improvements:** one concrete setting test at a time; repeated comparable before/after runs with recorded driver feedback. Use the S13 as one validation car, then cars with different modes/steps/drivetrains. Introduce suspension/differential sensitivity only where encoding and evidence support it.

For the S13, selection 5 maps to 4.23 and selection 1 maps to 4.80 despite its 4.30 display label. The unnamed ECU value remains unidentified. The existing combined change cannot isolate gearing versus ECU contribution. Future testing should use complete named settings with baseline, gearing-only, ECU-only and combined runs under comparable conditions. A full physics simulation or universal optimal-tune claim is not warranted.

## Validation and preserved strengths

All **261 existing regression groups passed** during this audit, including the existing 8,000-combination output-range sweep. Those tests establish implemented behavior and broad range coverage; they did not contain the boundary/attribution cases above and do not establish driving quality.

Isolated audit probes invoked the existing preview.21 DLLs, with synthetic fixtures and read-only S13 inspection. No shared application project was rebuilt. The setup probe exported only inside its ignored fixture directory. The history probe intentionally supplies improved metric values to isolate attribution logic; it is not evidence of real-world improvement.

Local evidence retained under ignored `artifacts/`:

- `audit-setup-physics/results.log` and `Audit.csproj`: raw-step, S13, click-endpoint export and definition-only probes.
- `audit-history-probe/results.log` and `Probe.csproj`: control, hash-only, field-coverage and wrong-recommendation probes; detailed notes in `audit-history-findings.md`.
- `audit-gearing-limiter/probe.log` and `Probe.csproj`: real-key limiter probe; detailed notes in `audit-gearing-power-findings.md`.
- `tuning-audit-2026-09-23/telemetry-probe/results.log` and `Probe.csproj`: known-backward-travel probe.
- `tuning-audit-2026-09-23/regression.log`: 261 passes, zero failures.
- `tuning-audit-2026-09-23/assembly-evidence.json`: audited source and assembly hashes for local reproduction.

Example commands from repository root, using the retained audit fixtures:

```powershell
dotnet run --project artifacts/audit-setup-physics/Audit.csproj
dotnet run --project artifacts/audit-history-probe/Probe.csproj -c Release
dotnet run --project artifacts/audit-gearing-limiter/Probe.csproj
dotnet run --project artifacts/tuning-audit-2026-09-23/telemetry-probe/Probe.csproj
dotnet tests/AtomicDriftTuner.RegressionTests/bin/Debug/net8.0-windows/AtomicDriftTuner.RegressionTests.dll
```

Keep existing safeguards: immutable history, recorded goals, unknown/partial decode states, wrong-car and changed-source refusals, explicit pit staging, failed/live capture guards, low-confidence suppression, control tradeoff reporting, conservative proxy labels and protected/corrupt archive fallback. Fixing the findings should strengthen these, not replace them with more confident guesses.
