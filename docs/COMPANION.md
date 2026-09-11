# In-game companion preview

The optional CSP Lua app adds recording controls, connection status and desktop Your Next Step guidance. Desktop ADT stays authoritative for recording, analysis, storage and driver/car context. No tuning/diagnosis algorithms or wheelbase-write paths are changed by this extension.

See [installation and testing instructions](../companion/README.md). Build the ZIP with `distribution/build-companion.ps1`; build desktop preview 4 with the normal package script. The two packages are separate because Content Manager installs the Lua app, while ADT remains a Windows application.

## Connection and recorder contract

- Reuses ADT Remote pairing (`POST /api/pair`, `X-ADT-Token`); code rotation invalidates access.
- `GET /api/companion/status` returns protocol version 1, app version, telemetry freshness, next-step title/instructions and current recorder capabilities.
- `POST /api/companion/recording` accepts only `start`, `stop`, `save` plus the window ID, session ID and control version from the most recent status.
- Companion routes require a loopback caller and the existing pairing middleware. No browser CORS access is enabled. Commands are serialized, and stale recorder/plan versions are rejected before execution on the WPF dispatcher.
- Start refuses unsaved recordings, stale telemetry and mismatched desktop car/rig/driver context. Driver, conditions, baseline, setup and tune-use confirmation come from the prepared desktop recorder. Stop/save can still finish the original recorder after a dashboard-context change.
- Start/stop/save share the desktop recorder's implementation. Existing interruption handling, analysis, JSON/CSV save and guided progress callbacks are retained. Saving does not claim the run is sufficient to prove improvement.
- The Lua client uses only `127.0.0.1`, polls at most once per second, keeps credentials in memory, disables stale controls, ignores late responses and never retries recording mutations automatically.

## Test focus

1. Install the ZIP through CM; find the panel in AC and pair with desktop preview 4.
2. With ADT minimized, start/stop/save a prepared run. Reopen it in desktop ADT and confirm car, driver, conditions, samples and saved JSON/CSV.
3. Try start without a prepared recorder, without conditions, with AC offline, or while a previous run is unsaved. Confirm useful instructions and no discarded data.
4. Double-click commands and regenerate pairing credentials. Confirm no duplicate runs/saves and a prompt to re-pair.
5. Interrupt AC during a recording; confirm ADT retains partial evidence. Hide/reopen the panel while recording; the desktop recording should continue.
6. Change desktop car/driver between runs and confirm the old recorder cannot start a new run under the wrong selection. Confirm Stop/Save still finish its existing recording.
7. After a sufficiently clean baseline/test save, compare the panel's next step to the desktop dashboard.

## API references

- [CSP Lua apps and manifests](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/wiki/Lua-apps)
- [CSP HTTP client implementation](https://github.com/ac-custom-shaders-patch/acc-lua-sdk/blob/main/lib_web.lua)
- API signatures also checked against the installed `extension/internal/lua-sdk/ac_apps/lib.lua` (CSP 0.3.0-preview542). This is an API check, not an in-game compatibility test or a minimum-version guarantee.
