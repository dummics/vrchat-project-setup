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
- Bulk package editing from My projects: mark projects with Space, then Enter.
  Stage individual packages and versions, apply a creator preset while keeping
  other packages, or match a preset/reference project exactly. In add/update
  mode, each package can target a different subset of the selection. Review each
  project's additions, updates and removals before applying. Open Unity projects
  and projects changed after review are skipped; failures do not stop the others.
- In Add packages, press V to choose a searchable version before adding; the
  chosen version appears in the row. Enter confirms checked packages (or the
  focused package when nothing is checked).
- A scriptable CLI for project discovery, JSON output, package search/list/add/
  remove and UnityPackage project creation, with `-DryRun` previews.
- Configurable package presets with `latest` or pinned versions.
- Package discovery through `vrc-get`, VPM and the local VCC repository cache.
- Package-derived project names by default, optional custom names, and configurable naming rules.
- Detection and cleanup of interrupted or incomplete setup operations.
- Cancellation with `Q` or `Esc` during long Unity operations.
- Backups before package-manifest changes and timestamped execution logs.
- Import progress reads Unity logs only when their size or write time changes.
  Exact package versions already present on disk are reused, including packages
  installed as dependencies; `latest` still requests an update.
- Import and finalization share one Unity session, keep its update loop running
  and wait for import/compilation to settle through domain reloads. Import errors
  and finalization timeouts leave the project incomplete instead of reporting
  success. Heavy texture/shader processing remains Unity's responsibility.
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
   - Change the project name only when needed from the review menu.
   - Review the project name, target folder and import action before starting.
2. **Manage projects**
   - Open the project library: it scans the configured root and nearby nested
     folders, then shows one keyboard-selectable project table with project type,
     Unity version, VPM package count and last-updated time.
   - Starts with projects updated most recently; change the library order to
     alphabetical whenever that is more useful.
   - Select a listed project and land directly in one package workspace. The
     bordered table uses one readable Version column and explains only what will
     happen after saving. Left/Right cycles cached versions without leaving the
     table; Space includes or removes a package, while V opens the full list only
     when needed.
   - Find packages through a boxed search field and a scrollable result table
     with aligned name, package ID and version columns. Matches already present
     in the project are reported as already included rather than failed searches.
     Enter adds the highlighted package immediately to the pending set. Use Space
     for multi-selection, A for all results, or N to clear the selection. The Add
     button shows what will be queued; installation happens only at Save changes.
   - Press V or Enter on a package for a searchable version table. Type a version
     prefix, use P to show/hide prereleases, or M to enter an exact version. Fixed
     versions are pins; `latest` follows compatible updates when applied. Left/Right
     stays within stable versions unless the current choice is a prerelease.
   - Press P in the project workspace for named package presets. Save the current
     set, select a creator/custom set, or delete a preset. Applying a preset adds
     and updates its packages while keeping unrelated project packages. Edit the
     selected set and save under the same name to update a preset.
   - Press F for favorite packages: add common tools quickly or manage the
     reusable favorites list without retyping names for every project.
   - Treat VRChat SDK Base together with the selected project SDK (Avatars or
     Worlds): the picker offers versions shared by every component and stages
     one linked SDK update. Exact prerelease versions use the bundled `vrc-get`
     path for install, upgrade or downgrade.
   - Refresh the list explicitly or choose a folder outside the configured root.
   - Manage an existing Unity project and save a complete VPM change set once.
   - Stage the default preset without removing unrelated project packages.
   - Recognize required packages recorded by VPM as resolved/locked dependencies,
     avoiding false missing-package failures and repeated installs.
   - Include saved UnityPackages in the same final Save action when needed.
   - Review the staged changes in a dedicated confirmation surface, then keep
     the complete operation inside a scrollable progress panel that expands
     with the terminal height and adapts when resized. Page
     keys can inspect earlier messages without switching to a full-log screen;
     the timestamped session log remains available after completion.
     Finished operations show a highlighted Done/Back button; failures show
     Continue to recovery. Logs remain scrollable until the button is confirmed.
   - If saving fails, keep the staged choices and offer retry, package editing,
     or return-to-project paths instead of dropping the user out of the flow.
   - Detect and clean up incomplete project setups.
3. **Default package set**
   - Shows the required VRChat foundation first, in base/avatar/resolver order.
   - Keeps required packages installed while making optional packages easy to edit or remove.
   - Search packages and select available versions.
   - Type a version or prefix in the version picker; use `latest` or a specific version.
   - Remove optional starter packages such as GoGoLoco, VRCFury, Poiyomi,
     templates or Easy Login.
4. **Settings**
   - Set Unity Editor and project folder paths first.
   - Keep project naming rules and extra UnityPackage imports in separate, smaller screens.
   - Reset configuration when needed.

When creating from a UnityPackage, the tool creates the Unity project, applies
required manifest adjustments, installs configured VPM packages, and imports
main/extra UnityPackages sequentially in one Unity session. Finalization waits
through compilation and domain reloads in that same session. A second settle
session runs only if the post-import package check actually repairs packages.
Unity may still perform some
first-open asset processing, especially for projects with many textures or
scripts.

Import review offers **Build and publish platforms**: Windows, Android/Quest,
iOS, or combinations. A separate choice selects which platform Unity opens on.
Names containing the word Quest or Android suggest Android; you can override it
before starting. Missing Unity build modules block the import before project
creation. Existing project package edits preserve their current platform.
The SDK publishing selection is initialized on the first interactive opening,
using its public avatar session-state property when available. Older SDKs keep
the active Unity target and require choosing publishing platforms in the SDK.
This prepares platform selection; it does not convert PC materials for mobile.

The creation review includes **Package presets** and **Customize packages** for
this project only. A selected named preset replaces the default new-project set
and keeps the required foundation. On successful creation, `vpm add project`
registers the project in Creator Companion without closing or restarting VCC.
If registration fails, the completed project is preserved and a warning explains
how to add it manually.

Named presets live in `PackagePresets` in your local `vrcsetup.json`, which the
installer preserves. Each preset maps package IDs to exact versions or `latest`.
Your default set remains independent. From package settings, select a preset to
edit its packages and versions, then choose **Save preset**. Escape discards the
edit. Before importing, choose a creator preset or **Use my defaults**. New
projects use exactly that set plus the required foundation: optional packages
absent from a preset are not inherited from your defaults. Applying a preset to
an existing project offers keeping custom packages or matching the preset exactly.
Version search filters version numbers; searching by release date is not yet
available. A specific version belongs to the selected preset or project, rather
than a global package lock; `latest` follows compatible updates.

The main **Packages and creator presets** entry opens separate choices for
**My default packages**, **Creator presets**, favorites and **Saved package registry**. In **Creator presets**,
choose **Create a preset from my defaults**, name it, edit its package list and
versions, and choose **Save preset**. Cancel leaves both the preset and your
defaults unchanged. General settings stay in the separate **Settings** entry.
**My projects** opens the library directly; F2 contains folder, sorting, refresh
and incomplete-project cleanup options. Single-project and bulk package edits
share one table: Space includes/removes a package, Enter chooses its version,
and Tab moves to the visible actions. **Apply changes** opens the confirmation
before touching projects. **Options** controls project groups for individual
packages, additional imports and undo. Required foundation packages stay included.
Mixed installed versions remain unchanged until you choose a common version.
**Copy project** chooses any library project as source, then offers installed
packages or its saved preset selection, when available.
Import review emphasizes the main action with a larger filled button. Preset and
package customization use smaller outlined buttons; name/file/platform changes
have a separate compact outlined section. Renaming opens the current name as
editable text, with cursor movement, Backspace and Delete. Keyboard focus remains
visible on every action.
The main import action keeps its green accent but receives a filled highlight
only while focused. Settings has its own spaced outline and rose accent.
Operation logs reserve their final action area from the start and keep the log
panel height fixed, so completion does not expand the live display unexpectedly.
Saving from an existing project captures direct packages and the required locked
SDK foundation, rather than every transitive dependency.

The local registry combines defaults, named presets, favorites, package searches
and projects opened for editing. Cached definitions appear before repository
search and retain selected versions. **Saved package registry** can add them to
an existing/new preset. **Refresh in background** fetches available versions in
an in-process worker; menus remain usable and show progress. Refresh preserves
version choices made while it runs, keeps unavailable entries, and can be stopped.
Definitions live in `setup-scripts/cache/known-packages.json`. Project profiles
live in `<Project>/.vrcsetup/package-profile.json`; manifest changes and newer
preset definitions are detected without silently applying them.

```powershell
vrcsetup presets save "Creator PC" "F:\Unity Projects\Creator Avatar"
vrcsetup presets list -Json
vrcsetup presets apply "Creator PC" "F:\Unity Projects\Another Avatar" -DryRun
vrcsetup presets apply "Creator PC" "F:\Unity Projects\Another Avatar"
vrcsetup create ".\Avatar.unitypackage" -Name "New Avatar" -Preset "Creator PC"
vrcsetup setup "F:\Unity Projects\Another Avatar" -Preset "Creator PC" -DryRun
vrcsetup presets remove "Creator PC"
```

### Project library and cache

The interactive library has a visible search field: type a project name, PC/Quest,
or Unity version to filter the table immediately. Enter manages the selected
project; F2 opens sorting, refresh and folder options. Columns and the number of
visible rows adapt to the terminal size. Narrow terminals keep the project name
and omit lower-priority columns. Scan/cache
diagnostics remain available through CLI output instead of occupying the main UI.
Import starts with a drag-and-drop field for the `.unitypackage` file. Dropped
paths are normalized, including quotes, spaces and special characters.

The first project-library scan treats `ProjectSettings/ProjectVersion.txt` and
`Packages/vpm-manifest.json` as the canonical metadata sources. If either file
is missing, the library can recover package names/type from embedded package
folders and the Unity version from a generated root `.csproj`; recovered values
are marked with `*` instead of being presented as canonical. If no reliable
version source remains, the version stays `Unknown`. Later scans reuse unchanged
metadata and re-read only new or modified projects. The generated index lives
under `setup-scripts/cache/`, is local to the portable or installed copy, and is
never committed or copied during installation.

The default order is **recently updated**, using the project folder timestamp
shown by Windows Explorer. Choose **Change sort order** in the library to
persist **Name (A-Z)** instead. The same preference is used by
`vrcsetup projects`; use `-Sort recent` or `-Sort name` for a one-off CLI order.

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
# For an unattended script only; interactive uninstall always asks first.
vrcsetup uninstall --yes

# Discover projects (add -Json for automation)
vrcsetup projects
vrcsetup projects -Refresh -Json
vrcsetup projects -Sort name

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
  "FavoritePackages": [
    "gogoloco",
    "com.vrcfury.vrcfury",
    "com.poiyomi.toon"
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
packages. `FavoritePackages` is a personal quick-add list and does not install
anything until those favorites are selected in a project.

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
shortcuts, alias behavior, repair/uninstall confirmation, folder validation and paths containing special
characters.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

The test runner launches its Start Menu/BAT probe through `wscript.exe` and the
included zero-window helper, so automated test runs do not flash a child console.

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
