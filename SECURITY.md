# Security Policy

## Supported Versions

Atomic Drift Tuner (ADT) is currently in public beta and under active development.

Security fixes are generally targeted at the latest publicly released beta version.

| Version | Supported |
| --- | --- |
| Latest public beta | ✅ Yes |
| Older beta releases | ⚠️ Limited |
| Development / unreleased builds | ⚠️ Best effort |

Users should reproduce security-related problems on the latest public release whenever it is safe and practical to do so.

---

## Reporting a Security Vulnerability

Please **do not open a public GitHub issue** for vulnerabilities that could expose:

- passwords;
- API keys or tokens;
- Discord credentials;
- GitHub credentials;
- private keys;
- authentication information;
- sensitive local files;
- remote-access vulnerabilities;
- arbitrary code execution;
- privilege escalation;
- other information that could put ADT users or infrastructure at risk.

Instead, use GitHub's private vulnerability reporting feature for this repository when available.

For ordinary bugs that do not expose sensitive information or create a security risk, use the repository's **Bug Report** issue form.

---

## What to Include

When privately reporting a vulnerability, include as much of the following as possible:

- affected ADT version;
- affected component;
- operating system;
- steps required to reproduce the problem;
- expected behavior;
- actual behavior;
- potential security impact;
- relevant logs or screenshots;
- whether the issue affects the desktop application, SimHub Bridge, ADT Remote, Share API, Discord bot, installer, or another component.

Please remove passwords, tokens, credentials, personal information, and unrelated private data before attaching logs or files.

---

## ADT Support Bundles

ADT diagnostic/support bundles are intended to help troubleshoot problems while minimizing unnecessary private information.

Before publishing a support bundle, users are encouraged to review its contents.

Never intentionally include:

- passwords;
- authentication tokens;
- API keys;
- private keys;
- `.env` files;
- credentials;
- proprietary third-party binaries;
- unrelated personal files.

If a support bundle unexpectedly contains sensitive information, do not upload it publicly.

---

## Third-Party Components

ADT integrates with software and hardware that may include:

- Assetto Corsa;
- SimHub;
- AZOM;
- MOZA hardware/software;
- community-created cars and drift packs.

Security vulnerabilities in those products should normally be reported to their respective developers or maintainers.

A vulnerability caused by how ADT interacts with one of those components may still be reported to the ADT project.

---

## Wheelbase Safety

Some ADT features interact with force-feedback and direct-drive wheelbase settings.

Unexpected or incorrect settings can produce substantial steering force.

If testing causes unexpected, unstable, or unsafe wheelbase behavior:

1. stop the test;
2. disable or reduce force feedback if necessary;
3. restore known-safe settings;
4. do not repeatedly reproduce the behavior solely for diagnostic purposes;
5. report the problem with the safest available reproduction information.

Security or bug investigation should never require intentionally operating hardware in an unsafe condition.

---

## Responsible Disclosure

Please allow reasonable time for a security issue to be investigated and corrected before publicly disclosing technical details that could put users at risk.

The goal is to fix legitimate security problems quickly while protecting ADT users during the process.
