# Audit Call Map

Date: 2026-02-18
Status: Completed (static audit)
Scope: flow map UI/business senza modifiche codice

## 1) Entrypoint e flusso top-level

`setup-scripts/vrc-setup-script.ps1:1`

- `-Wizard` o assenza `-projectPath` -> `Start-Wizard` (`setup-scripts/vrc-setup-script.ps1:14`)
- CLI mode con `-projectPath` -> `Start-Installer` (`setup-scripts/vrc-setup-script.ps1:18`)

## 2) Wizard orchestration

Funzione root:

- `Start-Wizard` (`setup-scripts/commands/wizard.ps1:1138`)

Main loop menu:

- `Show-Menu` principale (`setup-scripts/commands/wizard.ps1:1155`)
- Header globale con hint `ESC cancels` (`setup-scripts/commands/wizard.ps1:1148`)

Routing opzioni:

- First-run setup: `Invoke-FirstRunSetup` (`setup-scripts/commands/wizard.ps1:25`, called at `setup-scripts/commands/wizard.ps1:1140`)
- Setup project flow: `Setup-ProjectFlow` (`setup-scripts/commands/wizard.ps1:893`, called at `setup-scripts/commands/wizard.ps1:1167`)
- Package management: `Edit-VpmPackages` (`setup-scripts/commands/wizard.ps1:659`, called at `setup-scripts/commands/wizard.ps1:1170`)
- Reset config via installer: `Start-Installer -projectPath "-reset"` (`setup-scripts/commands/wizard.ps1:1179`)

## 3) Setup project flow (wizard -> installer handoff)

`Setup-ProjectFlow` (`setup-scripts/commands/wizard.ps1:893`)

Scelte:

- UnityPackage mode (`setup-scripts/commands/wizard.ps1:950`)
- Existing Unity project mode (`setup-scripts/commands/wizard.ps1:950`)
- Cleanup incomplete projects (`setup-scripts/commands/wizard.ps1:950`)

UnityPackage path:

- Drag/drop prompt: `setup-scripts/commands/wizard.ps1:1013`
- Project name prompt: `setup-scripts/commands/wizard.ps1:1047`
- Existing target decision menu: `setup-scripts/commands/wizard.ps1:1059`
- Confirm pre-run: `setup-scripts/commands/wizard.ps1:1082`
- Handoff installer:
- recreate overwrite: `setup-scripts/commands/wizard.ps1:1100`
- existing VPM only: `setup-scripts/commands/wizard.ps1:1103`
- existing VPM + extras: `setup-scripts/commands/wizard.ps1:1106`
- new create/import: `setup-scripts/commands/wizard.ps1:1109`

Existing project path:

- Drag/drop prompt: `setup-scripts/commands/wizard.ps1:1117`
- Confirm pre-run: `setup-scripts/commands/wizard.ps1:1126`
- Handoff installer: `setup-scripts/commands/wizard.ps1:1131`

## 4) Package management flow

`Edit-VpmPackages` (`setup-scripts/commands/wizard.ps1:659`)

UI primitives usati:

- package list menu: `Show-Menu` (`setup-scripts/commands/wizard.ps1:698`)
- add/search picker: `Show-MenuFilter` (`setup-scripts/commands/wizard.ps1:731`, `setup-scripts/commands/wizard.ps1:763`)
- version picker: `Select-VpmVersion` (`setup-scripts/commands/wizard.ps1:507`)

Key behavior:

- free-typed fallback in filter menu (`setup-scripts/lib/menu.ps1:347`, `setup-scripts/lib/menu.ps1:550`)
- confirm remove package with cancel hard option (`setup-scripts/commands/wizard.ps1:883`)

## 5) Installer flow e progress pipeline

Root:

- `Start-Installer` (`setup-scripts/commands/installer.ps1:78`)

Progress orchestration:

- overall progress (`Write-Progress` id=1): `setup-scripts/commands/installer.ps1:150`
- per-process progress (`Show-ProcessProgress` id=2, parent=1):
- create project: `setup-scripts/commands/installer.ps1:290`
- main import: `setup-scripts/commands/installer.ps1:370`
- extra imports: `setup-scripts/commands/installer.ps1:389`
- finalize: `setup-scripts/commands/installer.ps1:492`

Package install path:

- `Install-PackagesInProject` (`setup-scripts/commands/installer.ps1:12`)

## 6) Shared UI primitives e coupling

Menu library:

- `Show-Menu` (`setup-scripts/lib/menu.ps1:204`)
- `Show-MenuFilter` (`setup-scripts/lib/menu.ps1:338`)
- `Show-ChecklistPaged` (`setup-scripts/lib/menu.ps1:584`)

Progress library:

- `Show-ProcessProgress` (`setup-scripts/lib/progress.ps1:2`)
- cancel input (`Q/Esc`) + confirm prompt (`setup-scripts/lib/progress.ps1:48`, `setup-scripts/lib/progress.ps1:59`)

Coupling observation:

- forte presenza di `Read-Host`/`Write-Host` nel wizard oltre ai componenti `lib/menu.ps1`, quindi UI non completamente mediata da un adapter unico.

