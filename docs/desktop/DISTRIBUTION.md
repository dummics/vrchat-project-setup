# VRChat Project Setup for Windows

The Windows app and vrcsetup command use the same C#/.NET 10 engine.

## Install

Run Install-VrcSetup-<version>-win-x64.exe. It is self-contained: no .NET SDK or Visual Studio is required. Install for the current user, then open **VRChat Project Setup** from Windows Search.

Unity Editor and its selected build modules, VRChat Creator Companion and the VPM CLI remain content-creation prerequisites. VPM has its own runtime requirements; the app checks tool failures and never treats an incomplete import as ready. No VRChat login is requested by VRCSETUP.

The application is a community utility, independent of VRChat Inc. Unsigned test builds may show Windows reputation prompts. Verify SHA256SUMS.txt; do not disable Windows security settings to run a test build.

## Portable

Extract VrcSetup-<version>-win-x64.zip, then run VRChat Project Setup.bat or app/desktop/VrcSetup.Desktop.exe. The portable marker keeps settings under data/ in the extracted folder. Use bin/vrcsetup.cmd for the terminal.

## CLI

After installation, open a new terminal and run vrcsetup help. vrcsetup without arguments opens the terminal UI; vrcsetup gui opens the Windows UI. PowerShell 5.1 and PowerShell 7 can both invoke it.

Projects, package edits, preset operations and UnityPackage creation retain their existing commands, -Json and -DryRun flags. A preview never changes a Unity project. Exact preset synchronization and deletion are explicit operations.

## Data and recovery

Installed settings/cache/logs: %LOCALAPPDATA%\VrcSetup. Program: %LOCALAPPDATA%\Programs\VrcSetup. Backups and operation state live in each project's .vrcsetup folder. Legacy configuration is copied and migrated, preserving the original.

Stop Unity before changing its project packages. If an operation fails, inspect Activity and the full log, then review again or restore a verified backup. Imported assets are backed up on existing projects. A new partial project is preserved until you explicitly delete it.

Repair rebuilds the command and shortcuts after validating payload hashes. Uninstall removes the program and its scoped shortcuts/PATH entry, preserving settings and Unity projects. Previous installation backups are preserved during upgrades.

## Feedback

Report the app version, action, result and relevant diagnostic log. Avoid sharing personal paths or private avatar content unnecessarily. This package includes source, fixture and native-render verification on its development branch; interactive mouse/keyboard acceptance is a separate test.
