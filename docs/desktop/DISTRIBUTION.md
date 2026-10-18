# VRChat Project Setup for Windows

The Windows app and vrcsetup command use the same C#/.NET 10 engine.

## Install

Run Install-VrcSetup-<version>-win-x64.exe. It is self-contained: no .NET SDK or Visual Studio is required. Install for the current user, then open **VRChat Project Setup** from Windows Search.

Public beta installer: [0.2.0-beta.5](https://github.com/dummics/vrchat-project-setup/releases/tag/v0.2.0-beta.5), Windows x64. The installer is currently unsigned, so Windows may show a publisher/reputation prompt on a browser download. SHA-256 is available alongside it. Public download and the app's full update/restart path have been verified in an isolated installation; avatar import acceptance on other PCs remains beta testing.

Current numbering is **0.2.0-beta.5**. The earlier 2.0 previews were development builds, not a stable 2.0 release. Installing the beta over a preview preserves user settings, projects and a previous-program backup. The saved editor is retained; the app searches Unity Hub metadata/custom paths, default installations and Windows installed-program records for the pinned Unity 2022.3.22f1, checking executable product metadata. Settings offers Find editor, Choose file and the official Unity installation guide; imports and Open project can configure the editor before executing. A manual editor choice is saved immediately. Installed copies remember their data home across independent launches.

## Updates and sharing

Give friends the self-contained installer, or a published asset from [GitHub Releases](https://github.com/dummics/vrchat-project-setup/releases). Clients need neither Git nor a GitHub token. No VPS is used. The version menu at the bottom of the app offers update checks, release downloads and Report a bug. Startup checks are asynchronous and quiet; Settings → Updates controls startup checks and beta inclusion.

When a newer complete release is available, choose **Install update and restart**. A compact updater window shows the installation phase and result, then reopens the app automatically. It also tries to reopen the preserved app if installation fails. The app downloads the installer from this repository, verifies its SHA-256 against that release's `SHA256SUMS.txt`, waits for imports and pending edits to finish, closes normally, installs and reopens. It never kills Unity or a running import. The installer refuses replacement while another installed GUI/CLI process remains active and preserves the previous installation. Portable copies link to Releases instead of replacing their own folder. Offline/rate-limit failures are non-blocking; manual retries stay in the status bar.

Maintainers use [the release command](RELEASING.md). An explicit version tag validates the single source version, runs fixture/native UI checks, builds installer+ZIP+checksums and an optional differential installer, verifies full/differential installation in isolated copies, then publishes. Alpha/beta/rc versions are prereleases; final `x.y.z` releases are supported. A manual run defaults to a draft unless Publish is selected. Drafts and incomplete releases are ignored by clients. Existing release tags/assets are never overwritten. Ordinary source commits do not publish updates.

From beta.7, compatible installed copies automatically prefer the smaller differential installer. It contains changed files and reuses unchanged files only from the exact base version after hash validation. The reconstructed installation is fully verified and uses the same staging/backup/restart path as a full update. Missing/modified bases, skipped versions or failed differential downloads use the full installer. A retry after an installer failure also uses the full installer. Beta.6 and older clients still download the full installer for the transition. No additional runtime installation is required.

`docs/site/index.html` is a dependency-free download page using the same public Releases API, with fallback links when GitHub is unavailable. Enable GitHub Pages with the GitHub Actions source, then run `download-page.yml` after the workflows are integrated into the default branch. No Pages deployment or public beta publication is implied by a local build. The feature branch remains separate until owner-approved integration.

The repeatable [local update test](UPDATE-TESTING.md) exercises the actual GUI commands, checksum, installer and relaunch against a newer build in an isolated copy. It needs no server or GitHub credentials and leaves the owner's installation and projects untouched. The live GitHub transport/publishing check remains separate.

Report a bug opens a new repository issue with the app version and report prompts prefilled, even before the feature branch is integrated. The user reviews and submits it on GitHub; no issue, screenshot or log is sent automatically. A structured web form template becomes active when integrated into the default branch. Sources: [GitHub release API](https://docs.github.com/en/rest/releases/releases), [issue form fields](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/syntax-for-githubs-form-schema), [GitHub Pages workflows](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).

Unity Editor and its selected build modules, VRChat Creator Companion and the VPM CLI remain content-creation prerequisites. VPM has its own runtime requirements; the app checks tool failures and never treats an incomplete import as ready. No VRChat login is requested by VRCSETUP.

The application is a community utility, independent of VRChat Inc. Unsigned test builds may show Windows reputation prompts. Verify SHA256SUMS.txt; do not disable Windows security settings to run a test build.

Packages keeps Preset packages and My packages tabs above their content. The centered preset selector belongs only to Preset packages, with previous/next arrows and a dropdown; package/preset menus hold secondary actions, and save controls appear for pending edits. Version changes preserve existing rows, and successful saves receive a brief footer confirmation. Preview 13 moves recovery actions into each Activity entry, with Imports/Operations history and individual logs. It was installed on Dom's request on 17 October 2026; installed payload hashes and preserved settings/history/legacy launcher were verified.

Preview 6 includes the preview 5 package workflow: My packages and project Add package share an in-page local/online browser. Version choices are row dropdowns with a beta filter; Latest retains the shared `latest` policy. Project changes remain staged until Apply, with Back/Revert and confirmation before leaving a pending draft.

Narrow windows use compact navigation and vertically stacked creation fields. The shared package-selection dialog prioritizes a tall, flat list, inline versions and small footer actions. Search runs as you type; favorites update in place without moving cards. Favorite ordering/filter membership refreshes when re-entering the page or changing its filters.

## Portable

The [multi-import workspace](IMPORT-QUEUE.md) supports parallel imports without a fixed user-facing worker limit, per-project progress/cancel, logs and explicit recovery. Resource warnings stay inline. Analysis cache maintenance is in Settings.

Preview 8 includes offline UnityPackage inspection and avatar-focused package suggestions. Dom authorized upgrading preview 6 after code review, compilation and payload-integrity verification, without direct functional tests. Version lookups and search results do not automatically become personal packages. `vrcsetup inspect <file> --json` and `--deep` use the same local analyzer as Create; neither starts Unity or installs dependencies. New candidate signatures from the supplied research await source verification. See [inspection behavior](PACKAGE-INSPECTION.md).

Extract VrcSetup-<version>-win-x64.zip, then run VRChat Project Setup.bat or app/desktop/VrcSetup.Desktop.exe. The portable marker keeps settings under data/ in the extracted folder. Use bin/vrcsetup.cmd for the terminal.

## CLI

After installation, open a new terminal and run vrcsetup help. vrcsetup without arguments opens the terminal UI; vrcsetup gui opens the Windows UI. PowerShell 5.1 and PowerShell 7 can both invoke it.

Projects, package edits, preset operations and UnityPackage creation retain their existing commands, -Json and -DryRun flags. A preview never changes a Unity project. Exact preset synchronization and deletion are explicit operations.

The interactive terminal follows the original Import / My projects workflow: colored menu panels, searchable project selection, package/version keyboard control and staged changes before saving. The desktop Create page uses a single Windows, Android/Quest or iOS choice. Advanced CLI platform controls remain available.

On Dom's PC, `vrcsetup-legacy` opens the preserved original PowerShell tool while v2 is being accepted. It lives separately under `%LOCALAPPDATA%\Programs\VrcSetup-Legacy`, with its own `bin` PATH entry, and is not removed or overwritten by v2 installation. The original engine is not bundled into the shared-core v2 distribution.

## Diagnostics

Settings → Developer tools enables development controls, verbose diagnostics and tool-output tracing; Save settings persists these choices. CLI and `vrcsetup gui` also accept `--developer`, `--verbose`, `--trace-tools` and `--no-catalog-refresh` for that run. Errors are always recorded in `%LOCALAPPDATA%\VrcSetup\logs\diagnostics-yyyyMMdd.jsonl`. Trace output may contain local paths; inspect it before sharing.

Activity separates imports and operations. Check, resume/reimport and associated-backup restore belong to each relevant history entry. The CLI retains `recovery` commands and its interactive Activity menu. Presets use local known packages and favorites immediately; stale version lists refresh asynchronously when the preset page opens. Search contacts repositories only when requested.

## Language and layout

The desktop uses the system UI language (Italian or English; other languages fall back to English). Settings → Language overrides this. Apply language and restart saves that choice and restarts only when imports and pending package/preset changes are finished. New installs default to Dark; existing theme choices remain unchanged. Package names/IDs, paths, versions and provider logs are not translated.

The main window has a 720×580 minimum, an always-accessible compact sidebar, and full-width Import/Settings views. Information is a popup with downloads, bug reports, contact@dummics.com and logs. Footer Update available starts the update directly; Update failed opens details.

## Data and recovery

Installed settings/cache/logs: %LOCALAPPDATA%\VrcSetup. Program: %LOCALAPPDATA%\Programs\VrcSetup. Backups and operation state live in each project's .vrcsetup folder. Legacy configuration is copied and migrated, preserving the original.

Stop Unity before changing its project packages. If an operation fails, inspect Activity and the full log, then review again or restore a verified backup. Imported assets are backed up on existing projects. A new partial project is preserved until you explicitly delete it.

Repair rebuilds the command and shortcuts after validating payload hashes. Uninstall removes the program and its scoped shortcuts/PATH entry, preserving settings and Unity projects. Previous installation backups are preserved during upgrades.

## Feedback

Report the app version, action, result and relevant diagnostic log. Avoid sharing personal paths or private avatar content unnecessarily. This package includes source, fixture and native-render verification on its development branch; interactive mouse/keyboard acceptance is a separate test.
