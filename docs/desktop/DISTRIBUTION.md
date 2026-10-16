# VRChat Project Setup for Windows

The Windows app and vrcsetup command use the same C#/.NET 10 engine.

## Install

Run Install-VrcSetup-<version>-win-x64.exe. It is self-contained: no .NET SDK or Visual Studio is required. Install for the current user, then open **VRChat Project Setup** from Windows Search.

Unity Editor and its selected build modules, VRChat Creator Companion and the VPM CLI remain content-creation prerequisites. VPM has its own runtime requirements; the app checks tool failures and never treats an incomplete import as ready. No VRChat login is requested by VRCSETUP.

The application is a community utility, independent of VRChat Inc. Unsigned test builds may show Windows reputation prompts. Verify SHA256SUMS.txt; do not disable Windows security settings to run a test build.

Preview 4 keeps the original core/TUI while polishing the desktop. Packages uses a centered preset selector with Preset packages and My packages tabs; package/preset menus hold secondary actions, and save controls appear for pending edits. Automatic version selection means `latest`; Advanced contains preview and exact-version controls. Activity is in the navigation footer, with restore and unfinished-project actions in its menu.

## Portable

Extract VrcSetup-<version>-win-x64.zip, then run VRChat Project Setup.bat or app/desktop/VrcSetup.Desktop.exe. The portable marker keeps settings under data/ in the extracted folder. Use bin/vrcsetup.cmd for the terminal.

## CLI

After installation, open a new terminal and run vrcsetup help. vrcsetup without arguments opens the terminal UI; vrcsetup gui opens the Windows UI. PowerShell 5.1 and PowerShell 7 can both invoke it.

Projects, package edits, preset operations and UnityPackage creation retain their existing commands, -Json and -DryRun flags. A preview never changes a Unity project. Exact preset synchronization and deletion are explicit operations.

The interactive terminal follows the original Import / My projects workflow: colored menu panels, searchable project selection, package/version keyboard control and staged changes before saving. The desktop Create page uses a single Windows, Android/Quest or iOS choice. Advanced CLI platform controls remain available.

On Dom's PC, `vrcsetup-legacy` opens the preserved original PowerShell tool while v2 is being accepted. It lives separately under `%LOCALAPPDATA%\Programs\VrcSetup-Legacy`, with its own `bin` PATH entry, and is not removed or overwritten by v2 installation. The original engine is not bundled into the shared-core v2 distribution.

## Diagnostics

Settings → Developer tools enables development controls, verbose diagnostics and tool-output tracing; Save settings persists these choices. CLI and `vrcsetup gui` also accept `--developer`, `--verbose`, `--trace-tools` and `--no-catalog-refresh` for that run. Errors are always recorded in `%LOCALAPPDATA%\VrcSetup\logs\diagnostics-yyyyMMdd.jsonl`. Trace output may contain local paths; inspect it before sharing.

Activity shows operations. Additional unfinished-project and restore controls appear in developer mode. The CLI retains `recovery` commands and its interactive Activity and recovery menu. Presets use local known packages and favorites immediately; stale version lists refresh asynchronously when the preset page opens. Search contacts repositories only when requested.

## Data and recovery

Installed settings/cache/logs: %LOCALAPPDATA%\VrcSetup. Program: %LOCALAPPDATA%\Programs\VrcSetup. Backups and operation state live in each project's .vrcsetup folder. Legacy configuration is copied and migrated, preserving the original.

Stop Unity before changing its project packages. If an operation fails, inspect Activity and the full log, then review again or restore a verified backup. Imported assets are backed up on existing projects. A new partial project is preserved until you explicitly delete it.

Repair rebuilds the command and shortcuts after validating payload hashes. Uninstall removes the program and its scoped shortcuts/PATH entry, preserving settings and Unity projects. Previous installation backups are preserved during upgrades.

## Feedback

Report the app version, action, result and relevant diagnostic log. Avoid sharing personal paths or private avatar content unnecessarily. This package includes source, fixture and native-render verification on its development branch; interactive mouse/keyboard acceptance is a separate test.
