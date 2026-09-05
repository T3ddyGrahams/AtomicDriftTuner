# GitHub Release Checklist

Use this checklist when preparing and publishing an Atomic Drift Tuner (ADT) release.

Release-specific features and validation requirements should be documented in the corresponding release notes or testing plan rather than permanently added to this checklist.

---

## 1. Prepare the Release

Before building:

- [ ] Confirm the intended ADT version.
- [ ] Confirm whether the release is a beta/pre-release or stable release.
- [ ] Confirm the working tree contains the intended release changes.
- [ ] Confirm documentation reflects the functionality included in the release.
- [ ] Confirm the changelog/release notes are updated.
- [ ] Confirm no temporary development files, credentials, logs, telemetry, support bundles, or build artifacts are being committed.
- [ ] Review known issues and determine whether any should block the release.

Recommended version format:

```text
v<major>.<minor>.<patch>
```

Beta/pre-release example:

```text
v0.8.1-beta.1
```

The Git tag, release title, application version, and release notes should refer to the same release version.

---

## 2. Build the Release

On the Windows build machine, open PowerShell in the repository root.

Allow local scripts for the current PowerShell session only:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

Build the GitHub release assets:

```powershell
.\build-github-release.ps1 -SimHubPath "C:\Program Files (x86)\SimHub"
```

Replace the SimHub path if SimHub is installed elsewhere.

Expected release assets are written under:

```text
artifacts\release\
```

Do not publish the release until the generated assets have been tested.

---

## 3. Validate the Build

Test the actual packaged build that will be distributed rather than relying only on a development build.

At minimum:

- [ ] Launch the generated ADT build.
- [ ] Confirm the displayed ADT version is correct.
- [ ] Confirm the application starts without unexpected errors.
- [ ] Confirm Setup & Paths behaves correctly.
- [ ] Confirm saved settings and profiles load correctly.
- [ ] Confirm Assetto Corsa car scanning works.
- [ ] Confirm active-car detection works when applicable.
- [ ] Confirm Drift Target / session intent works.
- [ ] Confirm Desired Behavior loads and saves correctly.
- [ ] Confirm tune generation works.
- [ ] Confirm AC setup recommendations can be generated.
- [ ] Confirm telemetry recording and analysis work.
- [ ] Confirm the Tuning Assistant opens and processes supported sessions.
- [ ] Confirm Appearance/theme functionality works.
- [ ] Confirm Diagnostics opens and reports expected information.
- [ ] Confirm ADT Remote works when included in the release.
- [ ] Confirm support-package generation works when applicable.

Release-specific functionality should also be tested according to the corresponding release notes or testing plan.

---

## 4. Validate SimHub / AZOM Integration

When the release includes bridge or AZOM-related functionality:

- [ ] Confirm the packaged ADT SimHub Bridge is the expected version.
- [ ] Fully exit SimHub before installing or replacing the bridge.
- [ ] Confirm **Install / Repair Packaged Bridge** completes successfully.
- [ ] Restart SimHub.
- [ ] Confirm **Atomic Drift Tuner Bridge** loads correctly.
- [ ] Confirm ADT can communicate with the bridge.
- [ ] Confirm supported live AZOM values can be read.
- [ ] Confirm supported Apply operations behave as expected.
- [ ] Confirm live readback verifies successful changes.
- [ ] Confirm a failed/unverified write is reported instead of silently treated as successful.
- [ ] Confirm Revert behaves correctly when supported.
- [ ] Confirm bridge failure or absence does not prevent normal non-bridge ADT functionality.

Only perform live hardware testing in an appropriate test environment.

---

## 5. Test Installer and Portable Builds

When both release formats are produced:

### Installer

- [ ] Install ADT using the generated installer.
- [ ] Confirm first launch succeeds.
- [ ] Confirm expected application files are installed.
- [ ] Confirm user settings/data are stored in the intended locations.
- [ ] Confirm packaged bridge installation/repair works.
- [ ] Confirm uninstall behavior is reasonable.
- [ ] Confirm upgrading from the previous supported release preserves expected user data.

### Portable

- [ ] Extract the portable ZIP to a clean folder.
- [ ] Launch ADT.
- [ ] Confirm required files are present.
- [ ] Confirm ADT does not depend on the original build directory.
- [ ] Confirm paths containing spaces work.
- [ ] Confirm settings and profile behavior are as expected.
- [ ] Confirm packaged bridge installation/repair works when applicable.

---

## 6. Review Release Assets

Before uploading:

- [ ] Confirm the installer filename contains the correct version.
- [ ] Confirm the portable ZIP filename contains the correct version.
- [ ] Confirm no debug-only files are included.
- [ ] Confirm no `.env`, credentials, tokens, logs, telemetry sessions, support bundles, or unrelated local files are included.
- [ ] Confirm third-party proprietary binaries are not included unless redistribution is explicitly permitted.
- [ ] Confirm the packaged bridge payload is intentional and permitted for distribution.
- [ ] Confirm release notes match the build being uploaded.

Recommended: calculate SHA-256 hashes for the final release assets.

Example:

```powershell
Get-FileHash .\artifacts\release\<portable-file>.zip -Algorithm SHA256

Get-FileHash .\artifacts\release\<installer-file>.exe -Algorithm SHA256
```

Record the hashes with the release information when appropriate.

---

## 7. Prepare the GitHub Release

Create a Git tag matching the application release version.

Example:

```text
v0.8.1-beta.1
```

Recommended release title format:

```text
Atomic Drift Tuner <version> — <release name or summary>
```

Example:

```text
Atomic Drift Tuner v0.8.1-beta.1 — Modern Workflow UI
```

Then:

- [ ] Create the matching GitHub tag.
- [ ] Confirm the tag points to the intended commit.
- [ ] Mark beta releases as **pre-release**.
- [ ] Use the corresponding release notes as the GitHub release description.
- [ ] Upload the portable ZIP.
- [ ] Upload the installer EXE.
- [ ] Include SHA-256 hashes when appropriate.
- [ ] Review the release page before publishing.

Do **not** publish until the version, tag, notes, and uploaded files all agree.

---

## 8. Publish

Before pressing **Publish release**, perform one final check:

- [ ] Correct version
- [ ] Correct tag
- [ ] Correct target commit
- [ ] Correct pre-release/stable status
- [ ] Correct release notes
- [ ] Correct installer
- [ ] Correct portable ZIP
- [ ] Correct hashes, if included
- [ ] No unintended files attached

Publish the release.

---

## 9. Post-Release Verification

After publishing:

- [ ] Open the public GitHub release page.
- [ ] Confirm the release appears correctly.
- [ ] Confirm both release assets are downloadable.
- [ ] Confirm filenames and version numbers are correct.
- [ ] Confirm release notes render correctly.
- [ ] Confirm any README/download links still point users to the correct location.
- [ ] Confirm automated GitHub-to-Discord release posting works when enabled.
- [ ] Confirm the release can be downloaded and launched independently of the build environment.
- [ ] Record any newly discovered release-specific issues.

If a serious problem is discovered after publishing, document the issue before deciding whether to replace assets, publish a follow-up beta, or otherwise correct the release.

---

## Release Principle

A successful build is **not automatically a successful release**.

ADT releases should be treated as ready only after the actual distributed artifacts have been tested and the release metadata, documentation, bridge compatibility, and safety-sensitive functionality have been verified.
