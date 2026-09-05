# GitHub Release Checklist — v0.8.1-beta.1

Recommended tag: `v0.8.1-beta.1`

Recommended title: `Atomic Drift Tuner v0.8.1-beta.1 — Modern Workflow UI`

1. On the Windows build machine, open PowerShell in the repository root.
2. Allow local scripts for this PowerShell session only:
   `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`
3. Build the release assets, replacing the SimHub path as needed:
   `.\build-github-release.ps1 -SimHubPath "C:\Program Files (x86)\SimHub"`
4. Runtime-test the generated app before publishing.
5. Verify the six-step tuning workflow and all embedded tool navigation.
6. Verify Appearance opens separately and does not replace the active embedded page.
7. Verify a scan containing multiple `matsuri_mayhem_*` cars produces the auto-detected **Matsuri Mayhem** pack.
8. Verify live AZOM operations only after confirming the expected bridge and hardware environment.
9. Create GitHub release tag `v0.8.1-beta.1` and mark it as a **pre-release**.
10. Paste the contents of `RELEASE_NOTES_v0.8.1-beta.1.md` into the GitHub release description.
11. Upload the portable ZIP and installer EXE from `artifacts\release`.
12. Publish the pre-release.
