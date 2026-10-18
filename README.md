# VRC Project Setup

A Windows app for creating VRChat Unity projects, importing `.unitypackage` files and managing packages across your projects.

**Windows x64 · Currently in beta**

## Download and install

**[Download for Windows](https://github.com/dummics/vrchat-project-setup/releases)**

1. Open the newest release, including prereleases while the app is in beta.
2. Download `Install-VrcSetup-<version>-win-x64.exe` and run it.
3. Follow setup. The app opens automatically when installation finishes.

The same small EXE installs a new copy or updates an existing one. It downloads and verifies the required components; you do not need to download the hash-named ZIPs or `components.json` yourself. No .NET SDK is needed to run the app.

Internet is required for components that are not already available locally. The optional portable ZIP is available on the same release page. Builds are currently unsigned, so Windows may show a publisher warning.

## Get started

1. In **Settings**, choose your Unity Editor and projects folder. The app can search for Unity **2022.3.22f1**.
2. Create an empty project, add an existing project, or drop a `.unitypackage` into **Import projects**.
3. Choose a preset, review the packages and start the import.

Unity with the required platform modules, VRChat Creator Companion and the VPM CLI are separate prerequisites. See [setup details](docs/desktop/DISTRIBUTION.md).

## What it does

- Browse projects and manage packages individually or in batches.
- Create presets, choose package versions and keep favorites.
- Review changes before applying them, with progress and logs.
- Track imports and recover incomplete projects through Activity.
- Use the desktop app or the included terminal interface.

## Updates and support

Check for updates from the version menu at the bottom of the app. Updates preserve your settings, reuse unchanged components and reopen the app automatically.

To report a problem, use **Information → Report a bug** or [open a GitHub issue](https://github.com/dummics/vrchat-project-setup/issues). Include your app version, what you tried and what happened. Remove personal information before sharing logs or screenshots.

This is an independent community tool, not an official VRChat product.

## For developers

The native WinUI desktop app and CLI/TUI share a C# / .NET 10 engine. The current app source is on **`feature/winui-desktop`**; `main` still contains the earlier PowerShell version. Download a release to use the app; GitHub's source ZIP is not an installer.

```powershell
dotnet run --project src/VrcSetup.Cli -- help
dotnet test tests/VrcSetup.Core.Tests -c Release
```

Installed CLI: `vrcsetup` opens the terminal UI, `vrcsetup gui` opens the desktop app, and `vrcsetup help` lists commands.

See [building and publishing](docs/desktop/RELEASING.md), [component updates and hosting](docs/desktop/COMPONENT-UPDATES.md), and [verification](docs/desktop/VERIFICATION.md).
