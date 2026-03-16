# VRChat Unity Project Setup - Modular Scripts

Structure:
- `vrc-setup-script.ps1`: main unified entrypoint with `-projectPath` or `-Wizard` flags.
- `setup.bat`: batch launcher that runs the wizard in the current terminal session.
- `commands/wrappers/`: optional legacy wrappers (vrcsetupscript.ps1, vrcsetup-wizard.ps1) kept for backward compatibility.
- `commands/installer.ps1`: `Start-Installer` + helpers (UnityPackage create/import + VPM install).
- `commands/wizard.ps1`: `Start-Wizard` logic and menu.
- `lib/`: shared helpers: menu, config, progress, utils.
- `config/`: configuration templates + local config.
	- `vrcsetup.defaults` is tracked in repo and used as the first-run seed.
	- `vrcsetup.json` is generated on first run (or by the wizard), is local-only and gitignored (it contains local paths).
	- If the template is missing, the script generates a minimal skeleton config.
	- `unity-test-framework.dependencies.json` is tracked and provides the Unity Test Framework dependency snippet.

Wizard UX notes:
- Main menu uses arrow-key selection.
- TUI rendering is more stable (reduced flicker) and uses a modernized color theme.
- VT/ANSI escape sequences are opt-in: set `VRCSETUP_TUI_VT=1` only if your terminal supports them.
- Backend commands avoid printing progress lines to keep the TUI clean.
- Wizard clears the screen before running the installer to avoid leftover menu artifacts.
- Wizard clears the screen before text prompts (drag&drop paths) to avoid overlap with the menu.
- Action rows are visually separated (2 blank lines) and color-coded (Back = red).
- "Setup project" is unified: choose UnityPackage or existing project.
- VPM packages editor is 2-step: select package → choose action (change version/remove), plus add package (type-to-filter).
- Bugfix: "Add package" no longer throws and instantly returns to the list.
- Versions list is SemVer-sorted (e.g. 0.1.29 > 0.1.9).
- Version picker supports paging + filter patterns (e.g. *.9, X.X.1190, or re:<regex>).
- In paged lists, use Left/Right to change page.
- Advanced settings includes naming rules (prefix/suffix/regex remove) and per-unitypackage remembered project names.
- UnityPackage extra-import folder is configurable (Advanced settings):
	- Config key: `UnityPackagesFolder`
	- By default it's DISABLED (no extra imports).
	- If set, the installer will also import every `*.unitypackage` found in that folder (besides the one you selected), after the main package (can be multiple).

Optional tooling:
- If a local `vrc-get` exe is present, the wizard can search packages and list versions even when the local VCC repos cache is empty.
	- Put the exe under `setup-scripts/lib/vrc-get/` (any `*.exe` name; `vrc-get.exe` preferred)

- Next steps:
- Continue modularizing by moving more logic into `commands/installer.ps1` and splitting into smaller commands.
- Gradual translation of messages to English.
- Add more tests and CI checks for `-Test` dry-run mode.
- Archive/Remove legacy scripts in root after validating wrappers and main entrypoint.
	- Legacy scripts are archived in `archive/legacy` (if present).
