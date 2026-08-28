# 🎮 VRChat Project Setup

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

## ⬇️ Download

**[Download the latest source ZIP](https://github.com/dummics/vrchat-project-setup/archive/refs/heads/main.zip)**

Extract the ZIP before running it. The downloaded source is fully usable; a
separate release package is not required.

## 🚀 Quick start

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

## ✨ What it provides

- A keyboard-friendly interactive setup wizard.
- A cached project library that scans the configured projects folder, recognizes
  avatar/world projects and opens package management without asking for a path.
- Fast project creation from a `.unitypackage`, with an automatic suggested name
  and one compact review screen.
- Existing-project setup and AIO VPM management: add, update and remove several
  packages, then apply the complete plan in one run.
- A scriptable CLI for project discovery, JSON output, package search/list/add/
  remove and UnityPackage project creation, with `-DryRun` previews.
- Configurable package presets with `latest` or pinned versions.
- Package discovery through `vrc-get`, VPM and the local VCC repository cache.
- Remembered project names and configurable naming rules.
- Detection and cleanup of interrupted or incomplete setup operations.
- Cancellation with `Q` or `Esc` during long Unity operations.
- Backups before package-manifest changes and timestamped execution logs.
- Portable, installed and command-line entry points backed by the same engine.

## 🧩 Requirements

- Windows 10 or Windows 11.
- Windows PowerShell 5.1 or PowerShell 7.
- VRChat Creator Companion and a compatible Unity Editor installation.
- Internet access when package metadata or packages must be downloaded.

The first-run wizard helps select Unity and project paths. In PowerShell 7, the
bundled Spectre.Console runtime presents every interactive menu, path prompt and
package browser in a consistent panel layout. Windows PowerShell 5.1 and redirected
sessions use a simple text fallback. No extra PowerShell module is required.

## 🤝 Relationship with VRChat Creator Companion

This project complements VRChat Creator Companion; it does not replace it.

VRChat Project Setup focuses on a guided, repeatable project-preparation flow:
input selection, naming, configured VPM packages, UnityPackage import, recovery
and diagnostics. VCC remains the official tool for VRChat SDK distribution,
official project management and platform services.

This is an independent community utility and is not affiliated with or endorsed
by VRChat Inc.

## 📁 Source-folder layout

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

## 🧭 Wizard workflows

The main wizard starts with two clear actions: **Create project** and **Manage
projects**. It then provides:

1. **Create project**
   - Create from a UnityPackage using the suggested folder name immediately.
   - Review the project name, target folder and import action before starting.
2. **Manage projects**
   - Open the project library: it scans the configured root and nearby nested
     folders, then shows project type, Unity version and direct VPM package count.
   - Select a listed project and go directly to its AIO package actions.
   - Refresh the index explicitly or choose a folder outside the configured root.
   - Manage an existing Unity project and apply a complete VPM change set once.
   - Add/update the default preset without removing unrelated project packages.
   - Optionally import extra UnityPackages.
   - Detect and clean up incomplete project setups.
3. **Default package set**
   - Shows the required VRChat foundation first, in base/avatar/resolver order.
   - Keeps required packages installed while making optional packages easy to edit or remove.
   - Search packages and select available versions.
   - Use `latest` or pin a specific version.
   - Remove optional starter packages such as GoGoLoco, VRCFury, Poiyomi,
     templates or Easy Login.
4. **Settings**
   - Set Unity Editor and project folder paths first.
   - Keep project naming rules and extra UnityPackage imports in separate, smaller screens.
   - Reset configuration when needed.

When creating from a UnityPackage, the tool creates the Unity project, applies
required manifest adjustments, installs configured VPM packages, imports the
package and waits for a bounded finalization step. Unity may still perform some
first-open asset processing, especially for projects with many textures or
scripts.

### Project library and cache

The first project-library scan reads each Unity project's
`ProjectSettings/ProjectVersion.txt` and `Packages/vpm-manifest.json`. Later
scans reuse unchanged metadata and re-read only new or modified projects. The
generated index lives under `setup-scripts/cache/`, is local to the portable or
installed copy, and is never committed or copied during installation.

The library reads project metadata only. Package changes still require choosing
a project, reviewing the AIO add/update/remove plan and confirming the operation.

## ⌨️ Terminal usage

After installation, open a new terminal and run:

```powershell
# Interactive wizard
vrcsetup

# Maintenance
vrcsetup repair
vrcsetup uninstall

# Discover projects (add -Json for automation)
vrcsetup projects
vrcsetup projects -Refresh -Json

# Search and inspect direct VPM dependencies
vrcsetup packages search gogoloco
vrcsetup packages list "D:\Unity Projects\My Avatar"

# Preview or apply project-scoped AIO changes
vrcsetup packages add "D:\Unity Projects\My Avatar" gogoloco@1.8.6 -DryRun
vrcsetup packages add "D:\Unity Projects\My Avatar" gogoloco@1.8.6
vrcsetup packages remove "D:\Unity Projects\My Avatar" gogoloco

# Create from a UnityPackage and add packages to the configured preset
vrcsetup create ".\My Avatar.unitypackage" -Name "My Avatar" -Package gogoloco@latest
```

`packages add` also changes an existing direct package to the requested version.
Unchanged packages are kept without reinstalling them. `packages remove` refuses
to remove the required VRChat foundation packages. Add `-Json` to `projects`,
`packages list`, or `packages search` when another script needs structured data.

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

## 📦 Configuration and package versions

Packages are configured in `setup-scripts/config/vrcsetup.json`:

```json
{
  "VpmPackages": {
    "com.vrchat.base": "latest",
    "com.vrchat.avatars": "latest",
    "com.poiyomi.toon": "latest",
    "com.vrcfury.vrcfury": "latest"
  },
  "RequiredPackages": [
    "com.vrchat.base",
    "com.vrchat.avatars",
    "com.vrchat.core.vpm-resolver"
  ],
  "UnityEditorPath": "C:\\Path\\To\\Unity.exe",
  "UnityProjectsRoot": "D:\\Unity Projects",
  "UnityPackagesFolder": null
}
```

Use `latest` for convenience or an exact version for reproducible projects. The
wizard validates configured versions before applying them. Only the three
VRChat foundation packages in `RequiredPackages` are locked. Other starter
packages are normal choices and can be removed. Existing older package-list
configurations are migrated automatically without restoring removed optional
packages.

When `dev.foxscore.easy-login` is selected and an imported avatar contains an
older source copy under `Assets/EASY LOGIN`, the tool moves it to a recoverable
`.vrcsetup/backups/` location before resolving the VPM package. Easy Login is
optional and can be removed like other non-foundation packages.

## 🛡️ Safety and recovery

- Project manifest files are backed up before modification.
- AIO synchronization starts from the project's current direct VPM dependencies;
  packages are removed only after the user removes them from the reviewed set.
- Interrupted UnityPackage workflows are tracked in
  `<Project>/.vrcsetup/state.json`.
- Cancelled incomplete creations can be removed through the wizard.
- Install and repair preserve the user's configuration.
- Uninstall requires confirmation and validates an installation marker before
  removing files.
- Logs are written under `setup-scripts/logs/`.

## ✅ Testing

The regression suite validates both PowerShell 7 and Windows PowerShell 5.1,
portable and installed launchers, AIO package add/remove planning, Start-menu
shortcuts, alias behavior, repair/uninstall safety and paths containing special
characters.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

## 🪟 Future direction

The long-term direction is a small Windows desktop companion built on the same
setup engine, without duplicating Creator Companion responsibilities. See
[Future Desktop App](docs/FUTURE-DESKTOP-APP.md).

## 💬 Contributing

Issues and pull requests are welcome. Please describe the project type, expected
behavior, observed behavior and any relevant sanitized log excerpt. Do not
include local credentials or private avatar assets.

## License

Released under the [MIT License](LICENSE).
