# Publishing an update

## Release presentation

- Title: the version alone, for example `0.2.0-beta.15` or `1.0.0`. Keep the immutable `v<version>` tag and existing prerelease/stable channel unchanged.
- Start notes with the marked **Download this version** badge linking directly to that release's unified Windows EXE, followed by notes/changelog. Never link users to a source archive as the installer. The release workflow generates this automatically.
- Display the installer as `VrcSetup-<version>-win-x64.exe`. Existing physical `Install-VrcSetup-...` filenames and URLs stay unchanged: installed clients discover them by exact name and verify their checksums. Display labels do not alter downloads or integrity metadata.
- A future physical filename change needs a bridge app release accepting both names before later releases switch to the short one. Keep the bridge available for older clients. Do not rename existing assets or publish a duplicate EXE just to hide the old prefix.
- `scripts/Sync-ReleasePresentation.ps1 -WhatIf` previews metadata normalization. The **Release presentation** maintenance workflow applies it using the repository-scoped Actions token and saves the old public metadata as an artifact. It changes only titles, note badges and display labels, never payloads, tags, dates or channel flags. This preserves old-client updates; downgrade behavior is a separate installer feature.

The default branch's README links directly to the newest numbered public release; the presentation script updates that link after publication, including prereleases. It also adds an explicit newest-version link above download buttons on historical releases. This avoids GitHub's prerelease suffix sorting (for example beta.9 before beta.15 on the same date), which changing a title does not fix. Do not rewrite existing tags/dates or falsely mark a beta stable to manipulate the list. Keep the README short and product-facing. The desktop source currently remains in its feature worktree; README presentation does not require merging the application.

Source work and release publication are separate. Commit the intended changes on the existing project branch; do not create a release for every source commit. `Directory.Build.props` is the single version source.

After public assets are verified/published, `Sync-ReleasePresentation.ps1 -ReadmeBranch main -FeedBranch main` refreshes the download link and the generated `.github/updates/releases.json` on `main`. Clients fetch that static raw file instead of the rate-limited REST API. The presentation-maintenance workflow can repair/refresh the feed without building another app; it skips release-intent pushes because the release workflow owns that publication. Preserve this step for future beta and stable releases.

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

Release CI runs from the release commit on its source branch, verifying that its immutable version tag points to exactly that commit. Running on the branch lets successive versions share the NuGet cache; GitHub isolates caches saved by different tags. A compatible SDK already present on the hosted Windows runner is used with `global.json`; setup-dotnet remains the fallback. No compiled output is cached. Publishing still requires an intentional `chore(release):` version commit or an explicit manual publish. Changes to the workflow/packaging scripts automatically run the full pipeline as verification only; manual `verify_only` also builds/tests without creating a release. Ordinary application commits do not trigger release builds. Build stage timings and measured artifact sizes are retained with native UI evidence. A cold dependency cache is expected once, and after dependency changes.

Measured on hosted Windows runners on 18 October 2026: beta.18 build/checks 298 seconds excluding its 17-second publication step; optimized cold-cache verification 298 seconds; optimized warm-cache verification 200 seconds (33% less). Packaging fell from 133 to 75 seconds. The warm run retained core, native UI, install/repair/uninstall, manual completion and historical update/relaunch checks. It did not publish a release, so final upload time is additional. Cache restore took 8 seconds; the dependency cache occupied about 579 MB. These are measured runs, not a fixed timing guarantee. Evidence: Actions runs 39168647877, 39169171638 and 39169578031; local `.work/artifacts/2026-10-18-ci-speed/timings.json`.

GitHub Actions owns build, tests and asset publication. Beta/RC/alpha releases are marked prerelease, while final releases are stable. Clients with beta updates disabled keep receiving stable releases only. Manual workflow runs default to draft and must use the source version from the selected ref. No VPS or client Git credentials are needed. The publishing command does not install or update the owner's local app.

## Build locally

```powershell
./scripts/Publish-Windows.ps1 -OutputDirectory .work/artifacts/my-release
```

Use a fresh output directory. The build emits one small Native AOT online installer, hash-named component ZIPs, `components.json`, an optional-use portable ZIP, `SHA256SUMS.txt` and local `release-build.json` with measured byte sizes. The same EXE installs or updates VRC Project Setup. Its technical filename remains `Install-VrcSetup-<version>-win-x64.exe` for compatibility with installed clients. Published binaries omit debug symbols; development builds retain them. All required runtimes are supplied through verified components. See [component updates and hosting migration](COMPONENT-UPDATES.md). Building the bootstrapper requires the Windows C++ toolchain; users need no development tools.

CI downloads the newest complete older portable release and verifies its checksum to test the unified installer through the previous app's updater. Before publication, tests seed the ordinary verified component cache with build output. All component ZIPs must be published with the EXE; `Release.ps1` and CI handle this. New releases publish exactly one EXE. Historical differential releases remain readable by existing clients, but separate differential installers are no longer produced.

## Verification and recovery

The native setup keeps a fixed 560 × 260 logical-pixel window for initial choices, all progress phases and completion. Phase text uses 14px Segoe UI immediately above the 24px progress bar. Shortcut choices and Finish fit the same compact window; the download/install protocol is unchanged.

The public EXE owns one native window throughout setup. The downloaded engine connects through a random, current-user-only named pipe and creates no window. Progress resets per phase: download counts missing archive bytes; preparation counts verified files; installation counts source verification, staging copies and staged verification. Unknown-duration integration uses an indeterminate bar. Detailed file operations go to `updates/components/last-engine.log`; download/setup diagnostics go to `last-install.log`. Manual Finish choices remain in the original window; automatic updates skip them.

`tests/Test-HostedSetup.ps1 -Installer <engine.exe> -TestRoot <fresh-folder> -Version <version>` verifies the real headless engine, measured progress, shortcuts, declined reinstall/downgrade and automatic completion using isolated files and shell-folder overrides. CI requires this proof before publishing. `VrcSetupSetup.exe --render-setup <folder>` captures the actual native start/download/install/completion controls offscreen without downloading or installing.

Manual installation ends on a completion page with Start (selected), desktop shortcut (optional), and launch (selected) choices, applied by Finish. Start shortcuts use the product name **VRC Project Setup** for Windows search under the current user. Reinstalling the same version or manually installing an older version requires confirmation; downgrades are not a guarantee of older settings-format compatibility. The automatic updater skips this page and reopens the app automatically.

`VrcSetupInstaller.exe --verify-manual-installer <fixture> --source-payload <payload>` exercises completion, shell shortcuts, declined reinstall/downgrade and uninstall. It requires a fresh fixture with `.manual-installer-fixture`, `VRCSETUP_SKIP_PATH_UPDATE=1`, and Start/Desktop overrides pointing to its `start` and `desktop` folders. Evidence is saved inside the fixture; real user shell folders are never used by this test.

- Fixture tests cover exact-base selection, modified local files, bad downloads, checksum/cache behavior, reconstruction and invalid reuse metadata.
- Native UI checks run against the published self-contained desktop.
- With a previous release available, CI tests the unified GUI update/restart in an isolated copy, preserving settings and checking every installed hash. No real Unity projects are changed.
- The previous installation is retained. A failed update reopens it where possible, and the next retry uses the full installer. User data remains outside program replacement.

Evidence on 18 October 2026: 144 fixture tests, 190 native UI checks; isolated beta.6→beta.7 full update/restart in 21.3s and differential install/restart in 11s. The direct differential test does not claim a real-network GUI download. Local sizes: full installer 186,134,215 bytes versus beta.6's 214,811,948; portable 115,568,791 versus 144,249,503; differential 72,058,567 bytes. CI output may differ slightly by SDK patch/build environment. Artifacts: `.work/artifacts/2026-10-18-lean-release/`.
