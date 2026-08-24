# VRChat Unity Project Setup Scripts

Automated scripts to create and configure VRChat Unity projects, add VPM packages, and manage setup workflows.

## 🎯 Purpose

This folder contains a set of scripts that help you quickly create, configure, and maintain VRChat Unity projects, including support for importing Unity packages and managing VPM packages/versions.

UnityPackage mode order (important): the installer creates the project, applies required manifest tweaks (Unity Test Framework), installs configured VPM packages, then imports the UnityPackage(s) and runs a bounded post-import finalize step. This aims to reduce Unity doing a second big import/preprocess pass when you open the project in the GUI.

Implementation notes:
- The finalize step is blocking (no update callbacks) and exits from code (no `-quit`), so Unity doesn't close before the asset pipeline is stable.
- Unity invocations pin `-buildTarget StandaloneWindows64` for deterministic imports.

Note: Unity may still show some import/preprocess on the first GUI open (especially for packages with many textures/scripts). The scripts can reduce double-work, but they can't fully eliminate Unity's first-time asset pipeline.

Cancellation:
- During Unity steps (create/import/finalize), press `Q` or `Esc` to cancel.
- If you cancel in UnityPackage mode, the script stops Unity and deletes the created project folder.

Incomplete project cleanup:
- When creating a project from a UnityPackage, the installer writes a marker at `<Project>/.vrcsetup/state.json` with the current step.
- The marker is created right after the Unity project is created (so a crash/failure afterwards can still be detected).
- The project is marked `completed=true` only when all expected steps are marked as done.
- In the wizard: `Setup project` → `Cleanup incomplete projects`.
- It shows a paged checklist (default: all selected) so you can delete half-imported / dead projects safely.

## 📦 Structure

```
_unityprojectsetup/
├── vrcsetupfull.bat            # Launcher (opens the wizard)
└── setup-scripts/
    ├── vrc-setup-script.ps1    # Unified entrypoint (wizard + CLI)
    ├── setup.bat               # Batch wrapper for the wizard
    ├── commands/               # Wizard + installer commands
    ├── lib/                    # Shared helpers (menu/config/progress/utils)
    └── config/                 # Defaults (tracked) + local config (gitignored)

Note: on first run, `setup-scripts/config/vrcsetup.json` is created from `setup-scripts/config/vrcsetup.defaults`.
If the template is missing, the script generates a minimal skeleton config.
```

## 🚀 Quickstart

### Recommended: one-click per-user install

Double-click `INSTALL.bat`, or run:

```powershell
pwsh -NoProfile -File .\Install-VrcSetup.ps1
```

The installer requires no administrator rights. It copies the tool to
`%LOCALAPPDATA%\Programs\VrcSetup`, adds its `bin` folder to the current user's
`PATH`, and installs the `vrcsetup` command. Open a new terminal after install,
then run:

```powershell
vrcsetup
```

Maintenance commands:

```powershell
vrcsetup repair
vrcsetup uninstall
```

`REPAIR.bat` and `UNINSTALL.bat` provide the same operations for users who
prefer double-clickable launchers. Reinstall and repair preserve
`setup-scripts\config\vrcsetup.json`; the first install also migrates an existing
local config found beside the source scripts. Uninstall can optionally back it up with
`Uninstall-VrcSetup.ps1 -KeepConfig`.

### Portable use without installation

Run the script from PowerShell or via `vrcsetupfull.bat` to open the interactive wizard:

```powershell
# In PowerShell
.\setup-scripts\vrc-setup-script.ps1 -Wizard

# Or execute the top-level .bat (Windows)
vrcsetupfull.bat
```

Note: the batch launcher now runs the wizard in the current terminal session. If `pwsh` is installed it is preferred, otherwise Windows PowerShell is used.

## 🧭 Modes of Operation


### Wizard Mode
The wizard offers the following options:

1. Setup project (choose UnityPackage or existing project folder).
     - If the target project folder already exists (UnityPackage flow), you'll get extra choices:
         - Delete existing and recreate from the UnityPackage
         - Use existing: setup VPM only
         - Use existing: setup VPM + import extra UnityPackages (from `UnityPackagesFolder`)
2. Manage VPM packages (type-to-filter picker + selectable versions when available).
3. Advanced settings (naming rules + remembered project names).
4. Reset the configuration.

### CLI Mode
You can run the main script in scripted mode from command line:

```powershell
# Create project from UnityPackage
.\setup-scripts\vrc-setup-script.ps1 -projectPath "C:\Path\To\Package.unitypackage"

# Setup an existing project
.\setup-scripts\vrc-setup-script.ps1 -projectPath "C:\Path\To\UnityProject"

# Reset configuration
.\setup-scripts\vrc-setup-script.ps1 -projectPath "-reset"
```

## ⚙️ VPM Packages Configuration

The VPM packages included in the project are configurable in `setup-scripts/config/vrcsetup.json` using package names and versions.

Example config snippet:

```json
{
    "VpmPackages": {
        "com.vrchat.base": "latest",
        "com.vrchat.avatars": "3.5.0",
        "com.poiyomi.toon": "9.0.57",
        "com.vrcfury.vrcfury": "latest"
    },
    "UnityEditorPath": "<full path to Unity.exe>",
    "UnityProjectsRoot": "<folder where projects are created>",
    "UnityPackagesFolder": "<optional folder with extra .unitypackage files>"
}
```

- `latest` installs the newest version available.
- You can specify exact versions like `"3.5.0"` to lock to a specific release.
- The wizard validates versions with VPM (fail-fast) and can also show selectable versions from the local VCC repos cache.

## 🔎 Optional: `vrc-get` (better search + versions)

If you ship `vrc-get` as a local portable exe, the wizard can:
- Search packages using `vrc-get search <query...>` (works even when the local VCC repos cache is empty)
- List available versions using `vrc-get info package <id> --json-format 1`

Portable setup (recommended for this repo):
- Download the prebuilt Windows binary from the official releases.
- Put it under:
    - `setup-scripts/lib/vrc-get/` (any `*.exe` name is accepted; `vrc-get.exe` is preferred)

If the local exe is missing, the scripts fall back to `vpm` + local VCC cache.

## 🧠 Advanced naming

In `setup-scripts/config/vrcsetup.json` you can store naming preferences used when creating a project from a UnityPackage:

- Prefix/suffix
- Regex remove patterns (auto-clean the suggested project name)
- Remember a custom project name per UnityPackage path

## 🔄 Migration from Old Format
Older config files that used a simple array of package names are migrated automatically into the new dict format with `latest` as a default version.

### Easy Login policy

`dev.foxscore.easy-login` is a protected default package and is resolved from Fox_score's Vulpine Vault repository (`https://foxscore.dev/vpm/index.json`). The tracked default uses `latest`: setup resolves the newest stable release available at that moment, while the project's `Packages/vpm-manifest.json` records the exact resolved version for reproducibility.

If an imported avatar contains an old source copy at `Assets/EASY LOGIN`, setup verifies its package ID and moves it outside `Assets` to `.vrcsetup/backups/easy-login-assets-<timestamp>` before resolving the VPM package. This prevents duplicate Easy Login assemblies without deleting the imported copy. Run setup again on an existing project to refresh the package; close that project in Unity first.

## 📝 Changelog (summary)

- v2.0 - 26/10/2025: Added support for configurable package versions, migration, and validation.
- v1.0: Initial release with .unitypackage-based setup, VPM configuration, and interactive wizard.

## 🛠️ Advanced Notes

- The script integrates with Unity via the editor path configured in `setup-scripts/config/vrcsetup.json`.
- Drag & drop inputs often include quotes; paths are normalized automatically.
- Spaces, Unicode, `&`, parentheses, and wildcard-like characters such as `[]` are handled literally.
- Wizard path fields accept `%ENVIRONMENT_VARIABLES%`, `~`, and paths relative to the tool folder.
- UnityPackage mode lets you override the project name; the wizard remembers the last one.
- `UnityPackagesFolder` (optional) controls where the installer looks for extra `*.unitypackage` files to auto-import when creating a project from a UnityPackage.
    - If it's a relative path, it's resolved from the tool/repository root.
    - When missing/empty, extra-imports are DISABLED.
- Ensure PowerShell execution policies and system permissions allow the script to invoke Unity and modify project files.

### 🔍 Test mode, backups & logs

- `-Test` (dry-run): Run the script with `-Test` to print actions that would be performed without modifying the project or adding packages. Example:
```powershell
.\setup-scripts\vrc-setup-script.ps1 -projectPath "C:\Path\To\Project" -Test
```
- Backup: Before applying changes to `Packages/manifest.json`, the script creates a timestamped backup (`manifest.json.bak.YYYYMMDD-HHmmss`) in the same folder. If a change breaks the project, restore the original with:
```powershell
Copy-Item "<Project>\Packages\manifest.json.bak.YYYYMMDD-HHmmss" "<Project>\Packages\manifest.json" -Force
```
- Logs: the script writes `vpm` and execution logs to `setup-scripts/logs/` as `vrcsetup-YYYYMMDD-HHmmss.log`.

These features provide safe rollback paths without forcing any particular version policy. Keep in mind: we don't change versions automatically; pinning/upgrade decisions are still yours to set in `setup-scripts/config/vrcsetup.json`.

## Contributing

Contributions are welcome. Open an issue or a pull request with a description of the change.

## License
See the `LICENSE` file in this folder for license details.
