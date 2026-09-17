# In-game companion workflow preview

The optional CSP Lua app provides **Record**, **Findings**, **Compare** and **Help** tabs. Companion `0.2.0-preview.1` with desktop `0.9.0-preview.13` can prepare the current recording plan, explicitly confirm settings in use, record/stop/save, request saved-run findings, plan one supported recommendation and save comparison feedback. Desktop ADT stays authoritative for recording, analysis, storage and driver/car context. No tuning/diagnosis algorithms or wheelbase-write paths are changed by this extension.

See [installation and testing instructions](../companion/README.md). Build the CM-installable ZIP with `distribution/build-companion.ps1`; build desktop preview.13 with the normal package script. Content Manager installs the Lua app; ADT remains a Windows application. Restart the driving session after updating the companion so CSP reloads its manifest and code.

## Connection and recorder contract

- Reuses ADT Remote pairing (`POST /api/pair`, `X-ADT-Token`); code rotation invalidates access.
- `GET /api/companion/status` retains protocol version 1, app version, telemetry freshness, next-step instructions/completion/progress and recorder capabilities. The additive `workflow` object has its own `protocolVersion: 1`, opaque `controlVersion`, context, readiness flags and cached report. Older desktops continue to support recording; the new tabs explain which update is needed.
- `POST /api/companion/recording` accepts only `start`, `stop`, `save` plus the window ID, session ID and control version from the most recent status.
- `POST /api/companion/workflow` accepts `prepare`, `confirm`, `findings`, `plan` or `review` with the latest workflow control version. `findings` names the exact saved session; `plan` also names its displayed recommendation; `review` names the report session and carries the explicit driver rating, next action and optional notes (maximum 2,000 characters). `confirm` requires an affirmative confirmation for the current plan.
- Companion routes require a loopback caller and the existing pairing middleware. No browser CORS access is enabled. Commands are serialized, and stale recorder/plan versions are rejected before execution on the WPF dispatcher.
- Start refuses unsaved recordings, stale telemetry and mismatched desktop car/rig/driver context. Driver, conditions, baseline, setup and tune-use confirmation come from the prepared desktop recorder. Stop/save can still finish the original recorder after a dashboard-context change.
- Start/stop/save share the desktop recorder's implementation. Existing interruption handling, analysis, JSON/CSV save and guided progress callbacks are retained. Saving does not claim the run is sufficient to prove improvement.
- Saved-run report building is explicit through **Read saved run**. Normal status polling displays the cached report rather than analyzing every second. The report preserves the saved goal, confidence, recommendation reasons, measured comparison and limitations. Driver feedback is a separate saved record.
- The Lua client uses only `127.0.0.1`, polls at most once per second, keeps credentials in memory, disables all cached action buttons after a command, ignores late responses and never retries mutations automatically. A changed context or report cannot authorize a command from an old screen.
- The four tab bodies and pairing/reconnection screens use CSP `ui.childWindow` with available space. Long instructions, evidence, limitations and controls scroll at the minimum 300 × 280 window size.

Initial workflow/hardware/car/goals, setup file selection and conditions remain desktop tasks. Preparing/planning/confirming a recording never applies car or wheelbase settings. Keep/Revert in a review records the decision only. Full telemetry tables and older run history remain available in desktop ADT.

## Test focus

1. Install the ZIP through CM; launch a new session, find all four tabs and pair with desktop preview.13. Resize to the minimum size and scroll to the final action in each tab.
2. With ADT minimized, start/stop/save a prepared run. Reopen it in desktop ADT and confirm car, driver, conditions, samples and saved JSON/CSV.
3. Try start without a prepared recorder, without conditions, with AC offline, or while a previous run is unsaved. Confirm useful instructions and no discarded data.
4. Double-click commands and regenerate pairing credentials. Confirm no duplicate runs/saves and a prompt to re-pair.
5. Interrupt AC during a recording; confirm ADT retains partial evidence. Hide/reopen the panel while recording; the desktop recording should continue.
6. Change desktop car/driver between runs and confirm the old recorder cannot start a new run under the wrong selection. Confirm Stop/Save still finish its existing recording.
7. After a sufficiently clean baseline save, request Findings and compare the goal, confidence, reasons and recommendations to desktop ADT. Planning one test must not apply any settings.
8. Update the setup actually loaded in AC, check the prepared context and explicitly confirm it. Save a second run, request Compare and verify the baseline, result and limitations against the desktop report.
9. Save Better/Worse/No noticeable difference/Tradeoff feedback and a next action. Verify the saved desktop history preserves both the driver's rating and measured outcome, including disagreements. Confirm Keep/Revert does not write game/wheelbase settings.
10. Repeat after a stale connection, double click and a lost response. No command should repeat automatically. Old desktops should retain recording controls without broken workflow buttons; missing or incompatible workflow data should show an update message.

## API references

- [CSP Lua apps and manifests](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/wiki/Lua-apps)
- [CSP HTTP client implementation](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/blob/main/lib_web.lua)
- API signatures also checked against the installed `extension/internal/lua-sdk/ac_apps/lib.lua` (CSP 0.3.0-preview542). This is an API check, not an in-game compatibility test or a minimum-version guarantee.
