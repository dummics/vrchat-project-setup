# VRCSETUP Desktop migration

Owner approved the full implementation and remote branch on 2026-10-14.

Branch: feature/winui-desktop. Base: 438aa35 after preserving the earlier TUI work in c76fc5a and documentation in 438aa35. Both base commits were pushed to main. Daily installation is preserved.

## Verified checkpoint

- .NET 10 shared core: typed snapshots, immutable package-policy maps, SDK version alignment, dry-run planning, per-project staging/bulk targets and common executor.
- Infrastructure: atomic configuration with revision checking and legacy migration, project metadata/fallback/cache, local registry, VPM/vrc-get runner, hidden owned child-process guard, Unity import adapter and backup/recovery.
- CLI/TUI clients use the C# core; the old daily tool remains available while migration is being validated.
- Windows client uses WinUI 3 / Windows App SDK 2.5.1, native controls and shared package edit sessions. Unpackaged self-contained build succeeds with zero warnings.
- 58 .NET tests passed, including manifest/config formats, SDK/latest policies, package staging, stale review/busy/backup-failure paths, actual filesystem/cache/lock behavior and configuration revision conflicts.
- Existing PowerShell suite passed under PS7 and Windows PowerShell 5.1 before the branch was created; targeted progress/import-performance tests passed.

## Remaining work to satisfy the full mandate

- Independent review of the core/executor and filesystem/process side effects, followed by fixes and regression tests.
- CLI subprocess contract checks and real VPM/Unity E2E on disposable projects.
- Native WinUI dispatcher/render checks and screenshots. Owner asked to continue without mouse/keyboard input; no interactive desktop test is authorized in this session.
- Complete payload/installer tests, self-uninstall and preservation of user data/entrypoints; improve any incomplete parity surfaces found during verification.
- Translate every A01–A38 and B01–B03 row to verified evidence. Do not treat this checkpoint as completed migration.
- Documentation, release packaging, final targeted commit/push and clean branch status. No merge to main or daily-install promotion requested.

No automated goal was created. No personal memory is written. The approved plan is IMPLEMENTATION-PLAN.md; operational evidence stays in .work/artifacts/.
