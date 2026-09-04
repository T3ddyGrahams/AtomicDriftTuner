# Atomic Share Codes — Phase 1

Atomic Share Codes are a portable representation of a generated Atomic tune context and a safe recommendation snapshot.

## Current test format

- Schema: `atomic-share/v1`
- Text prefix: `AT1-`
- Encoding: compact JSON → GZip → Base64URL

The first test format is deliberately self-contained so the Windows app can validate the payload before any server or Discord integration is introduced.

## What an AT1 code contains

- Atomic version and creation time
- wheelbase identity and tuning-relevant hardware values
- steering-wheel identity and tuning-relevant wheel values
- drift pack
- car tuning context
- car's AC folder **name** when available
- Drift Target
- seven Desired Behavior target values
- a review snapshot of generated performance-oriented AZOM recommendations
- Assetto Corsa FFB recommendations
- Self-Steer / Stability / Detail scores
- estimated peak wheel torque
- bounded tune notes

## What an AT1 code does NOT contain

- full Assetto Corsa paths or Windows user-profile paths
- telemetry recordings
- saved calibration history
- app settings
- SimHub/AZOM credentials
- Atomic Remote pairing tokens
- Discord token or account credentials
- arbitrary files
- preference-style AZOM settings such as Bluetooth, status LED, standby or protection preferences

## Import safety

Import is intentionally non-authoritative for hardware writes.

1. Atomic decodes and validates the share payload.
2. The user reviews the snapshot.
3. The user explicitly chooses **Load Context + Regenerate Locally**.
4. Atomic loads the shared hardware/wheel/pack/car/Drift Target context.
5. Atomic regenerates through the receiver's normal local tuning engine, local calibration and local AZOM preferences.
6. Nothing is written to AZOM by the import action.

The shared recommendation snapshot is therefore useful for comparison and future Discord display, but is never treated as a remote hardware command.

Desired Behavior import is a separate checkbox and is OFF by default. When enabled, only the seven bounded `-2..+2` behavior targets are saved.

## Validation limits

The decoder:
- requires the exact v1 schema;
- checks supported enum values;
- validates hardware, car, AZOM, AC FFB, score and behavior ranges;
- limits compressed input size;
- limits decompressed JSON size;
- limits note count and text lengths.

These checks are required before the same format is accepted by a public API.

## Phase 2: short server IDs

Once Phase 1 is validated, the server can store the exact same payload and issue a short identifier:

```text
AT-7K4D2P
```

Proposed flow:

```text
Windows Atomic
    ↓ validated atomic-share/v1 payload
Atomic Share API
    ↓
AT-7K4D2P
    ↓
Discord /tune AT-7K4D2P
```

The API and Discord bot will only deal with tune/profile data. Hardware writes remain local to the Windows Atomic application.
