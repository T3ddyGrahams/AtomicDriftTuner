# Telemetry intelligence preview

## Intelligence 2.0 completion plan — September 26

Local **previews .35–.37** implement the first three milestones below. Public preview.33 remains the released beta. This is ongoing work; ADT does not yet learn optimal settings from accumulated reviews or verify physical tyre forces/hands-off steering.

| Milestone | State | Completion requirement |
| --- | --- | --- |
| Reliable test attribution and direction handling | Implemented in preview.35; driving acceptance pending | Exact planned versus actual controls, preserved history, matched coverage, goal-specific evidence, and consistent backward-travel exclusions. |
| Car-supported legal adjustments | Implemented for supported mappings in preview.36; driving acceptance pending | Audit A1/A2/A7: shared verified value/step mapping, direction-preserving quantization and definition guards, with truthful holds for unknown mappings, invalid baselines, end stops and no-ops. See [scope and tests](audits/setup-values-preview36.md). |
| Deeper phase and speed diagnosis | Implemented in preview.37; driving acceptance pending | Separate initiation/transition/sustained behavior by speed and direction; tie it to saved Desired Behavior and pedal context, with evidence counts, uncertainty and conflicting signals visible. Slip and steering remain proxies. See the condition review below. |
| Repeated car/driver test history | Pending | Accumulate only matched, comparable experiments with analysis provenance; retain regressions, tradeoffs and driver disagreements, avoid counting repeated reviews of one run as independent tests, and report insufficient evidence explicitly. |
| Controlled recommendations and acceptance | Pending | Use supported adjustments and repeatable car/driver evidence for one reviewable test at a time. Validate with multiple cars, tracks, input styles and wheelbases; preserve manual confirmation and rollback. |

### Review where a pattern happens — local preview.37

1. Save a run with the intended car/driver and Desired Behavior. Include the approach to entries, both transition directions and several clean sections. These can be short sections on a short track.
2. Read **Your next step**. A supported car finding now names its measured speed/direction condition. If conditions conflict or inputs differ substantially, keep the setup and repeat the same section at similar speed and with similar pedal use before tuning.
3. For the explanation, enable **Show advanced telemetry and recommendations → Phase Evidence → Where the pattern happens**. Select a row to read its full evidence. The table scrolls horizontally; the explanation wraps underneath. The existing appearance colours apply.
4. Timing needs three complete entries/transitions in that condition. Continuous observations need ten **accumulated** clean seconds per speed/direction condition. Sparse conditions remain visible with LOW confidence; a whole-run total cannot supply their missing evidence. Overall unreliable or insufficient runs remain inspection-only.
5. If ADT offers a supported focused test, review the exact change, apply it in the pits and repeat the named section. A whole-car adjustment can affect other conditions, so check both directions and review the comparison before keeping it.

Canonical speed bands are below 50, 50 to below 90, and 90+ km/h. The shared saved speed preference controls condition labels; mph boundaries (~31 and ~56) are rounded display equivalents, not altered calculations. Complete timing events that cross bands remain visible but cannot establish a speed-specific setup recommendation. Left/right uses the existing body-slip sign convention; low-angle response rows use yaw direction. These labels do not identify track corners or prove identical lines.

The condition table covers entry/transition timing, transition steering response, sustained-angle variation, front/rear wheel-slip share, powered rotation, steering-oscillation rate, low-angle front response and drift FFB saturation. Means use elapsed time rather than frame counts. Variation never crosses a gap, phase exclusion, speed band or direction boundary. Oscillation clusters are assigned at detection, and may begin in a different speed band. Timing and continuous thresholds, speed bands and pedal tolerances are provisional engineering heuristics requiring driving validation.

Mean throttle, brake and raw clutch signal are shown only with at least 90% known pedal coverage. For timing and axle-slip findings, differences over 20 percentage points throttle/clutch or 15 points brake between supported conditions request a matched repeat. Similar means do not prove the same pedal timing or technique. Missing longitudinal travel remains unknown and known backward motion stays excluded. Tyre state, corner geometry, line, steering ratio and driver input can explain differences; none of these observations proves tyre force or hands-off self-steer.

This pass changes diagnosis presentation and when a handling test is justified. It preserves existing aggregate metrics, live recording-readiness checks, gain calibration, gearing/ECU calculations and comparison scoring. Raw saved sessions are reanalyzed on reopening with `drift-diagnosis/5` and `driving-context/1`; original files and historical review verdicts remain unchanged. It does not yet compare condition buckets across multiple experiments or automatically learn a tune.

### Try the verified setup-test workflow

1. Record a confirmed baseline with the intended car, driver, Desired Behavior and loaded setup.
2. In **Tuning Assistant → Your next step → Review one car setup change**, review the exact values and save or stage that focused test. ADT records the intended controls, values and measurement automatically.
3. Load the saved setup or explicitly apply the staged plan in the pits. Open the guided comparison recorder. Keep the prepared test description unchanged, capture/attach the setup actually in use, confirm it, then record comparable sections.
4. In **Before / After → Your setup changes**, inspect **Test verification**. The captured values must match the exact plan with no additional changes. Paired left/right controls count as one planned adjustment group, but their individual effects cannot be separated.
5. Rate and save the comparison. ADT supports the recommendation only when the runs are comparable, the planned measurement improves beyond the existing tolerance, the overall result is closer to the saved goals and the driver reports Better. This is an association to repeat and verify, not proof of causation.

To test your own car change, select the baseline in Recorder, describe the experiment, attach/capture the changed setup and check **This is my own car setup test, not an ADT recommendation**. ADT records the actual before/after car values when recording starts; keep FFB fixed. The review calls it a driver-defined test and never attributes it to an ADT recommendation. The checkbox is cleared when a guided ADT plan is prepared again. No change, mismatched field coverage or mixed FFB changes cannot define this car-only experiment.

Editing a prepared description or choosing a different baseline removes its exact-plan association. Notes alone—including older free-text recommendation and FFB plans—remain observations. Supported exact ADT contracts currently come from the focused car-test window. It is still possible to inspect any valid recording and its setting differences; no synthetic historical plans are invented.

Known backward motion is now excluded from clean drift, initiation/transition windows, pedal context and gain calibration independently of the angle goal. Windows do not bridge the excluded interval. Angle attempts retain their backward/recovery evidence. Legacy recordings lacking direction remain readable with an explicit unknown-direction note.

New reviews store `run-comparison/2`, both analyzer versions (currently `drift-diagnosis/5`) and the matched test ID/origin. Older saved files are not rewritten; their stored verdicts and driver feedback remain historical, with an unversioned-review label where needed. Reopening raw recordings uses the current analyzer. Numeric setup, gearing and ECU generation rules are unchanged by the exact-test attribution milestone.

### Earlier intelligence milestones

Local preview.26 distinguishes full goal coverage from **READY FOR PARTIAL REVIEW**. With reliable telemetry and at least 60 seconds of usable drift, a run can be reviewed even if the track or driving does not provide every requested measurement. The missing goals remain visible; their measured values, confidence and comparison eligibility do not change. Full readiness still uses the existing 20-second minimum and selected-goal requirements. You can stop and save at any time; neither status stops recording automatically. The optional ready notification sounds once per recording, including when partial review becomes available first.

**Short-track guidance:** time counters accumulate across multiple shorter driving sections in the same recording. Ten seconds total does not require one uninterrupted ten-second drift or corner. Individual initiation, transition and angle attempts still need their own valid windows; telemetry gaps do not count as driving time. Repeated short clean sections can supply useful evidence.

**Front-response guidance:** this particular proxy needs normal cornering, separately from the axle-slip measurement collected in drift. Progress shows collected seconds out of ten. Existing eligibility is speed at least 30 km/h, body slip at most 8°, and absolute recorded steering angle from 12° through 120°. A separate counter explains missing axle-slip evidence. Missing wheel-slip channels or a drift-only layout can leave one measurement unavailable even when the rest of the run is useful. Partial review preserves that limitation rather than guessing front grip.

Desktop, companion 0.4.0-preview.4 and the touchscreen receive the same heading, instructions and missing-goal list. Update the companion with the driving session closed, then launch a new session. It does not fix or bypass the separately recorded tuning-engine audit findings.

Preview.22 adds prominent evidence banners on desktop, companion and touchscreen. All missing goal-specific requirements are visible. Ready to Review uses the existing usable-drift, reliability and goal checks. The optional desktop PC chime and touchscreen device chime each sound once per recording; browser audio requires a touch/keyboard gesture after loading. Reconnects and readiness fluctuations do not repeat them. Missing telemetry, interrupted recordings and stopped/saved runs withdraw live readiness. Recording continues until explicitly stopped. Readiness means useful evidence for review, not proof of improvement; tuning calculations and thresholds are unchanged.

Live capture accepts one unnamed numeric VALUE at the root or under `[]` when named controls are also present. Its number is preserved separately in the fingerprint/history, with no inferred ECU/control identity. Changes invalidate confirmation and mixed-run attribution. History flags the unidentified control and keeps before/after attribution inconclusive; guarded pit staging still refuses these baselines. Duplicate, malformed, nonnumeric and unbounded data remain rejected.

Local preview.19 separates unusable wheel-slip readings from core motion evidence. A wheel-slip spike no longer erases an otherwise valid entry or transition. Bad readings are excluded from axle-slip metrics and affected pedal-slip comparisons; analysis reports their count separately. Invalid motion, gaps and excluded driving still break phase evidence. Initiation rise time still requires three complete entries; linked transitions are not fresh initiations. Saved recordings are reanalyzed when loaded, without rewriting their raw samples.

Introduced in development preview 1 and included in public beta **0.9.0-preview.3**. Driving validation with real cars and drivers is still required. See the [release notes](releases/v0.9.0-preview.3.md).

ADT now keeps phase evidence, the goals recorded with each run, immutable tune snapshots, and driver feedback together. A comparison can report **Closer to goals**, **Farther from goals**, **Tradeoff**, **No clear change**, or **Inconclusive**. A saved review separately assesses whether the driver and telemetry support improvement in the recorded recommendation test.

Local preview.21 also records supported decoded gearing/ECU meanings and car-data fingerprints with new tune snapshots. Comparisons show verified physical ratios separately from saved indexes; partial/unsupported interpretations cannot become measured power or live readback. A manual setup with unnamed VALUE entries cannot support an improvement verdict because the control cannot be identified. Wrong-car/duplicate model identities are rejected; missing model identity leaves file-derived meanings partial. Record a fresh preview.21 baseline: its packed-archive or expanded unpacked-text fingerprint covers more files than preview.20. These are snapshot-time file checks, not continuous verification of CSP overrides or active physics. See [car-data decoding](CAR_PHYSICS.md).

## Current setup capture and live evidence — local preview.14

Local preview.15 adds a separate [explicit pit Save & Apply Tune workflow](PIT_SETUP.md): generate/review in AC Setup, stage the numeric plan, then explicitly apply from the companion's Pit setup tab. Goals, findings, feedback and generation still do not apply settings. A verified pit action does not establish improved handling; a clean baseline/comparison and the existing evidence checks are still required. Real-game acceptance is pending.

With the updated CSP companion paired, Recorder can snapshot the current setup's numeric VALUE fields, including supported unsaved pit edits, without a manual file attachment. Capture is read-only and requires a compatible CSP version and verified live car/track. The saved tune stores the values, a canonical numeric fingerprint, source, layout and receipt time; raw INI metadata and pairing credentials are not saved. Manual attachment remains available. Use the same capture method and field coverage for both runs.

The short recording guide uses the existing diagnosis and the goals saved with the run: collect useful drift, add goal-relevant entries/transitions/complete angle attempts, then stop and save when ready. More details are expandable in desktop ADT, the companion and the touchscreen. Background analysis is throttled; long recordings retain all samples even when live progress updates pause. Evidence readiness is not an improvement verdict.

Existing phase, pedal, gearing and Desired Behavior calculations remain in place. The added quality guards apply when an automatically captured setup changes or monitoring is lost during a run: measurements and the immutable starting snapshot are preserved, while confident tuning, guided advancement and improvement attribution require a fresh run. Before/after scoring and driver feedback remain separate. Capture observes periodically, so changes reversed between observations may go unseen. Real CSP/mod-car acceptance, especially unsaved pit edits, still needs driving validation.

## First-run choices and guidance — local preview.12

Setup now asks **Car tuning only** or **Car + FFB**. Car-only keeps FFB fixed and skips FFB preparation and optional connection questions. Combined also explains manual FFB entry or supported SimHub/AZOM live control. Both use the same underlying telemetry diagnosis, Desired Behavior, confidence rules, comparison checks and immutable history. The selected workflow focuses the suggested test; it does not remove raw evidence or rewrite earlier runs.

New users select their own hardware, car and drift target instead of receiving the developer's startup defaults. ADT remembers these choices per Windows user. Hardware is still identified in car-only mode so comparisons refer to the same rig. Follow [the start-to-finish walkthrough](GUIDED_WORKFLOW.md) for preparation, recording, testing one change and saving a review. Keep the workflow unchanged during a comparison; changing it preserves the earlier journey and history.

## Simple results and angle goals — local preview.11

Tuning Assistant opens at **Your next step**: the goal saved with the run, what ADT noticed, confidence, and one next action. **Why this next step?** explains the evidence. **Plan this test** uses the existing recorded-test workflow; **Review comparison** shows the reason two runs cannot be compared; **Rate and save this comparison** leads to driver feedback. **Back to dashboard** returns to the recording walkthrough. It does not start a recording or apply settings by itself.

Turn on **Show advanced telemetry and recommendations** to see Assessment, Recommendations, Phase Evidence and Pedal Evidence. Before / After and Tune & Run History remain available in the simple view. Technical metric tables and comparison conditions expand separately. Detailed pedal analysis is retained, but ordinary activity does not become homework: pedal repeat-run advice requires at least three similar, complete, goal-relevant response windows during sustained drift. Such timing associations can still reflect corner exit or intentional deceleration.

To request more angle:

1. Open **Desired Car Behavior** for the selected car. Under **What angle do you want to hold?**, choose **Keep my current angle**, **Sustain more angle** or **Sustain more extreme angle**.
2. The starting ranges are 45–65° and 65–80° of body angle relative to travel. They are provisional targets, not universal car limits. **Optional angle range for this car → Choose my target range** accepts 20–85°, with at least 5° between endpoints. Steering geometry, power, grip and driving inputs constrain what is achievable.
3. Keep the handling and angle-stability preferences that describe your car. A stable/forgiving handling preset can coexist with the extreme-angle goal. Save Desired Behavior. The angle goal is evaluated separately and does not itself change generated setup, gearing or FFB values.
4. Record a fresh baseline, including at least three attempts and the return from each one. Keep recording for at least two seconds after leaving the target band. Use comparable track sections and inputs when testing a change.
5. Read **Your next step**. The optional metrics show time in the requested band, typical continuous hold, speed retained and observed settled returns. Brief peaks and above-band angle are not rewarded as a sustained hold. A control concern calls for reviewing the attempts.

Angle attempts require at least 0.5s in the band at ≥20 km/h, a continuous 0.3s entry window and 2s recovery window with fresh travel-direction data. Time below the lower bound minus 5° for at least 0.5s ends the attempt. Recovery is a proxy: forward travel below that boundary for ≥0.5s at useful speed, without a major loss of speed or backward travel. At least 0.2s backward travel or ≥90° slip, or over 40% end/entry speed loss, triggers a concern. Planned deceleration can also cause speed loss. Missing/sparse windows have unknown recovery; they cannot establish success. Scoring needs at least three complete attempts, ≥20s clean drift, and no more incomplete than complete attempts. These thresholds require driving validation.

The explicit angle goal replaces the usual 72° extreme-angle warning with the angle-attempt/control assessment. A requested high angle with speed and observed recovery is no longer treated as a loss of control solely for exceeding 72°. New runs record local longitudinal velocity because AC's existing ADT slip signal folds forward and backward travel together. Older runs remain readable but cannot establish this recovery evidence without that channel.

Goals and custom ranges persist per car, in share codes and in immutable tune/run snapshots. Remote handling updates preserve the desktop angle goal. Changing the goal or range requires a fresh baseline; older runs keep their original goals. Matching angle-goal comparisons permit changed angle exposure, while keeping unrelated handling changes descriptive and retaining speed, pedal-use and control checks. Longer holds with worse speed/recovery can yield **Tradeoff**, not an unqualified improvement. Wider-line scoring is not included: ADT does not yet have a track-position reference for the desired line.

## Pedal evidence — introduced in local preview.10

ADT now relates recorded throttle, brake and clutch changes to the surrounding car response. These signals come directly from Assetto Corsa telemetry; SimHub and AZOM are not required for this analysis.

1. Record and save a normal 60–120 second run with the correct car and driver selected. Keep your current setup for this first check.
2. Open **Tuning Assistant**, enable **Show advanced telemetry and recommendations**, then select **Pedal Evidence**. The event list shows throttle applications/lifts, brake applications/releases, throttle/brake overlap and complete clutch signal cycles near drifting.
3. Select an event. **Selected event — full explanation** wraps the entire explanation below the table, including on a narrow monitor. Scroll down to read it; the table also scrolls horizontally.
4. Read the before/after angle, yaw, speed, rear wheel-slip, RPM and steering evidence. The other pedal ranges and gear-change note help identify simultaneous inputs. Missing response windows say so instead of inventing a result.
5. Read the pedal rows in **Assessments** and **Recommendations**. They relate the evidence to that run's saved rear-grip, powered-rotation, stability, transition and initiation goals. Overlap and clutch use may be intentional; these are observations, not driver-error scores.
6. For a tune test, follow the baseline/change/repeat process below. Keep your line, assists and pedal timing similar. **Before / After** now displays pedal-use differences and explains when they make the result inconclusive.

Applications/lifts require a substantial excursion from ≤35% to ≥65% throttle or back; braking uses ≤5% and ≥20%. Both ends must be sustained for at least 0.12 seconds and the crossing must take at most 1.2 seconds. Smaller changes still contribute to the time-weighted input measures. Overlap is ≥20% throttle and ≥15% brake for at least 0.20 seconds. Clutch cycles cross the 25%/75% signal bands and return within 1.5 seconds; signal polarity is not interpreted as a physical pressed/released state.

The response compares the mean from 0.30–0.05 seconds before an event with the mean from 0.10–0.65 seconds afterward. Angle/yaw/slip/steering use magnitudes. RPM shows the peak rise across that window. Complete continuous windows at useful speed and sampling are required. Pedal analysis uses the same exclusions as phase diagnosis, so events never bridge invalid samples, freezes, telemetry gaps, pit/AI/reverse/off-track periods, impacts or a timeline restart.

A clutch cycle with a gear change is marked as potentially part of a shift. A cycle with a recorded RPM rise and throttle, without a gear change, can be labelled **possible clutch-kick pattern**. This does not confirm a technique: clutch calibration, raw signal direction and assists are not identified. There is no separate handbrake signal. Zero or unchanging values do not prove a physical pedal is present or calibrated.

The comparison adds 11 context measures during clean drift: low/high throttle time, throttle variation, mean brake input, braking time, overlap time, mean/variation of the raw clutch signal, and throttle application/lift and clutch-cycle rates. They never count as improved tuning goals. Provisional rejection tolerances are 20 percentage points for throttle bands; 15 for throttle/clutch variation, braking time and clutch mean; 10 for brake mean and overlap; and the larger of 6 events/minute or 50% of baseline for event rates. Both runs need at least 20 seconds of drift and verified signal coverage over at least 90% of it. A percentage point is the difference between percentages: 20% to 35% is 15 points.

Different pedal inputs can make a tune result inconclusive even if average throttle is unchanged. Differences remain visible. Matching these tolerances supports comparison; it does not establish identical technique or prove a setup caused the result. Thresholds are engineering heuristics requiring driving validation.

Raw saved sessions are reanalyzed when reopened. Legacy runs with unverified signal coverage remain readable, but require a fresh recording for pedal attribution. This addition preserves the existing FFB, gearing, setup-generation and calibration calculations and never writes hardware settings or Desired Behavior merely by analyzing a run.

## Try a before/after test

1. Choose **Car tuning only** or **Car + FFB**, then select the correct wheelbase, rim, drift pack, installed car and drift target. Save that car's Desired Behavior. Save/load a named baseline car setup. In Car + FFB, also generate/review the FFB tune and enter/apply the intended supported values. Car-only keeps the existing FFB fixed.
2. Enter an AC session. Open **Telemetry Recorder**, connect, enter a driver name and tune name such as `Baseline A`, and describe the layout, conditions and task. Reuse the same driver name and description for the comparison run.
3. **Attach AC Setup Snapshot** using the saved `.ini` actually used in AC. Tick the mode-specific tune-use confirmation only when true: car-only confirms the chosen setup with unchanged FFB; combined also confirms the selected FFB targets. ADT's generated AC FFB/AZOM snapshot is not proof of your live wheelbase settings.
4. Record a representative run with several initiations and transitions in both directions, plus sustained drift. Aim for at least 60–120 seconds with at least 20 seconds of clean drift. Stop and **Save Session**.
5. Open **Tuning Assistant** and read **Your next step**. Open the optional advanced view for Assessments, Recommendations, Phase Evidence and Pedal Evidence. Keep the recorded Desired Behavior fixed while testing a setup change. **Open AC Setup with Guidance** loads temporary guidance into the setup tuner; the existing Generate/Save workflow previews and writes a new setup file.
6. Test one small change in the same workflow. Return to the recorder, name the new tune `Test B`, attach the new setup, select the original run under **Recommendation baseline**, and describe the exact recommendation/change tested. Confirm tune use again. Record on the same track/layout, under comparable conditions and with similar speed, line and driving inputs; save.
7. In Tuning Assistant, select Test B and choose Baseline A under **Before / After**. Inspect metric changes, comparison limitations and the captured tune differences under **Tune & Run History**.
8. Choose your outcome rating and add notes, then **Save Run Review**. ADT retains the measured outcome, your rating and any disagreement. Repeat a comparable run before deciding to keep the change. Review drafts survive switching runs/baselines in the open window; save them to retain them after closing.

Old recordings remain readable. Their raw telemetry is reanalyzed, but ADT does not invent the missing driver, tune, historical goals or track identity. They can provide observations, not a verified improvement comparison.

## What the diagnosis measures

| Area | Evidence | Interpretation limits |
| --- | --- | --- |
| Initiation | Median rise from 5° body slip to sustained 20°, after a settled low-angle period; pedal-change clues | Needs at least three complete events. Clutch/brake/throttle clues do not confirm a driving technique; there is no separate handbrake signal. |
| Transitions | Median crossover from departing below 15° to sustained opposite 20°, with both directions shown in the event list | Incomplete reversals and discontinuities do not become completed transitions. |
| Front response / axle balance | Low-slip yaw response per steering input and sustained-drift front/rear wheel-slip shares | Proxies, not tire forces. There is no universal 50/50 slip target. Compare the same car under similar inputs. |
| Rear grip | Rear wheel-slip share relative to the recorded planted/loose preference | Throttle, tire model, loading and line also affect slip. |
| Self-steer | Steering movement during completed transitions | Driver hand torque is unavailable. This cannot distinguish hands-off self-steer from driver steering. |
| Stability | Time-normalized sustained-angle variation outside entry/transition windows; extreme-angle events | Track curvature also changes angle; an extreme-angle event is not a confirmed spin. |
| Oscillation | Clusters of at least four significant, rapid steering-rate reversals in sustained drift | Normal phase reversals are excluded; repeated driver corrections can still produce this pattern. |
| FFB | Time at or above 98% FFB magnitude while drifting | Only sustained saturation can propose a small automatic AC gain calibration delta. Steering proxies do not automatically alter wheel speed, damping or torque. |

Timing preferences use a **provisional** 0.5–1.3 second reference across the -2…+2 Desired Behavior range. This is an engineering heuristic, not a validated optimum for every car/driver. A slower requested transition is not rewarded merely for getting faster. Neutral grip/self-steer preferences remain descriptive rather than receiving an invented directional goal. Livelier angle preference does not reward oscillation or extreme-angle events.

Metrics are weighted by elapsed time, rather than giving high-rate recordings more influence. Each row shows its evidence duration, event count and confidence. Sparse evidence is displayed without an overall score for that metric. Grip and steering proxies are capped at medium confidence.

## Comparison and recommendation attribution

Overall comparison requires the same known driver, exact car/pack, wheelbase/rim, tuning workflow, track, conditions/task, session intent and recorded Desired Behavior; verified active-car identity; at least 20 seconds of usable drift and 15 Hz sampling in each run; acceptable continuity; and similar speed, angle, left/right exposure, speed-band distribution, average throttle and the pedal context measures above.

The comparison engine uses a 15% change tolerance with a minimum per-metric floor and requires at least three scored metrics. Faster or more responsive runs with worsened control can receive **Tradeoff**. Rejected comparisons still show descriptive differences and the reason they are inconclusive.

To support improvement in a **recorded recommendation test**, ADT also requires both tunes to be confirmed in use, AC setup fingerprints for both runs, a real numeric setting change, and an exact recorded plan linked to that baseline. Captured controls and before/after values must match the plan completely, with matching field coverage and capture methods; metadata-only changes, extra controls and free-text descriptions cannot meet this condition. The intended measurement must improve, the saved driver review must say Better and the overall telemetry must be closer to the recorded goals. Conflicting feedback is retained as disagreement. Multiple changes cannot isolate a single setting's effect.

These checks establish a documented association, not proof of causation or automatically learned optimal setups. Tire wear/temperatures, track conditions, practice and unrecorded setting changes may still affect the result. The system does not train a model or silently retune the car from previous runs.

## Data and recovery

- Existing raw sessions remain under `%LOCALAPPDATA%\AtomicDriftTuner\TelemetrySessions`. New JSON adds run context; existing CSV columns retain their order, with `longitudinal_velocity_ms` appended in preview.11 (blank when unavailable). The extra quality and history metadata lives in JSON.
- `%LOCALAPPDATA%\AtomicDriftTuner\RunHistory` contains `drivers`, `tunes` and `reviews`. Driver names map to persistent local IDs. Tune/review records are new files written atomically; existing versions are never overwritten.
- Each recording snapshots saved Desired Behavior, generated tune values and optional numeric AC setup values, filename and SHA-256. It does not store an absolute setup path. A tune version can remain even when recording is cancelled or the run is not saved.
- Histories are scoped to driver and rig/car. Recent selectors show up to 200 matching runs, tune versions or reviews (100 recommendation baselines in the recorder); files remain on disk beyond that display limit.
- The reader marks invalid source values before sanitizing them for display. The analyzer breaks at gaps, frozen packets and invalid samples; excludes pit limiter, AI/reverse, four wheels off-track and impact evidence; and refuses attribution after a timeline restart or interrupted run. Legacy recordings lack some exclusion signals and say so.
- Malformed history entries are skipped individually and preserved. Malformed or unsupported additive run context is treated as unknown while retaining usable raw telemetry. Saved reviews retain the outcome at review time; reopening raw sessions recalculates observations using the current analyzer.

## Existing intelligence validation (local preview.11)

Local preview.11 passes 116 regression scenarios, including 15 new angle/summary scenarios, the 14 pedal scenarios and the existing 8,000 built-in tuning-combination range sweep. New checks cover optional/custom goals, per-car/share/history storage, preserved setup/FFB values, native forward/backward velocity, held angle and speed/recovery, 15/25/50/100 Hz equivalence, peaks, invalid/frozen/excluded/gapped recordings, interrupted windows, timeline resets, unknown legacy recovery, remote handling saves, changed goals, comparison tradeoffs and the simple next step. Existing phase diagnosis, history, recorder, guided workflow, gearing and touchscreen regression checks also pass.

WPF checks cover 411 geometry assertions, 167 theme assertions, 16 companion-recorder, 50 recording-recovery, 14 gearing, 25 guided-workflow/report-binding and 17 angle-goal/next-step assertions. Angle controls, the simple result and optional advanced tables are checked at narrow, portrait, short-landscape and ultrawide dimensions. Production save/reload, preset, visibility and next-action handlers are exercised with isolated data; no game or wheelbase writes are performed. See [test commands](../tests/README.md).

Synthetic checks verify implementation behavior; they do not establish real-world tuning quality. The next acceptance step is the baseline/change/repeat driving workflow above, using the maintainer's car and driver profile.
