# VRChat Project Setup

> A click-first Windows companion for preparing VRChat Unity avatar projects consistently.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![PowerShell](https://img.shields.io/badge/PowerShell-5.1%2B-5391FE?logo=powershell&logoColor=white)](https://learn.microsoft.com/powershell/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

VRChat Project Setup turns a downloaded `.unitypackage` or an existing Unity
project into a repeatable VRChat setup flow. It guides the user through paths,
project naming and VPM packages, then performs the setup with validation,
recoverable state and useful logs.

It is designed for creators who prefer to click through a clear wizard, while
still providing the `vrcsetup` command for advanced users and automation.

## Download

**[Download the latest source ZIP](https://github.com/dummics/vrchat-project-setup/archive/refs/heads/main.zip)**

Extract the ZIP before running it. The downloaded source is fully usable; a
separate release package is not required.

## Quick start

### Install for the current Windows user

1. Extract the downloaded ZIP.
2. Double-click **`Install VRChat Project Setup.bat`**.
3. Press any key when installation finishes to open the wizard.
4. Later, press the Windows key and search for **VRChat Project Setup**.

Installation does not require administrator rights. It creates a private copy
under `%LOCALAPPDATA%\Programs\VrcSetup`, adds `vrcsetup` to the current user's
`PATH`, and creates Start-menu shortcuts for opening, repairing and uninstalling
the tool.

### Run directly without installing

Double-click **`VRChat Project Setup.bat`**.

This is the portable entry point. If an installed copy exists, it opens that
copy so configuration and maintenance stay consistent. Otherwise it runs
directly from the extracted source folder.

### Repair or uninstall

- Double-click **`Repair VRChat Project Setup.bat`** to validate the installation,
  rebuild the alias and restore Start-menu shortcuts.
- Double-click **`Uninstall VRChat Project Setup.bat`** to remove the per-user
  installation after an explicit `Y/N` confirmation.

These launchers detect the installed copy and will not treat the downloaded
source folder as an installation to delete.

## What it provides

- A keyboard-friendly interactive setup wizard.
- Project preparation from a `.unitypackage` or an existing Unity folder.
- Configurable VPM packages with `latest` or pinned versions.
- Package discovery through `vrc-get`, VPM and the local VCC repository cache.
- Remembered project names and configurable naming rules.
- Detection and cleanup of interrupted or incomplete setup operations.
- Cancellation with `Q` or `Esc` during long Unity operations.
- Backups before package-manifest changes and timestamped execution logs.
- Portable, installed and command-line entry points backed by the same engine.

## Requirements

- Windows 10 or Windows 11.
- Windows PowerShell 5.1 or PowerShell 7.
- VRChat Creator Companion and a compatible Unity Editor installation.
- Internet access when package metadata or packages must be downloaded.

The first-run wizard helps select Unity and project paths. PowerShell 7 is used
when available; otherwise the built-in Windows PowerShell 5.1 is supported.

## Relationship with VRChat Creator Companion

This project complements VRChat Creator Companion; it does not replace it.

VRChat Project Setup focuses on a guided, repeatable project-preparation flow:
input selection, naming, configured VPM packages, UnityPackage import, recovery
and diagnostics. VCC remains the official tool for VRChat SDK distribution,
official project management and platform services.

This is an independent community utility and is not affiliated with or endorsed
by VRChat Inc.

## Source-folder layout

The four user-facing actions stay at the first level of the downloaded source:

```text
vrchat-project-setup-main/
├── VRChat Project Setup.bat
├── Install VRChat Project Setup.bat
├── Repair VRChat Project Setup.bat
├── Uninstall VRChat Project Setup.bat
├── setup-scripts/
│   ├── setup.bat
│   ├── vrc-setup-script.ps1
│   ├── bin/                  # Installed terminal alias
│   ├── commands/
│   ├── config/
│   ├── lib/
│   └── maintenance/          # Internal install, repair and uninstall scripts
├── docs/
├── tests/
└── README.md
```

Local configuration is stored in
`setup-scripts/config/vrcsetup.json`. It is generated from the tracked defaults
and is intentionally excluded from Git because it can contain machine-specific
paths.

## Wizard workflows

The main wizard provides:

1. **Setup project**
   - Create and configure a project from a UnityPackage.
   - Prepare an existing Unity project.
   - Add VPM packages and optionally import extra UnityPackages.
   - Detect and clean up incomplete project setups.
2. **Configure VPM packages**
   - Search packages and select available versions.
   - Use `latest` or pin a specific version.
3. **Advanced settings**
   - Configure Unity paths, naming rules and remembered project names.
4. **Reset configuration**

When creating from a UnityPackage, the tool creates the Unity project, applies
required manifest adjustments, installs configured VPM packages, imports the
package and waits for a bounded finalization step. Unity may still perform some
first-open asset processing, especially for projects with many textures or
scripts.

## Terminal usage

After installation, open a new terminal and run:

```powershell
vrcsetup
vrcsetup repair
vrcsetup uninstall
```

The engine can also be called directly:

```powershell
# Open the wizard
.\setup-scripts\vrc-setup-script.ps1 -Wizard

# Prepare an existing Unity project or import a UnityPackage
.\setup-scripts\vrc-setup-script.ps1 -projectPath "C:\Path\To\Project-Or-Package"

# Validate the flow without changing the target project
.\setup-scripts\vrc-setup-script.ps1 -projectPath "C:\Path\To\Project" -Test

# Reset local configuration
.\setup-scripts\vrc-setup-script.ps1 -projectPath "-reset"
```

Paths containing spaces, Unicode characters, `&`, parentheses and wildcard-like
characters such as `[]` are handled literally. Wizard path fields also accept
environment variables, `~` and paths relative to the tool folder.

## Configuration and package versions

Packages are configured in `setup-scripts/config/vrcsetup.json`:

```json
{
  "VpmPackages": {
    "com.vrchat.base": "latest",
    "com.vrchat.avatars": "latest",
    "com.poiyomi.toon": "latest",
    "com.vrcfury.vrcfury": "latest"
  },
  "UnityEditorPath": "C:\\Path\\To\\Unity.exe",
  "UnityProjectsRoot": "D:\\Unity Projects",
  "UnityPackagesFolder": null
}
```

Use `latest` for convenience or an exact version for reproducible projects. The
wizard validates configured versions before applying them. Existing older
package-list configurations are migrated automatically.

`dev.foxscore.easy-login` is managed as a protected VPM dependency. If an
imported avatar contains an older source copy under `Assets/EASY LOGIN`, the
tool moves it to a recoverable `.vrcsetup/backups/` location before resolving
the package, avoiding duplicate assemblies without discarding the original.

## Safety and recovery

- Project manifest files are backed up before modification.
- Interrupted UnityPackage workflows are tracked in
  `<Project>/.vrcsetup/state.json`.
- Cancelled incomplete creations can be removed through the wizard.
- Install and repair preserve the user's configuration.
- Uninstall requires confirmation and validates an installation marker before
  removing files.
- Logs are written under `setup-scripts/logs/`.

## Testing

The regression suite validates both PowerShell 7 and Windows PowerShell 5.1,
portable and installed launchers, Start-menu shortcuts, alias behavior,
repair/uninstall safety and paths containing special characters.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

## Future direction

The long-term direction is a small Windows desktop companion built on the same
setup engine, without duplicating Creator Companion responsibilities. See
[Future Desktop App](docs/FUTURE-DESKTOP-APP.md).

## Contributing

Issues and pull requests are welcome. Please describe the project type, expected
behavior, observed behavior and any relevant sanitized log excerpt. Do not
include local credentials or private avatar assets.

## License

Released under the [MIT License](LICENSE).
