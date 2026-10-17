# VRCSETUP Desktop migration

## Preview 12 installed

Installed implementation ae6ab80 on 17 October 2026: installer exit 0, 759 installed hashes and release checksums verified, settings unchanged and legacy launcher preserved. Nine focused fixture tests pass, including seven simultaneous jobs and shared-log reading. Release publication has zero compiler diagnostics. No GUI/terminal input injection or interactive visual acceptance was performed.

State-aware resume/missing-asset import, shared log-reader fix, startup recovery prompt, import timers, fixed header resource status and simplified controls/manual-input settings are implemented. TUI import context/progress boxes are restored without changing its shared core. No UI automation or terminal input injection was used.

Dom's Lulla Quest recovery succeeded: operation 209100be64ad4bd69eeb961a16670c5a, 18:30:25–18:30:46 UTC, backup preserved, no package additions/updates/removals, no UnityPackage reimport, final Unity settle/validation successful, Completed marker verified. GIGGLES NSFW independently finished at 18:27:29 UTC and was not interrupted. Its older namesake folder remains preserved/incomplete. Original Lulla timings: create ~7 s, VPM ~65 s, Unity import/compile ~283 s. No equivalent historical CLI baseline exists; no UI-vs-CLI performance ratio is claimed. Evidence: `.work/artifacts/2026-10-17-preview12-project-state/`.

## Preview 11 installed

Installed on Dom's request: implementation 003cd7d, installer exit 0, all 759 installed hashes verified, configuration unchanged and legacy launcher preserved. Release publication completed without compiler diagnostics after correcting a fixture-variable collision. No application/import or automated GUI test was started. Owner performs runtime acceptance.

Package selection uses stable observable rows and frozen ordering throughout the dialog session, including version edits/search results. Review and import shows immediate preparation feedback and inline failures. The local diagnostic log recorded repeated Unity-editor validation failures even though the saved editor executable now exists; the exact old in-memory value is not recorded, so this does not prove why it differed. Planning now uses visible editor/root fields and can resolve a unique installed Hub editor if the chosen executable is unavailable. No configuration is silently saved. Creation confirmation is a brief name/platform/preset/package-count recap; acceptance queues imports and leaves the same page ready for another file. Existing-project detailed reviews are unchanged.

Verification boundary: static review, Release build/publish and installed-file hashes; no app launch, native automation or actual import. Native regression-check source covers stable selection/version row instances, but is not executed. Evidence: `.work/artifacts/2026-10-17-preview11-import-review/`.

Previous installation: **2.0.0-preview.10**, implementation a5f0c40, superseded by preview 11. Its installation evidence remains `.work/artifacts/2026-10-17-unified-imports/installed-verification.json`.

## Preview 10 unified import page

Import projects is one responsive page: active jobs at the top, staged files and editable per-file name/platform/packages below. Adding files does not navigate away or follow an active job. Details and a bounded live console expand on demand. The GUI exposes no concurrency setting and imposes no two/six-job cap; sustained RAM/CPU pressure generates inline text warnings only. Reviewed job inputs remain fixed; cancel, logs and explicit recovery stay contextual. CLI optional positive worker counts and resource-based automatic scheduling share the same queue/core.

Validation: static code review and Release compilation/publishing only, with no new test-suite or real import execution. Evidence and package: `.work/artifacts/2026-10-17-unified-imports/`. Preview 9 counts below are historical, not evidence for preview 10.

## Preview 9 projects and multiple imports

Projects now has direct Open project, relative modification time, responsive rows and package management without Unity version/kind columns. The package sheet follows the supplied Creator Companion references while retaining batch Apply/Revert. The new [Imports queue](IMPORT-QUEUE.md) shares the core pipeline with CLI: multi-file review, one/two workers, per-project steps/log/cancel, persistent history, explicit reviewed recovery and bounded analysis-cache clearing. Existing source files/personal presets are not changed by recognition.

Verification: 104 .NET tests and 132 native WinUI checks pass; the installed executable also passes all 132 native checks. Native rendering covers five widths, direct project actions/date and the new Imports page, without mouse/keyboard injection. Installer exit0; all 759 installed payload files match their hashes, config preserved. Implemented build `03212f5`. The four requested files were planned as new destinations and started with two workers. At Dom's stop, only GIGGLES PC/Quest had been created and reached preparation/package work; Lulla PC/Quest remained queued. Verified owned CLI process 41776 was stopped; original archives and partial project directories are preserved. Next GUI initialization marks old active records Interrupted for explicit recovery. No four-import completion claim. Evidence: `.work/artifacts/2026-10-17-projects-import-queue/`.

## Preview 8 reviewed installation

Research intake is recorded in [RECOGNITION-RESEARCH.md](RECOGNITION-RESEARCH.md). Search/version metadata no longer populate the personal package registry; transient discovery results stay out of the unfiltered library unless selected/favorited. Existing saved entries are preserved. Recognition excludes known world-only identities; the metadata seed contains 3,410 observations across 12 identities. Existing world project workflows remain available.

Review also corrected loss of pending changes on window close and numeric overflow while sorting malformed package versions. The shared planner/executor, terminal workflow and explicit staged application remain intact. This delivery uses static review, Release compilation/publishing and installed-file integrity verification. No new test-suite, UI automation or Unity import run is claimed. Evidence: `.work/artifacts/2026-10-17-preview8-reviewed-install/`.

Installation completed successfully: preview 8 implementation `4bd3c36`, 759 installed payload files match their manifest hashes, EXE/ZIP checksums verified, zero compiler diagnostics. Settings and the separate legacy launcher retain their baseline hashes; Start-menu target and command PATH entry are correct. Preview 6 remains in the installer's `.previous-*` rollback directory. The app was not launched. New research signatures and the remote harvester remain unimplemented, not silently added to this release.

## Preview 7 offline dependency inspection

The core and infrastructure now provide bounded streaming UnityPackage inspection, GUID-based advisory classification, an included metadata seed, personal signatures and observation caching. The CLI adds inspect/--deep/--index-project; the desktop automatically inspects selected files without blocking Create and adds suggestions to the existing package dialog. Pin/SDK-kind preservation, explicit range choices, stale-request rejection and staged acceptance are covered by tests. See [inspection behavior and delivery boundary](PACKAGE-INSPECTION.md).

The first delivery was local-only. Repository-content downloads and broad online creator-package discovery remain subsequent work; manual catalog search is available. Historical evidence: `.work/artifacts/2026-10-17-inspection-preview7/`. Its installer was not executed during the preview 7 hold.

Final candidate: 99 core tests and 112 published native checks pass. Installer/ZIP/checksums are in `release-verified`; implementation `5957763`. Real-corpus scans are approximately 0.09/0.43/1.23 seconds; cached Lulla is 0.27 seconds including process startup. Installed preview6 and its configuration hashes remain unchanged. This is tester-ready packaging, not a claim of human acceptance or clean-machine validation.

## Preview 6 responsive polish

Navigation automatically collapses at narrow widths. Creation fields and preset actions stack when their viewport is below 560 pixels; the chosen filename stays on one ellipsized line. Native verification includes 520 and 760 pixel windows alongside the existing three widths.

The shared creation/package selection dialog uses a taller flat list, per-row version dropdowns, asynchronous search as you type and small content-footer actions. The title is reduced; the separate version-options panel and Search button are removed. Cancel/dismiss returns no edits; Done explicitly accepts the working copy.

Favorite rows notify property changes in place. Library refresh reconciles rows instead of resetting the collection, preserves visible order and defers favorite-filter membership changes until re-entry or a deliberate filter change. Evidence lives under `.work/artifacts/2026-10-17-responsive/`.

## Preview 5 package workflow

Project management now has explicit Projects/Back navigation, Revert and a contextual batch Apply strip. The Installed column itself opens the version dropdown; Choice/After saving and the long action menu are removed. Rows show only a small pending-change hint. Navigation asks before discarding staged project/preset edits, including clicking an already selected Projects navigation item. Returning from the package browser preserves the active batch.

The project Add package action and the My packages preset tab share a reusable in-page browser. Local definitions are immediately filterable/addable; typing also starts debounced, cancellable catalog/repository/VRC-Get lookup in the background. The project browser has no preset-editing tab or preset selector. Project-observed definitions are remembered in the local registry.

Preset and project versions use native row dropdowns with a beta filter. A shared bounded single-flight cache moves synchronous catalog/tool work off the dispatcher, prevents repeat lookups for pending requests, preserves existing pins, and retries empty/faulted results. No version ContentDialog is queued by these rows.

Create clipping was reproduced with copied real settings: form right1238 exceeded client1164 although viewport-only checks looked valid. Explicit viewport-sized, left-aligned forms and resize handling now put the form at249..1009 in the same client. The native test checks full-window bounds as well as the local viewport. Candidate evidence is under `.work/artifacts/2026-10-16-package-flow/`; personal project data and the preview4 installation are not changed.

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
