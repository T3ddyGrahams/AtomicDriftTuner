# Open-source release checklist for Atomic Drift Tuner

This checklist intentionally does not choose a license for the project owner.

## Before publishing

- Remove build output, installer artifacts, logs, telemetry recordings, local
  settings, machine paths and secrets.
- Do not commit SimHub, AZOM, MOZA, Assetto Corsa or other third-party binaries.
- Keep SimHub references as build-time references to the user's own installation.
- Review all logos, screenshots and brand assets before publishing them.
- Add a `.gitignore` for Visual Studio/.NET artifacts and Atomic local data.
- Choose and add an OSI-approved license.
- Add contribution and security-reporting guidance.
- Document exactly what the SimHub/AZOM bridge does, including the reflection-
  based compatibility path and live readback verification.
- State clearly that Atomic Drift Tuner is unofficial and not endorsed by
  SimHub, AZOM, MOZA or Kunos/Assetto Corsa unless those parties explicitly
  authorize such wording.
- Prefer reproducible release instructions so testers can build the application
  from the public source and compare it with distributed binaries.
- Publish SHA-256 hashes for release artifacts.
- Consider code-signing Windows installer/executable builds once distribution
  expands.

## Useful repository files

- `README.md`
- `LICENSE`
- `.gitignore`
- `CONTRIBUTING.md`
- `SECURITY.md`
- `CHANGELOG.md`
- `CODE_OF_CONDUCT.md` (optional but useful for a public community)
- `docs/ARCHITECTURE.md`
- `distribution/README-BETA-TESTERS.md`

## Third-party boundary

Atomic source may reference APIs/types supplied by locally installed third-party
software, but the public repository should not redistribute those proprietary
DLLs unless their licenses explicitly permit redistribution.

The current bridge project already resolves SimHub assemblies from
`$(SimHubInstallPath)` and marks them `Private=false`, which is the right general
shape for a public source repository.


## AZOM write-safety disclosure

Before public beta, document the Atomic-side write guards in the README/release
notes so reviewers can see that the reflection compatibility path is not allowed
to spam AZOM:

- explicit Apply/Revert only for the current UI,
- single-flight batches,
- duplicate live-target suppression,
- 120 ms minimum direct-write spacing,
- 350 ms readback refresh delay,
- stop-on-first-unverified batch behavior,
- 500 ms last-value-wins debounce service for any future live slider writes.

This is especially important because the exact compatibility path may enter AZOM
below some of the plugin's public UI/action-layer guards.
