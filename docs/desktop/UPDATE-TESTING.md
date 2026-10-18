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

### Installed-shortcut regression (beta.5)

The owner's beta.3 update failure was reproduced by starting the beta.1 base from its own install directory. The old installer inherited that current directory and failed at MoveDirectory, exactly as in the owner receipt. The harness now always sets base WorkingDirectory to install, rather than the repository. Beta.5 independently relocates the installer to Temp, also fixing handoff from older clients; its own GUI explicitly launches the installer from data home. Keep this entry-point condition in future update tests. Normal updates show a compact progress/result window and reopen automatically. Diagnostic fixture updates suppress the window, preserve logs and use the same installer operations.

**Verified beta.1 → public beta.3 on 18 October 2026:** `-UseRealNetwork` completed in 27.4 seconds, including public feed discovery, GitHub asset redirects, 205 MiB installer download, SHA-256, real installer replacement and candidate relaunch. `RealNetworkUsed` and `RealInstallerUsed` are true. All payload hashes, complete settings hash, previous installation and no-repeat behavior passed. A subsequent independent installed CLI launch recovered the same data home, saved editor path, Dark theme and hide-completed preference. Owner config/journal/program marker/legacy launcher hashes were unchanged. Evidence: `.work/artifacts/2026-10-18-editor-release/online-run/`.

Public prerelease: https://github.com/dummics/vrchat-project-setup/releases/tag/v0.2.0-beta.3 . Source 4523821; successful workflow 39121554963. Local verification: 130 fixture tests and 150 native UI checks, including the installed pinned editor on Dom's PC. CI also passed fixture/native checks before publication; discovery checks requiring an installed editor are conditional there. No real Unity project imports/package mutations were used for this distribution test. Owner's active app remains beta.1 until the normal update command is selected.

The beta.3 test entry point also supports `-UseRealNetwork -TimeoutSeconds 600`. Build a base payload from beta.3 source with version beta.1, provide the published beta.3 installer as `-UpdateInstaller`, and use a fresh output directory. In this mode the app uses its normal HTTP client, public GitHub feed, redirects and asset download; the supplied installer only defines the expected version/checksum fixture. No transport is substituted. Successful evidence must explicitly report `RealNetworkUsed: true`. The isolated app/installer still cannot alter owner data or shortcuts.

The fixture includes a saved editor path on an unavailable drive plus appearance/import preferences. Its complete configuration hash must survive replacement and candidate startup. Native UI checks separately verify that a found editor is persisted immediately and survives reload. Normal installed copies record their data home in the installation marker, and installer relaunch passes the same home to the new process.

From beta.3 onward, tag pushes publish releases after all checks pass; manual runs remain drafts by default. Beta/rc tags remain prereleases. Earlier beta.2 draft evidence below is historical.

Without `-UseRealNetwork`, the local test verifies the complete installer/restart pipeline with HTTP transport supplied by files. That mode does **not** verify GitHub's live download/redirect path or publishing permissions.

For the normal owner update, use the installed app's version menu → Check for updates → Install update and restart. Finish imports and save/revert pending package edits first. The current beta remains installed until that action; publishing a candidate does not itself replace local programs.

Historical beta.2 preparation used a draft-only release workflow; Pages remained separate. HTTPS Git tag pushes triggered the workflow without changing local credentials. The first run failed because PowerShell split the native smoke-test path into separate arguments; direct `ProcessStartInfo.ArgumentList` fixed the launch. The beta.2 run passed all 126 fixture tests and 146 native UI checks, then created a draft prerelease with installer, portable ZIP and checksum file. [Beta.2 workflow](https://github.com/dummics/vrchat-project-setup/actions/runs/39089138768). Its draft was not independently readable by the assistant connector. The public beta.3 verification above supersedes this earlier limitation.

The owner installation remains beta.1 until the update action is selected. Beta.3 is publicly discoverable; the old beta.2 draft remains ignored. No Pages deployment was performed.

## Verified run, 18 October 2026

`scripts/Test-ApplicationUpdate.ps1` completed beta.1 → beta.2 in about 10 seconds using the real installer and two distinct app processes. Settings, installed payload hashes, previous-program backup and no-repeat behavior passed. Owner program marker, config, import journal, legacy launcher, Start menu shortcuts and user PATH remained unchanged throughout the isolated test. Evidence: `.work/artifacts/2026-10-18-update-e2e/verified-run/`.
