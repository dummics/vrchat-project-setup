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
```

Use a fresh output directory. The build emits one self-contained Windows installer, an optional-use portable ZIP, `SHA256SUMS.txt` and local `release-build.json` with measured byte sizes. The same EXE installs or updates VRC Project Setup. Its technical filename remains `Install-VrcSetup-<version>-win-x64.exe` for compatibility with installed clients. Published binaries omit debug symbols; development builds retain them. The desktop excludes unused AI/ML/Search/Widgets packages. All required runtimes remain bundled.

CI downloads the newest complete older portable release and verifies its checksum to test the unified installer through the previous app's updater. New releases publish exactly one EXE. Historical differential releases remain readable by existing clients, but separate differential installers are no longer produced.

## Verification and recovery

- Fixture tests cover exact-base selection, modified local files, bad downloads, checksum/cache behavior, reconstruction and invalid reuse metadata.
- Native UI checks run against the published self-contained desktop.
- With a previous release available, CI tests the unified GUI update/restart in an isolated copy, preserving settings and checking every installed hash. No real Unity projects are changed.
- The previous installation is retained. A failed update reopens it where possible, and the next retry uses the full installer. User data remains outside program replacement.

Evidence on 18 October 2026: 144 fixture tests, 190 native UI checks; isolated beta.6→beta.7 full update/restart in 21.3s and differential install/restart in 11s. The direct differential test does not claim a real-network GUI download. Local sizes: full installer 186,134,215 bytes versus beta.6's 214,811,948; portable 115,568,791 versus 144,249,503; differential 72,058,567 bytes. CI output may differ slightly by SDK patch/build environment. Artifacts: `.work/artifacts/2026-10-18-lean-release/`.
