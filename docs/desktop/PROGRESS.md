# VRCSETUP Desktop migration

Status: visual polish revision, 16 October 2026. Version: **2.0.0-preview.4**, Windows x64.

## Preview 4 visual polish

Projects keeps its existing flow, with aligned rows and a per-project package-management icon replacing normal Ready/count text. Exceptional project states retain a warning icon. Create and Settings use constrained, stretching scroll content to prevent fields and actions overflowing the viewport.

Packages has a centered preset selector and separate Preset packages / My packages tabs. Local packages appear as cards with favorite stars and inclusion state for the current preset. Row menus contain secondary actions; the save/discard strip only appears for pending changes. Preset rename preserves staged edits, creation overrides and case-insensitive template references. Required package removal stays disabled.

The compact version dialog defaults to the current choice; Automatic represents the same `latest` engine policy. Search, previews and exact versions remain under Advanced. Activity moves to footer navigation and uses structured entries, an empty state and a contextual project-actions menu. Restore and unfinished-project actions remain available without developer mode. Settings shares field widths, grouped surfaces and aligned switches.

The core and terminal client are unchanged. Evidence and the new installer/portable build live under `.work/artifacts/2026-10-16-polish/`. Native fixture verification covers 1024, 1180 and 1366 pixel window widths, package-menu data context, rename/template references, library state and form bounds. Human desktop input remains outside these automated checks.

## Preview 3 usability revision

Dom's original PowerShell TUI is available immediately through the independent `vrcsetup-legacy` installation. It is retained while the GUI is evaluated. The shared-core C# TUI now follows its primary Import / My projects flow, panel presentation, searchable/multiselect project library and keyboard package workspace. Nested Back/Esc and version lookup failures preserve staged edits; recovery remains reachable.

The desktop Create page now has an obvious drop zone, automatic editable name, destination, one platform choice and preset customization. Optional templates are collapsed. Presets have a visible Default/named selection, direct staged editing, Add package, favorite stars and a local My packages library with asynchronous stale version refresh. The idle operation footer is hidden; Activity replaces Activity & recovery in normal navigation. Developer tools expose shared JSONL diagnostics and runtime flags.

The reported blank/error Projects screen did not reproduce with copied real settings: the final native startup loaded 45 projects without errors (43 at the initial check, before the owner added more). Catalog failures no longer prevent project scanning, initialization callbacks are gated, malformed project metadata is isolated, and unavailable roots/empty searches have explicit messages and a folder-selection action. No personal Unity project was modified by these checks.

Preview 3 artifacts and verification are retained under the canonical checkout's `.work/artifacts/2026-10-16-usability/`. The original migration's Unity and lifecycle evidence below remains applicable; the new UI tests use fixture projects and native rendering without mouse/keyboard injection. The running preview 2 instance is preserved; install the new package after closing it normally.

## Previous migration checkpoint (preview 2)

Dom approved the complete migration, dedicated remote branch, installer and local installation. Branch: `feature/winui-desktop`; stable main is retained without a merge. Preexisting work was preserved and pushed as `c76fc5a` and `438aa35` before the feature branch. Core/client checkpoint: `7436185`; completed implementation: `31e54eb`; corrected installer migration and preview 2: `24f2cdc`.

## Delivered

- Shared C#/.NET 10 core and infrastructure, first-class CLI/Spectre TUI, native WinUI 3 desktop client and per-user standalone installer.
- All A01–A38 parity rows and B01–B03 additions implemented; see [verification map](VERIFICATION.md). Creation recipes preserve setup policy; they do not clone entire private projects.
- Project library, creation, package version control, presets/defaults/favorites, bulk targets, review, logs/reports, import pipeline, backup/recovery, cancellation and lifecycle operations use the shared engine.
- Old PowerShell engine and its implementation-specific tests retired on this branch. Configuration/resources and the Unity-compatible import bridge are retained in their new source locations. Main and Git history preserve the previous implementation.
- Standalone EXE, portable ZIP and SHA256SUMS are in the canonical checkout's `.work/artifacts/2026-10-14-winui-migration/release-2.0.0-preview.2/`. Generated binaries are not committed to Git or published as a GitHub release.
- Preview 2 installed under `%LOCALAPPDATA%\Programs\VrcSetup`. Legacy settings were recovered and verified; the final upgrade preserves the configuration hash exactly. Original legacy and previous .NET program copies remain available as rollback backups. User PATH, command alias, Start-menu target and installed payload integrity verified.

## Verification

- 76 .NET rule, planner/executor, filesystem/config, failure/recovery and CLI subprocess tests pass.
- 17 real Unity/VPM E2E checks pass on two disposable projects, including CLI/shared-service creation, UnityPackage import, compiled probe and bulk add/remove/preset operations.
- 18 native WinUI dispatcher/render checks pass on the self-contained published executable, with development runtime paths excluded. Six screens, two window sizes, dark theme and a package dialog are covered. No mouse/keyboard input was injected.
- 17 standalone installer checks pass: legacy migration, install, upgrade, alias repair, running-CLI self-uninstall, settings/project preservation and shortcut ownership. The omitted-home portable-marker regression has a separate .NET test.
- Release desktop/CLI/installer builds succeed with zero warnings. Independent review findings and live packaging/migration defects were fixed before handoff.
- Disposable Unity projects and obsolete package candidates were removed after retaining evidence. Useful checks, native captures, logs, final distributables and live-install proof remain under `.work/artifacts/`.

## Acceptance boundaries

Dom requested tests without desktop input. Human mouse/keyboard, screen reader, actual multiple-DPI displays and a clean tester PC remain acceptance checks, not claimed results. Android/iOS modules and exact prerelease installation were not exercised live. Unity/VCC/VPM and VPM's compatible runtime remain external prerequisites; the application itself bundles .NET/Windows App SDK runtimes. The installer is unsigned.

Source changes and this handoff are committed/pushed on the feature branch. No personal-memory write or automated goal was created. [Distribution instructions](DISTRIBUTION.md) describe installation, portable mode, CLI and recovery.
