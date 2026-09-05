# Public Repository Contents

This repository contains the source code, build scripts, documentation, issue
forms, deployment definitions, and installer/build configuration required to
inspect, build, test, deploy, and contribute to Atomic Drift Tuner (ADT) and
its supporting open-source components.

This may include:

- ADT desktop application source code;
- ADT SimHub Bridge source code;
- Discord bot source code;
- Share API source code;
- deployment/service definitions;
- build and packaging scripts;
- installer definitions;
- public documentation;
- GitHub issue forms and repository configuration;
- non-sensitive example configuration files such as `.env.example`.

It intentionally does **not** contain:

- compiled EXE/DLL/MSI files;
- Visual Studio `bin`, `obj`, `.vs`, user, or debug-symbol files;
- generated release artifacts;
- `node_modules` or other generated dependency directories;
- local ADT settings or calibrations;
- telemetry recordings;
- logs, crash dumps, or runtime databases;
- exported support packages;
- personal or machine-specific paths and data;
- `.env` files containing real configuration values;
- credentials, passwords, API keys, tokens, private keys, or other secrets;
- proprietary SimHub, AZOM, MOZA, Assetto Corsa, or drift-pack binaries;
- temporary development notes or obsolete internal release-preparation files.

Compiled tester builds belong on the matching **GitHub Release**, not in the
source repository.

Third-party assemblies required by the SimHub Bridge must come from the user's
own legitimate local installation unless their licenses explicitly permit
redistribution.

Deployment examples may contain generic service-account names, filesystem
locations, ports, or other non-secret configuration needed to demonstrate a
working deployment. Real credentials and environment-specific secrets must
remain outside the repository.

Before publishing a release or committing deployment-related changes, review
the affected files for credentials, private data, unintended runtime state,
and third-party files that are not permitted for redistribution.
