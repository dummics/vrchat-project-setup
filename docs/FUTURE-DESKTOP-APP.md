# Future vision: VRChat Project Setup Desktop Companion

> **Status: future product direction — not current scope.**
>
> This document records a possible destination for VRChat Project Setup. It does not authorize a desktop-app rewrite, packaging change, or expansion of the current script's responsibilities.

## North star

Offer a small, installable Windows companion that makes the safe, repeatable parts of starting a VRChat Unity project approachable for a non-technical creator. A person should be able to download a release, double-click an installer, find the app from Windows Search, and complete a guided setup without needing to understand PowerShell, PATH, VPM, Unity project files, or command syntax.

The existing portable scripts, clickable `.bat` launchers, and `vrcsetup` command remain useful entry points. The desktop companion would be the friendly primary surface; its automation must be built on the same well-tested setup engine rather than a second implementation.

## Product boundary

This is a **setup companion**, not a replacement for VRChat Creator Companion (VCC).

| This companion owns | VCC continues to own |
| --- | --- |
| Guided creation or preparation of a Unity project from an avatar package or an existing project | VRChat account, SDK distribution, official project-management workflows, and VRChat platform services |
| Clear prerequisite checks and links to the official tools when they are missing | Installing, updating, or redefining VCC itself |
| Project-scoped AIO VPM management: review, add, update, or remove several direct packages in one operation | Repository publishing, ecosystem administration, or package management outside the selected project |
| Recoverable project setup, diagnostics, repair, and an understandable activity summary | Avatar/world authoring, building, testing, and uploading |

The app may hand the user to VCC at the appropriate point. It must never impersonate VCC, ask for VRChat credentials, or silently change unrelated Creator Companion projects/settings.

## UX principles

- **Click-first, command-capable:** Start-menu search and clickable Install / Repair / Uninstall are first-class. `vrcsetup` remains available for terminal users and automation.
- **Plain language, progressive detail:** Explain outcomes (for example, “Prepare this avatar project”) before exposing paths, package IDs, logs, or advanced settings.
- **One clear next action:** Home screen offers only useful choices: create from a UnityPackage, prepare an existing project, resume/recover, or open help.
- **Safe by default:** Preview material changes, validate inputs early, preserve backups, make cancellation explicit, and show exactly where the project/config/logs live.
- **Local and private:** No account, telemetry, or cloud dependency is required for the core setup experience.
- **Accessible and resilient:** Keyboard navigation, understandable errors, resumable operations, and a readable result screen are part of the product, not polish.

## Capability horizon (not a backlog commitment)

1. **Guided setup shell:** choose source/project, validate Unity/VCC/VPM prerequisites, show a final review, run the existing engine, and present a success or recovery summary.
2. **Project health and recovery:** detect interrupted `.vrcsetup` work, offer safe cleanup/resume choices, surface logs and backups, and run the already-defined repair path.
3. **Friendly package management:** compare the selected project's direct VPM packages, edit several choices in one screen, review the add/update/remove plan, and apply it once. Keep an advanced view for exact versions and paths.
4. **Optional integrations:** open the prepared project in the official tool or Unity only after the user confirms; no automation of upload/publishing flows.

## Current foundation: project library

The terminal application now owns a reusable project-catalog backend. It scans
the configured projects root, recognizes Unity projects from their standard
folders, reads Unity/VPM metadata, classifies avatar and world projects, and
keeps an incremental local cache so unchanged projects do not need to be parsed
again. Selecting a catalog entry routes into the existing project-scoped AIO
package workflow.

The terminal package workspace follows the same presentation contract: one
project-scoped bordered table carries a single readable version value and a
plain-language description of what saving will do. Left/Right changes the
version inline from a session cache; the full version list is progressive
disclosure rather than the default path. Package search, presets and optional
saved imports remain shortcuts on the same surface, with one visually separated
Save action. A future desktop table can replace keyboard focus with row clicks
or compact cell controls without changing the staged-change model underneath.

This is intentionally implemented below the presentation layer. Spectre.Console
is the current polished terminal client; a future Windows shell can consume the
same catalog records and refresh rules instead of introducing another scanner or
package engine. Creator Companion remains the official owner of its project list
and VRChat platform workflows; this tool does not modify or impersonate that UI.

## Packaging and architecture direction

- Ship per-user, without administrator rights, under `%LOCALAPPDATA%\Programs\VrcSetup` or the packaging equivalent.
- Keep a self-contained, versioned application core that owns validation, project operations, configuration migration, logging, and repair; UI, CLI, and clickable wrappers call that core.
- Release a compact downloadable archive with visible `Install`, `Repair`, and `Uninstall` launchers. Installation should create a searchable Start-menu entry and add the scoped `vrcsetup` launcher to the user PATH.
- Write an explicit installation record/version marker so local launchers can detect an installed copy and route intentionally: installed app for normal use, portable folder for no-install use, never an ambiguous mixture.
- Updates and repair must be transactional: validate the payload, preserve user configuration, replace only a verified previous install, and leave a recoverable backup on failure.
- Keep an offline-safe portable mode. It should remain usable directly from an extracted release and make clear that it is not the installed copy.

## Incremental delivery

1. Stabilize the present click-first script lifecycle: portable launch, install detection, installed command, repair, uninstall, Start-menu discoverability, and tests.
2. Continue extracting setup decisions from terminal rendering. The project
   scanner/cache is already shared backend work; creation and package orchestration
   should follow the same boundary.
3. Build a small Windows shell around the established engine; initially cover only the common happy path plus cancellation and recovery.
4. Package, test, and evolve the app only after real creator usability review confirms the flow is clearer than the script wizard.

## Success criteria

A first-time, non-technical user can download the release, double-click one file, find **VRChat Project Setup** from Windows Search, prepare a project using ordinary language, and understand what happened and what to do next. Advanced users retain the portable launchers and `vrcsetup` command, while both surfaces produce the same project result and diagnostics.

## Inspiration taken from ShutdownAT

ShutdownAT demonstrates the desired delivery pattern, not a UI to copy: a downloadable click-to-install package, per-user installation, Start-menu discoverability, a dedicated graphical surface, and a CLI/TUI that remain first-class clients of shared local behavior. Its installer also verifies payload structure, maintains installation metadata, creates only user-scoped PATH/shortcuts, and treats update/uninstall as recoverable lifecycle operations. The local sibling `_sdat` repository is the design reference; its relevant files are `README.md`, `Install SDAT.cmd`, `install.ps1`, `uninstall.ps1`, and `ROADMAP.md`.

For VRChat Project Setup, the equivalent should be deliberately smaller and purpose-specific: help someone get a project ready, then get out of the way.
