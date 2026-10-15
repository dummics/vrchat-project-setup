# VRCSETUP Desktop migration

Status: implementation complete for tester handoff, 16 October 2026. Version: **2.0.0-preview.2**, Windows x64.

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
