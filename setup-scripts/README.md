# VRChat Unity Project Setup - Modular Scripts

Structure:
- `vrc-setup-script.ps1`: main unified entrypoint with `-projectPath` or `-Wizard` flags.
- `setup.bat`: batch launcher that runs the wizard in the current terminal session.
- `commands/wrappers/`: optional legacy wrappers (vrcsetupscript.ps1, vrcsetup-wizard.ps1) kept for backward compatibility.
- `commands/installer.ps1`: `Start-Installer` + helpers (UnityPackage create/import + VPM install).
- `commands/wizard.ps1`: `Start-Wizard` logic and menu.
- `commands/cli.ps1`: project/package/create CLI verbs and machine-readable JSON output.
- `lib/`: shared helpers: menu, config, project scan/cache, Spectre presentation, progress, utils.
- `maintenance/`: internal per-user install, repair, uninstall, PATH and Start-menu integration.

Recommended install from the repository root:

```powershell
pwsh -NoProfile -File '.\setup-scripts\maintenance\Install-VrcSetup.ps1'
```

Open a new terminal and run `vrcsetup`. Use `vrcsetup repair` to restore the
alias, per-user PATH entry, missing config, and run a smoke test; use
`vrcsetup uninstall` to remove the per-user installation.
- `config/`: configuration templates + local config.
	- `vrcsetup.defaults` is tracked in repo and used as the first-run seed.
	- `vrcsetup.json` is generated on first run (or by the wizard), is local-only and gitignored (it contains local paths).
	- If the template is missing, the script generates a minimal skeleton config.
	- `unity-test-framework.dependencies.json` is tracked and provides the Unity Test Framework dependency snippet.

Wizard UX notes:
- Project library scans the configured projects root, reuses unchanged cached metadata, and opens a listed project directly in AIO package management.
- PowerShell 7 uses the bundled Spectre.Console assemblies across the complete interactive wizard: menus, path prompts, package search and project library. Windows PowerShell 5.1 uses the built-in text fallback.
- The home screen separates `Create project` from `Manage projects`; every Spectre screen shows its keyboard controls and an explicit Back action.
- Spectre panels use a restrained blue/slate palette, while required VPM packages appear first in the canonical base/avatar/resolver order and optional packages follow alphabetically.
- VT/ANSI escape sequences are opt-in: set `VRCSETUP_TUI_VT=1` only if your terminal supports them.
- Backend commands avoid printing progress lines to keep the TUI clean.
- Wizard clears the screen before running the installer to avoid leftover menu artifacts.
- Wizard clears the screen before text prompts (drag&drop paths) to avoid overlap with the menu.
- Action rows are visually separated (2 blank lines) and color-coded (Back = red).
- `Create project` offers a short UnityPackage path; `Manage projects` contains the library, a direct folder picker and incomplete-project cleanup.
- Existing-project AIO mode starts from the project's direct VPM dependencies, lets the user make several add/update/remove choices, then applies the reviewed set in one run.
- AIO synchronization skips dependencies whose exact version is unchanged.
- The VPM package editor supports change version/remove plus add package (type-to-filter). Only the VRChat base/avatar/resolver foundation is required; GoGoLoco and other starter packages are removable.
- Bugfix: "Add package" no longer throws and instantly returns to the list.
- Versions list is SemVer-sorted (e.g. 0.1.29 > 0.1.9).
- Version picker supports paging + filter patterns (e.g. *.9, X.X.1190, or re:<regex>).
- In paged lists, use Left/Right to change page.
- Settings presents Unity Editor and project-folder paths first, with project-name rules (prefix/suffix/cleanup rules) and per-UnityPackage remembered names in a separate screen.
- UnityPackage extra-import folder is configurable (Settings):
	- Config key: `UnityPackagesFolder`
	- By default it's DISABLED (no extra imports).
	- If set, the installer will also import every `*.unitypackage` found in that folder (besides the one you selected), after the main package (can be multiple).

Optional tooling:
- If a local `vrc-get` exe is present, the wizard can search packages and list versions even when the local VCC repos cache is empty.
	- Put the exe under `setup-scripts/lib/vrc-get/` (any `*.exe` name; `vrc-get.exe` preferred)

- Easy Login is optional. When selected, setup ensures the Vulpine Vault repository is registered and moves a verified legacy `Assets/EASY LOGIN` copy to `.vrcsetup/backups/` before installing the VPM-managed copy.

- Next steps:
- Continue modularizing by moving more logic into `commands/installer.ps1` and splitting into smaller commands.
- Gradual translation of messages to English.
- Add more tests and CI checks for `-Test` dry-run mode.
- Archive/Remove legacy scripts in root after validating wrappers and main entrypoint.
	- Legacy scripts are archived in `archive/legacy` (if present).
