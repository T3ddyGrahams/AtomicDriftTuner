# AZOM Live Integration — v0.7.2

## Boundary

Atomic Drift Tuner does not edit AZOM configuration files and does not implement
MOZA's hardware protocol itself.

The integration is split between:

1. the .NET 8 Atomic desktop application;
2. `AtomicDriftTuner.SimHubBridge.dll`, which runs inside SimHub.

The desktop app communicates with the bridge over the local named pipe:

`AtomicDriftTuner.AzomBridge.v1`

## Read path

The bridge captures known `AZOM.*` values only on SimHub's `DataUpdate` thread
and stores a cached snapshot. Named-pipe threads return that cache; they do not
perform cross-thread AZOM reflection.

Atomic uses live readback after each requested setting change. A setting is not
reported as successful unless the requested target is observed in the live
snapshot.

## Write transport order

For supported settings Atomic uses:

1. **Exact AZOM commit** — the bridge uses the running AZOM plugin instance and
   AZOM's Base-setting catalog/registrar path (`StepBaseSetting`, `SetToggle`,
   and compatible AZOM commit methods).
2. **Registered SimHub action fallback** when the exact path is unavailable or
   fails verification.
3. **SimHub CLI action fallback** last.

The exact path is used first because it is the path proven to reach exact target
values on the development/test installation. The public action paths remain
fallback transports.

## Atomic write guards

Because the compatibility path enters AZOM through an internal commit method,
Atomic does not rely exclusively on any outer duplicate/rate guards that AZOM
may have around its public UI/action layer.

v0.7.2 adds guards on Atomic's side:

### Explicit Apply/Revert

- Atomic only writes after an explicit Apply/Revert action.
- Only one Apply/Revert batch may run at a time in the desktop process.
- Settings are written and read back sequentially.
- The bridge suppresses a request when the corresponding live `AZOM.*` property
  already matches the requested target.
- Direct bridge commits are separated by at least 120 ms.
- The bridge never sleeps on SimHub's DataUpdate thread; when the spacing window
  has not elapsed, the request is requeued for a later update.
- Existing 350 ms fresh-read delay remains in the desktop controller so each
  setting crosses the bridge's ~5 Hz snapshot refresh boundary before
  verification.
- A batch stops at the first unverified setting.

### Interactive / future slider writes

Atomic does not currently write AZOM on every slider movement.

`AzomInteractiveWriteService` exists specifically for any future live-edit UI.
It implements:

- default 500 ms debounce;
- last-value-wins behavior;
- superseded values are discarded before reaching the bridge;
- the same guarded direct-write path is used after the debounce window.

A future slider implementation must use this service rather than calling the
direct bridge path for every value-change event.

## Duplicate behavior inside AZOM

For modern numeric settings, AZOM's own catalog path already returns without a
write when the current display value equals the target. Atomic's bridge-level
live-property check is an additional guard, not a replacement for AZOM's own
logic.

## Ordering and verification

`RoadSensitivity` remains ordered before individual equalizer bands because an
AZOM sensitivity preset may rewrite the equalizer curve.

After a write:

`request -> guarded commit -> wait for cache refresh -> live snapshot -> exact target?`

Only the final live value determines success.

## EQ firmware safety

Atomic currently models the supplied six-band Equalizer layout. When AZOM
reports bands 7–10, Atomic reads them but skips automatic custom EQ-band writes
until the tuning engine has a frequency-safe 10-band target model.

## Revert

Before Apply, Atomic saves the live snapshot plus the exact property names the
batch intends to change. Revert only targets that property set and uses the same
guarded/verified write pipeline.
