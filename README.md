# VRChat Project Setup

Windows companion for repeatable VRChat Unity projects. A native WinUI 3 desktop app and a Spectre.Console CLI/TUI share one C# / .NET 10 engine.

## Install or run portable

Use the standalone `Install-VrcSetup-2.0.0-preview.12-win-x64.exe` tester package. It includes the application runtimes, installs per user without elevation, creates Start-menu shortcuts and the `vrcsetup` command. The portable ZIP includes the same GUI and CLI; extract it before use. See [distribution instructions](docs/desktop/DISTRIBUTION.md).

This branch is `feature/winui-desktop`. The main-branch source ZIP contains the earlier PowerShell product. Source from this branch requires the .NET 10 SDK and Windows build tools; source and the distributable package are distinct downloads.

Current source is preview 12: [state-aware recovery](docs/desktop/IMPORT-QUEUE.md), missing-asset imports that preserve existing files/versions, compatible log readers, per-import timers and fixed resource status. The simple import page supports parallel jobs, a short recap and expandable console; manual paths are optional in Settings. The terminal import restores boxed input/progress views on the same shared engine. Package toggles/version edits retain stable rows. Offline inspection and discovery caches stay separate from My Packages. Lulla Quest recovery completed and was validated; broader UI/TUI acceptance remains with Dom.

## Features

- Project library with search, sorting, metadata recovery and bulk selection.
- Creation from UnityPackages with an automatic editable name, one desktop platform choice, creator presets and optional reusable templates. CLI retains advanced platform controls.
- Preset editing with a clear selected preset, staged changes, local package library, favorite stars and asynchronous version refresh.
- A shared package workspace: stage multiple additions, pins, latest updates and removals, then review per-project changes before saving.
- Linked VRChat SDK versions, required-package protection, exact prereleases, favorites and package registry.
- Overlay or exact preset application, package selection from another project, individual bulk targets and undo.
- Unity import/compile verification, Easy Login migration, manifest/asset backup, recovery, cancellation, logs and JSON reports.
- Legacy configuration migration, atomic revisions, project/process locks and stale-plan protection.

Unity with the necessary build modules, Creator Companion repositories and the VPM CLI are external prerequisites. VPM requires its compatible .NET runtime. The app reports missing requirements; it does not install Unity or change personal projects implicitly.

## CLI

`vrcsetup` without arguments opens the terminal UI. `vrcsetup gui` opens the Windows app. Use `vrcsetup help` for all commands and flags; legacy PowerShell flag spellings remain accepted.

The C# TUI restores the original Import / My projects flow, colored panels, searchable project selection and keyboard package workspace. Dom's original PowerShell version is also preserved locally as `vrcsetup-legacy` during GUI acceptance; it remains independent of v2 upgrades.

```powershell
vrcsetup projects --json
vrcsetup packages list 'F:\UNITY PROJECTS\Avatar'
vrcsetup packages add 'F:\UNITY PROJECTS\Avatar' gogoloco@latest --dry-run
vrcsetup create 'F:\Downloads\Avatar.unitypackage' --name 'Avatar PC' --preset 'Creator PC' --dry-run
vrcsetup repair
vrcsetup uninstall
```

Exit codes: 0 success, 1 failure or skipped projects, 2 cancellation/confirmation declined. Dry runs show plans without applying project changes; catalog/config caches may initialize.

## Develop and verify

```powershell
dotnet run --project src/VrcSetup.Cli -- help
powershell -NoProfile -File tests/Run-Tests.ps1 -BuildDesktop
powershell -NoProfile -File scripts/Publish-Windows.ps1
```

The root BAT launchers are source entrypoints. The published package has launchers targeting its bundled executables. The old PowerShell implementation is retired on this branch and remains available in Git history and stable main.

See [architecture and approved plan](docs/desktop/IMPLEMENTATION-PLAN.md), [verification and parity](docs/desktop/VERIFICATION.md), and [migration status](docs/desktop/PROGRESS.md). The Unity bridge is compiled by Unity and does not depend on .NET 10.
