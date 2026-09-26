# Track tools and quick driver feedback

Available in desktop **0.9.0-preview.38** and **ADT Companion 0.5.0-preview.1**. These features are part of the normal ADT application. Existing settings, tunes, drivers, recordings and reviews stay in the usual Windows-user data folder. There is no separate Track DEV application or different Remote port to configure.

## Upgrade and start

1. Close ADT before installing this desktop update. Reopen and check **0.9.0-preview.38**.
2. Close the Assetto Corsa driving session, then open **Remote → Install / Update Companion**. Content Manager may stay open. The normal ADT Companion now includes position capture; its updater backs up changed files and leaves other mods alone. The included Content Manager ZIP is an alternative.
3. Start a new driving session and enable/open **ADT Companion**. Its update loop continues while its window is hidden. Desktop position recording does not require Remote pairing; recording controls, setup capture and pit actions still use the existing paired connection on port **5190**.
4. Use the normal desktop Recorder. Confirm your car, driver, conditions, saved goals and the setup actually loaded in AC. The **TRACK** status shows whether fresh position samples are arriving. Core telemetry still records when position is unavailable. An older companion continues its existing controls but cannot provide the new track channel.

## Quick review after a setup test

1. Save a baseline run, review one supported adjustment, save or stage a separate test setup, and load/apply it in the pits. Record a comparable repeat with the same goals and conditions. Use ADT's tracked test flow so it knows which change you tried.
2. Open **Tuning Assistant → Before / After**, choose the baseline and find **Quick driver review** beneath the setup comparison.
3. Choose **Better / Same / Worse / Couldn't judge** for each question. Better means closer to your saved goal, not automatically more angle, faster transitions or more grip. The first question follows the recorded test where available. No answer is preselected.
4. Answer whether a new problem appeared and optionally add a note. The suggested next step updates as you answer. Click **Save quick review** to retain it after closing ADT.

The next step can be **Keep and verify**, **Repeat/check disagreement**, **Review tradeoff**, **Consider the baseline**, or **Review another supported test**. ADT retains uncertainty when the comparison is unfair, the setup change is unverified or the tested metric has too little evidence. An improvement elsewhere does not validate the named change. Driver-defined tests remain driver-defined.

**Review next supported test** reassesses the selected run as a new baseline. It opens the existing exact-value setup preview only if the current evidence and supported mapping allow another proposal; otherwise it explains why another run is needed. Answering or saving a review never applies, stages or restores settings. Tuning coefficients and existing comparison tolerances are unchanged.

Draft answers survive run/baseline switching inside the current window. Saved answers reload only for the same context, goals and measured evidence. **Tune & Run History** keeps earlier reviews, including conflicting feedback. Existing overall in-game ratings and desktop notes remain available alongside the detailed desktop review. This is not automatic learning of optimal settings from accumulated experiments.

## Mark a track section

1. Record at least three passes through a familiar section, continuing beyond its start and end. Stop and save.
2. Open **Tuning Assistant → Track & sections**. Select a time on the path or use the time slider. **Mark start**, move forward in the recording, then **Mark end**.
3. Choose one continuous, unambiguous section at least two seconds and 20 metres long. Shorter sections on either side of a crossing are easier to identify than a full overlapping loop.
4. Name the section. Optionally enter a body-angle range and preferred forward gear. Save the section and goal. Every save creates a separate revision.
5. The driven reference is the default line aim. For a wider-line experiment, choose a signed map side and offset and inspect the green aim. Positive is left of travel in the map's X/right, Z/up view. You choose the intended side; ADT does not know the outside of the corner or usable road width. Recording a reference pass on your intended line is another option.
6. Choose before/after complete passes. Blue and pink show recorded paths; gold is the reference. To compare two recordings, select the baseline in **Before / After**, then return to **Track & sections**.
7. Answer the section's **Quick driver review** and save it. History is tied to the exact section revision, recording IDs and pass times. Changing the goal or either pass opens a different review.

Section goals selected after driving are retrospective review targets. They do not replace the Desired Behavior recorded with a run. Same-run passes describe consistency. Cross-run section findings are descriptive even when the global setup test matches; section feedback cannot establish a tuning cause or alter a whole-run verdict.

## What is measured

- Fresh CSP world position and car/track/layout identity accompany accepted native physics samples. Optional AI spline points appear as faint map context; they are never substituted for the intended drift line. Missing spline data leaves the recorded route usable.
- Body angle measures orientation relative to motion. Map offset measures distance from the reference route. Neither determines tyre force, road boundaries, safe width or hands-off self-steer.
- Native telemetry continues at its requested 50 Hz. Position capture uses independent local memory channels, with fresh request tokens and an odd/even sequence to reject stale or incomplete responses. No HTTP, setup or wheelbase action exists in the position publisher.
- Position is accepted only within an observed request/response window of 80 ms, physics-state age of 50 ms, and combined uncertainty of 100 ms. This is not exact tick synchronization. Missing positions are never interpolated or invented.
- Matching shares the existing clean-motion, collision, gap, pit/AI, packet-reset and backward-travel exclusions. It additionally requires known forward motion, extended signals, valid gear and at least 20 km/h. Spatial gaps over 0.2 seconds split paths.
- Each complete pass needs at least two seconds, 90% reference coverage, ten position samples/second on average and evidence in all ten route intervals. These are provisional evidence thresholds. Reference routes are limited to 20–4000 metres and 2000 points; self-crossings and ambiguous overlaps are refused. The 20-metre matching corridor is a search tolerance, not road width.
- Comparisons check direction and similar speed/pedal use by route interval. Cross-recording comparisons also retain the existing driver, car, conditions, goals, setup coverage and reliability checks. Similar means do not prove identical technique.

Old recordings without world positions remain readable; a route cannot be reconstructed from missing positions. Earlier track-prototype position records remain understood if deliberately imported, but the developer build's separate files are not migrated over existing main settings. Track updates using identical track/layout IDs are not detected by a content hash; save a fresh reference after changing a mod.

## Live checks

Verify the displayed route against what you actually drove and test position capture with the companion visible and hidden in your CSP version. Try a track with and without usable AI spline data. Pause, replay, reset/teleport, reversing, layout changes and loss of the position channel should request another pass, not create evidence across the interruption. Save/reopen reviews and switch baselines/passes to verify their scope. Compare one controlled setup change on repeated runs before judging improvement. Automated fixtures do not establish real driving quality.
