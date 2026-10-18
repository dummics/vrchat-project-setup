# Publishing an update

Source work and release publication are separate. Commit the intended changes on the existing project branch; do not create a release for every source commit. `Directory.Build.props` is the single version source.

```powershell
# Preview: does not edit files, commit, tag or push.
./scripts/Release.ps1 -NextBeta

# Publish the next beta after source changes have been committed.
./scripts/Release.ps1 -NextBeta -Publish

# Start another series, an RC, or a final release.
./scripts/Release.ps1 -Version 1.0.0-rc.1 -Publish
./scripts/Release.ps1 -Version 1.0.0 -Publish
```

The publish command checks the clean working tree, version ordering, unused tag and remote branch ancestry. It commits only the version bump, creates the immutable tag, then pushes branch and tag atomically over HTTPS. It uses existing Git authentication and does not create a branch or change credentials. If a push fails, retain the version commit/tag, inspect the failure and retry that same atomic push; never move or overwrite a published tag.

GitHub Actions owns build, tests and asset publication. Beta/RC/alpha releases are marked prerelease, while final releases are stable. Clients with beta updates disabled keep receiving stable releases only. Manual workflow runs default to draft and must use the source version from the selected ref. No VPS or client Git credentials are needed. The publishing command does not install or update the owner's local app.

## Build locally

```powershell
./scripts/Publish-Windows.ps1 -OutputDirectory .work/artifacts/my-release
# Optional, checksum-verified portable archive of the exact earlier base:
./scripts/Publish-Windows.ps1 -OutputDirectory .work/artifacts/my-delta-release `
  -BaseArchive C:/path/to/VrcSetup-0.2.0-beta.6-win-x64.zip
```

Use a fresh output directory. The build emits the complete portable ZIP, self-contained installer, optional `Update-VrcSetup-<target>-from-<base>-win-x64.exe`, `SHA256SUMS.txt` and local `release-build.json` with measured byte sizes. Published binaries omit debug symbols; development builds retain them. The desktop references only its WinUI, windowing and text components, excluding unused AI/ML/Search/Widgets packages without deleting arbitrary runtime files. All required runtimes remain bundled.

CI automatically downloads the newest complete older release and verifies its archive checksum to build one differential installer. Skipping that base version uses the full installer. New runtime/dependency files travel in the differential package automatically; unchanged files are identified by path and SHA-256. It is a file-level differential, not a binary patch format.

## Verification and recovery

- Fixture tests cover exact-base selection, modified local files, bad downloads, checksum/cache behavior, reconstruction and invalid reuse metadata.
- Native UI checks run against the published self-contained desktop.
- With a previous release available, CI tests both the full GUI update/restart and direct differential installation/restart in isolated copies, preserving settings and checking every installed hash. No real Unity projects are changed.
- The previous installation is retained. A failed update reopens it where possible, and the next retry uses the full installer. User data remains outside program replacement.

Evidence on 18 October 2026: 144 fixture tests, 190 native UI checks; isolated beta.6→beta.7 full update/restart in 21.3s and differential install/restart in 11s. The direct differential test does not claim a real-network GUI download. Local sizes: full installer 186,134,215 bytes versus beta.6's 214,811,948; portable 115,568,791 versus 144,249,503; differential 72,058,567 bytes. CI output may differ slightly by SDK patch/build environment. Artifacts: `.work/artifacts/2026-10-18-lean-release/`.
