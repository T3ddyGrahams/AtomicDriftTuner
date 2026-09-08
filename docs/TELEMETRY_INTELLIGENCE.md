# Telemetry intelligence preview

Implemented in the local **0.9.0-preview.1** development build. This is not a new public beta release. Driving validation with real cars and drivers is still required.

ADT now keeps phase evidence, the goals recorded with each run, immutable tune snapshots, and driver feedback together. A comparison can report **Closer to goals**, **Farther from goals**, **Tradeoff**, **No clear change**, or **Inconclusive**. A saved review separately assesses whether the driver and telemetry support improvement in the recorded recommendation test.

## Try a before/after test

1. Select the correct wheelbase, rim, drift pack and installed car. Save that car's Desired Behavior in AC Car Setup. Generate your ADT tune and use the intended settings in AC/AZOM.
2. Enter an AC session. Open **Telemetry Recorder**, connect, enter a driver name and tune name such as `Baseline A`, and describe the layout, conditions and task. Reuse the same driver name and description for the comparison run.
3. **Attach AC Setup Snapshot** using the saved `.ini` actually used in AC. Tick the tune-use confirmation only if you are using the generated ADT targets and that setup. ADT captures generated AC FFB/AZOM targets; it cannot read back your live wheelbase settings.
4. Record a representative run with several initiations and transitions in both directions, plus sustained drift. Aim for at least 60–120 seconds with at least 20 seconds of clean drift. Stop and **Save Session**.
5. Open **Tuning Assistant**. Read Assessment, Recommendations and Phase Evidence. Keep the recorded Desired Behavior fixed while testing a setup change. **Open AC Setup with Guidance** loads temporary guidance into the setup tuner; the existing Generate/Save workflow previews and writes a new setup file.
6. Test one small change. Return to the recorder, name the new tune `Test B`, attach the new setup, select the original run under **Recommendation baseline**, and describe the exact recommendation/change tested. Confirm tune use again. Record on the same track/layout, under comparable conditions and with similar speed, line and driving inputs; save.
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

Overall comparison requires the same known driver, exact car/pack, wheelbase/rim, track, conditions/task, session intent and recorded Desired Behavior; verified active-car identity; at least 20 seconds of usable drift and 15 Hz sampling in each run; acceptable continuity; and similar speed, angle, left/right exposure, speed-band distribution and throttle.

The comparison engine uses a 15% change tolerance with a minimum per-metric floor and requires at least three scored metrics. Faster or more responsive runs with worsened control can receive **Tradeoff**. Rejected comparisons still show descriptive differences and the reason they are inconclusive.

To support improvement in a **recorded recommendation test**, ADT also requires both tunes to be confirmed in use, AC setup fingerprints for both runs, a captured tune change, an explicit reference to the baseline recommendation and a description of the change tested. The saved driver review must say Better and the telemetry must be closer to the recorded goals. Conflicting feedback is retained as disagreement. Multiple changes cannot isolate a single setting's effect.

These checks establish a documented association, not proof of causation or automatically learned optimal setups. Tire wear/temperatures, track conditions, practice and unrecorded setting changes may still affect the result. The system does not train a model or silently retune the car from previous runs.

## Data and recovery

- Existing raw sessions remain under `%LOCALAPPDATA%\AtomicDriftTuner\TelemetrySessions`. New JSON adds run context; CSV remains compatible with the existing columns. The extra quality and history metadata lives in JSON.
- `%LOCALAPPDATA%\AtomicDriftTuner\RunHistory` contains `drivers`, `tunes` and `reviews`. Driver names map to persistent local IDs. Tune/review records are new files written atomically; existing versions are never overwritten.
- Each recording snapshots saved Desired Behavior, generated tune values and optional numeric AC setup values, filename and SHA-256. It does not store an absolute setup path. A tune version can remain even when recording is cancelled or the run is not saved.
- Histories are scoped to driver and rig/car. Recent selectors show up to 200 matching runs, tune versions or reviews (100 recommendation baselines in the recorder); files remain on disk beyond that display limit.
- The reader marks invalid source values before sanitizing them for display. The analyzer breaks at gaps, frozen packets and invalid samples; excludes pit limiter, AI/reverse, four wheels off-track and impact evidence; and refuses attribution after a timeline restart or interrupted run. Legacy recordings lack some exclusion signals and say so.
- Malformed history entries are skipped individually and preserved. Malformed or unsupported additive run context is treated as unknown while retaining usable raw telemetry. Saved reviews retain the outcome at review time; reopening raw sessions recalculates observations using the current analyzer.

## Validation for this preview

40 automated regression scenarios pass, including 22 intelligence/history scenarios and the existing 8,000 built-in tuning-combination range sweep. Coverage includes phase detection, sample-rate changes, gaps, frozen/invalid frames, impacts, restarts, weak evidence, context mismatch, preference direction, control tradeoffs, conflicting feedback, setup snapshots, immutable history and legacy loading.

201 WPF geometry assertions pass, including scrolling to the new recorder fields, baseline selection, driver feedback and Save Run Review at narrow, portrait, short-landscape and ultrawide dimensions. Production markup/styles are tested without business event handlers or hardware services. See [test commands](../tests/README.md).

Synthetic checks verify implementation behavior; they do not establish real-world tuning quality. The next acceptance step is the baseline/change/repeat driving workflow above, using the maintainer's car and driver profile.
