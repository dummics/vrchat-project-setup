# Application update verification

The normal updater accepts only published assets from this repository's GitHub Releases. Its URL policy is unchanged by the local test; there is no configurable production feed or local web server.

## Repeat the local update

Use PowerShell 7 from this feature worktree. Build a base and a strictly newer installer:

```powershell
./scripts/Publish-Windows.ps1 -Version 0.2.0-beta.1 -OutputDirectory .work/artifacts/update-check/base
./scripts/Publish-Windows.ps1 -Version 0.2.0-beta.2 -OutputDirectory .work/artifacts/update-check/candidate
./scripts/Test-ApplicationUpdate.ps1 `
  -BasePayload .work/artifacts/update-check/base/VrcSetup-0.2.0-beta.1-win-x64 `
  -UpdateInstaller .work/artifacts/update-check/candidate/Install-VrcSetup-0.2.0-beta.2-win-x64.exe
```

Choose new build output folders when repeating a publish. The test automatically chooses a fresh evidence folder unless `-OutputDirectory` is supplied. It refuses an existing evidence destination, verifies both source payload and candidate checksum, and copies the base into an isolated `install/` with its own settings, projects directory and Start menu destination. User PATH updates are disabled only in the test child process.

The hidden native app runs the normal viewmodel update commands. A fixture HTTP handler supplies GitHub-shaped metadata and streams the actual local candidate installer. Normal release selection, SHA-256 verification, installer process, wait-for-exit, program replacement and relaunch all execute. The relaunched candidate verifies its version, every installed payload hash, unchanged fixture settings, preserved previous installation and absence of a repeated update offer, then exits.

Inspect `result.json` (`Passed: true`), `handoff.json`, `home/updates/last-result.json` and the before/after native screenshots. Test copies/backups are kept as evidence. No real avatar imports, owner installation changes or owner settings are part of this script. A timeout preserves evidence; inspect the reported isolated process paths before retrying. Do not run diagnostic test switches against a normal install: the app requires the dedicated fixture marker and install path.

## Actual online update

The local test verifies the complete installer/restart pipeline, with HTTP transport supplied by files. It does **not** verify GitHub's live download/redirect path or publishing permissions.

For a real beta.1 → beta.2 check, publish the newer complete GitHub release with its installer and `SHA256SUMS.txt`, then use the installed app's version menu → Check for updates → Install update and restart. Finish imports and save/revert pending package edits first. The current beta remains installed until that action; publishing a test candidate does not itself replace local programs.

The release workflow creates a reviewable draft and the Pages workflow is separate. On 18 October, HTTPS Git tag pushes triggered the release workflow without changing local credentials. The first run failed because PowerShell split the native smoke-test path into separate arguments; direct `ProcessStartInfo.ArgumentList` fixed the launch. The beta.2 run passed all 126 fixture tests and 146 native UI checks, then successfully created a draft prerelease with the installer, portable ZIP and checksum file. [Successful workflow](https://github.com/dummics/vrchat-project-setup/actions/runs/39089138768). The draft is visible to an owner with release access; the assistant connector cannot retrieve it. Creation/upload success is evidenced by the workflow, rather than an independent asset download.

The owner installation remains beta.1, ready for the live update trial. Review and publish the beta.2 draft before checking for updates; drafts are deliberately ignored by the app. No public release or Pages deployment was performed. The live GitHub download/redirect path remains to be tested.

## Verified run, 18 October 2026

`scripts/Test-ApplicationUpdate.ps1` completed beta.1 → beta.2 in about 10 seconds using the real installer and two distinct app processes. Settings, installed payload hashes, previous-program backup and no-repeat behavior passed. Owner program marker, config, import journal, legacy launcher, Start menu shortcuts and user PATH remained unchanged throughout the isolated test. Evidence: `.work/artifacts/2026-10-18-update-e2e/verified-run/`.
