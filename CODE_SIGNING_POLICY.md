# Code signing policy

Atomic Drift Tuner (ADT) is an open-source Windows application distributed from the public repository at <https://github.com/T3ddyGrahams/AtomicDriftTuner>.

**Status: Preparation only. SignPath signing is not enabled; approval and integration are still required.**

The following provider attribution applies only after acceptance and activation:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation.

## Signed project scope

The SignPath Foundation signing identity for ADT is intended only for ADT-owned Windows binaries built from source and build scripts maintained in this repository.

ADT does not use its signing identity to sign SimHub, AZOM, MOZA software, Assetto Corsa, drift-pack content, or other third-party binaries. Third-party software remains the responsibility of its respective publisher.

The signed Windows distribution is expected to cover the ADT desktop application and ADT-owned components such as the ADT SimHub Bridge when those components can be produced through the approved verifiable build path.

## Team roles

ADT is currently maintained by a single project maintainer.

- **Authors / committers:** [T3ddyGrahams](https://github.com/T3ddyGrahams)
- **Reviewers:** [T3ddyGrahams](https://github.com/T3ddyGrahams)
- **Approvers:** [T3ddyGrahams](https://github.com/T3ddyGrahams)

Changes proposed by people who are not committers are reviewed before merge. Every release signing request requires manual approval by an approver after the source revision, build origin, release version, and intended artifacts have been reviewed.

All project members with source-control or SignPath access are required to use multi-factor authentication.

## Build and signing rules

- Signed ADT binaries must originate from the public ADT repository and checked-in build configuration.
- The SignPath signing path must use verifiable CI origin information.
- For the SignPath Foundation Open Source path, jobs leading to a signing request must use GitHub-hosted runners.
- Every release-signing request requires manual approval.
- Product name metadata on ADT-owned signed binaries must identify the product as **Atomic Drift Tuner**.
- Release version metadata must be generated consistently from the intended ADT release version and enforced by the signing configuration.
- Signing secrets, certificates, and private keys are not stored in this repository.
- Third-party proprietary binaries are not committed to this repository merely to make the signing build work.
- A release is not published as signed until its final signed artifacts have been validated.

## Privacy policy

See [PRIVACY.md](PRIVACY.md).

**This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.**

User-requested network features, their destinations, and the local data ADT stores are described in the privacy policy.

## Release-page disclosure

Release notes for signed ADT releases should contain a **Code signing policy** link back to this document and identify SignPath.io / SignPath Foundation as the signing provider.

## Signing provider

- SignPath.io: <https://signpath.io/>
- SignPath Foundation: <https://signpath.org/>
- SignPath Foundation Open Source conditions: <https://signpath.org/terms.html>
