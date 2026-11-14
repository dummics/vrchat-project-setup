# Desktop migration verification

## Candidate after 0.3.2

The desktop lets users choose a ceiling from one to eight simultaneous imports.
Core fixtures cover FIFO admission, changes to the ceiling during execution,
low memory, unavailable samples, pauses, cancellation and duplicate project
destinations. The synthetic three-import run in `.work/mq4/multi-import.json`
completed in 111.55 seconds with three admitted jobs. Its minimum sampled free
RAM was 40.83 GiB and free commit was 42.07 GiB. The three Unity import logs
also contain timestamped editor start and shutdown messages from distinct
processes, with at least 7.56 seconds of observed overlap
(`.work/mq4/unity-process-overlap.json`). This confirms concurrent editors
in addition to queue admission. The prior two-job run took about 209 seconds
under different cache and load conditions, so the timing cannot isolate the
effect of the higher ceiling.

The native smoke verifies per-import VPM exclusions, pins and the package
restore confirmation path. The Italian smoke checks the visible suggestions
action at 720 pixels. The local isolated installation lifecycle verified install,
upgrade, repair and uninstall of the candidate. The package reinstall fixture
verified the actual VPM tool on a deliberately damaged file and checked the
package version, manifest, recovery archive and a file in Assets.

Historical incident classification:

| Observation | Evidence and current behavior |
| --- | --- |
| Layla compilation errors | The retained log contains C# compilation errors from ThryEditor. A separate real Unity fixture demonstrates that an imported asset followed by a compilation error can complete with a compilation notice. It also verifies recovery after correcting the script. |
| Mia ThryEditor conflict | The avatar archive contained legacy scripts with the same GUIDs as installed VPM files. Three damaged package files matched the archive exactly. A clean package restore retained Toon 10.0.29 and ThryEditor 2.74.4, and Unity then compiled with exit 0. Source fingerprints now detect this kind of import overwrite and expose an explicit package restore action. |
| Trixie import and cancellation | The short-path reproduction imported successfully. A long-path run recorded asset errors before reaching the import end marker and timing out while closing. The original incident still has no proven cause. |
| Process or inherited output pipe | Synthetic process tests exercise cancellation and timeout with an inherited pipe. They verify that the app can distinguish this condition from a successful Unity exit. |
| Appearance crash | Two-process native persistence smoke covers saving and loading appearance. The original crash has not been reproduced. |

The historical logs do not establish simultaneous imports as the cause of
either avatar incident. The queue and Unity probes use isolated projects.

## Priority import recovery verification after 0.3.1

Released as 0.3.2 from `b993212`. Windows release run
[43499972984](https://github.com/dummics/vrchat-project-setup/actions/runs/43499972984)
passed the full hosted checks, including native UI, Italian layout, appearance
restart, VPM registration, installation, repair and application update. The
public release contains the installer, portable archive, components and checksums;
the static update feed lists `v0.3.2`. Local core suite: 514 passed. Main and
the development branch were aligned after the automated README/feed updates.

The two-job automatic admission cap remains deliberate; raising it and extending
the detector corpus are separate follow-up work. The original Trixie stall and
appearance crash were not reproduced and are not claimed fixed by these results.

The local real-Unity fixture `.work/c1/compilation-import.json` passed on
Unity 2022.3.22f1: importing a deliberately broken script did not stop the
following text-asset archive, the driver reported a completed compilation
notice, and correcting the script settled without reimporting the archives.
This is synthetic evidence, not an audit of a personal avatar or its packages.

Native smoke `.work/artifacts/ui-priority-import/ui-smoke.json` passed 633
checks, including requeueing an owned paused creation at its original absent
destination without a new-copy prompt. The enqueue was intercepted before
execution; the test does not launch Unity for that paused request. Screenshots
at 720 pixels show cancellation directly on each collapsed import row.

Core tests cover compilation warning versus blocked import, absence of backups
for new creation, persistence of dismissed notices, and conservative migration
of older generic failures using explicit saved log evidence. Italian labels
and a short VPM resolver failure message have separate tests. Existing
pause/cancellation, project ownership and restore tests remain in the suite.

## 0.3.0 release and subsequent multi-import work

Version 0.3.0 was published from merge commit `ee399f9`; Windows release run `42527738219` passed, and the public installer checksum and update feed were checked. Earlier candidate notes below describe historical checkpoints, not the current release status.

The subsequent desktop change requests automatic scheduling. The queue admits at most two imports, starts with one until CPU data is available, and falls back to one when CPU or memory is under pressure or sampling is unavailable. A lower limit does not interrupt work already running. This is a conservative admission policy, not a guarantee against resource exhaustion, and it is not included in the 0.3.0 release.

Isolated queue tests exercise changing CPU and memory samples, missing samples, sampler errors, cancellation, pause, failed journal writes, and concurrent configuration. A synthetic Unity queue run completed three imports with a peak of two concurrent jobs. The synthetic archives are small; these results do not establish resource use for large avatars, child processes, or sustained system load. Further measurement and concurrency review remain necessary before delivery.

The E2E fixture records import duration separately from its full lifecycle. One isolated run measured 91.56 s; a later three-job run completed in 209.96 s overall, with individual import phases of 97.96, 93.51 and 89.69 s and a peak of two admitted jobs. These are single observations without controlled load or cache; they are not a performance guarantee or a large-avatar benchmark.

A further three-job run completed in 209.30 s with the same peak admission of two. Sampling the runner and descendants by parent PID observed a peak summed working set of 2,428.3 MiB over 91 samples, with up to 57 processes. The sum may double-count shared memory; short-lived or reparented descendants may be missed. It does not measure commit, CPU, or large-avatar behavior.

The optional E2E `--verify-unity-cancellation <Unity.exe> <new-fixture-directory>` starts Unity only on a new synthetic fixture, observes the owned process while it is alive, requests cancellation and waits for that process to exit. The local `uc3` run passed. It exercises one point during startup/import; it does not reproduce the reported Trixie stall, establish its cause, or cover every Unity phase.

## 0.3.0 refactor candidate — verification pending

At this historical checkpoint, the desktop history was integrated into main at f347f1a with source version 0.3.0;
the 0.3.0 release and tag did not yet exist. The integration passed 346 core tests and
the Release x64 desktop build. Those results predate the refactor below.

The refactor branch replaces the preset carousel with a native ComboBox and
the animated hover actions with a labelled native menu. Appearance editing is
extracted into its own control, with explicit subscription lifetime. Settings
restore suppresses intermediate palette updates. Appearance
errors are logged and surfaced through the existing banner. The reported crash
has not yet been reproduced or attributed to a confirmed root cause.

Activity and Settings now use NavigationView footer items rather than separate
buttons and hand-maintained selection markers. Native footer activation and
selection checks have been adapted but remain unexecuted. The palette resources
are applied by the shell at startup, independently of loading the settings view;
picker synchronization stays inside AppearanceEditor. The smoke fixture uses a
distinct saved canvas color before opening Settings, then restores Mint for the
remaining screen captures.

Custom accent fills are confined to Default/Light theme dictionaries; contrast
themes retain system highlight foreground/background resources. Appearance-only
settings saves avoid project rescans. Empty search results offer clearing the
query. Project rows expose their Unity version and own their responsive layout;
import resource telemetry is available from options rather than the page header.
Configuration normalization now preserves every supported project sort, including
reverse and creation-date order; previously it retained only name or recent.
Settings-only roundtrip theories cover these choices and appearance persistence
without creating Unity project fixtures.

Native smoke coverage has been updated for the new preset selector and extended
with isolated appearance/persistence and empty-search recovery checks. These
changes have not been built or run: native UI, Unity, installation and other
invasive verification are deferred while the owner uses the PC. Dark/light,
high contrast, keyboard, narrow layout and save/restart acceptance remain open.
Historical check counts and screenshots below describe their named candidates.

## 0.2.6 package migration

`dotnet run --project tests/VrcSetup.E2E -c Release -- --verify-package-migration <fresh-evidence>`
uses public VPM packages in an isolated project: Toon 10.0.27 → Pro downloader
11.0.38 → remove already-absent Toon → Pro 10.0.11 → Toon. It verifies actual
embedded and locked results, preserving exact downgrade choices. It never starts
Unity, signs into Patreon or registers fixtures with Creator Companion. Core
regressions cover conflicting explicit choices, missing locks, phantom removals,
version-specific migration metadata, physical/offline rollback and backup retention.
The closed-target requirement introduced earlier in 0.2.6 was superseded for
coordinated VPM changes by 0.2.6-beta.4. Asset imports launching a second editor
and full snapshot restores still need exclusive editor access. Backup of
shader-heavy Assets may take additional time.

The unattended `FlowInteraction` file picker treats a null choice as cancellation.
It must not delegate to a native picker: hosted release runners have no Unity
editor, and the creation check would otherwise wait for desktop input until the
workflow timeout. A smoke assertion covers this cancellation before creation.

For update performance, `--measure-component-update <components.json> <installed-root> <isolated-cache> <fresh-evidence>` on the E2E runner reads the installed root,
prepares verified components, then installs to its evidence folder with shortcuts
and PATH changes disabled. It records preparation/install phase timings and actual
network bytes in `timings.json`. Never point the cache or evidence at owner data.
Component preparation runs at most four disjoint components concurrently and
checks extracted bytes while writing; the installer still validates its staged
payload before replacement. Corruption/cancellation tests cover failure.

## Isolated verification and Creator Companion

Verification compositions and CLI children use `--no-vcc-registration`; direct
Unity driver fixtures use `registerProjects: false`. This is a transient test
option, never a saved user preference. Real user project creation retains normal
VPM registration. Package-mix, policy, native project-flow and CLI E2E verification
must not register disposable projects in the owner's VCC list or rely on cleanup
after registration. Existing test registrations can be removed with the official
`vpm remove project` command, which does not delete their project files.

## Projects regression candidate (23 October 2026, after beta.27)

The supplied tester diagnostic reproduced generated `com.vrcfury.temp` requests and a coherent VPM-selected NDMF prerelease being treated as a failed stable install. The candidate excludes only known generated identities, passes `latest` unpinned to the bundled tool, accepts its coherent resolution and retains exact pin/physical-lock checks. Native field validation blocks missing/invalid names with contextual feedback.

282 core tests and 400 native UI checks pass, plus the separate Italian startup/render check. They cover generated preset preservation, version/presence postconditions, bundled command translation and removal proposals, retry/dismiss persistence, profile round trips, log budgets and archive/install-cache retention, centered carousel/form layout, one-time choices, New/Partial creation retry without nested action locks, Activity before a report exists, the central Apply overlay and a compact operations flyout with 40 rows bounded to 200 pixels. UI evidence: `.work/artifacts/2026-10-23-projects-regression/ui-proof/` and `ui-it/`.

`--verify-projects-ui <editor> <new-evidence-directory>` additionally drives the actual native creation button with nine requested packages: Resolver, SDK Base/Avatars, Poiyomi, VRCFury, Modular Avatar, NDMF, AudioLink and Gesture Manager. It verifies unchanged saved presets, no Unity launch for empty creation, physical/lock consistency, a VPM-reported dependency removal conflict blocked before mutation, promotion of an implicit dependency and mixed-present bulk AudioLink apply across two newly created projects. Unity 2022.3.22f1 then explicitly compiles the isolated package mix: exit 0, probe marker present, no C# compiler errors. Evidence: `.work/artifacts/2026-10-23-projects-regression/real-ui-proof/projects-flow.json`. No owner project or installed app was changed. This validates the tested package combination on this machine; clean-VM acceptance remains a separate environment check. No release was published for this candidate.

## Package hierarchy and motion

The preset editor uses a text carousel with previous/next actions and a clickable choice list. Included packages use individually bordered rows on the page background; the addable catalogue uses quieter bounded cards or rows below a larger gap and a single divider. Neither section has an enclosing box. Included rows and catalogue share one scroll; a reserved scrollbar gutter keeps actions inside the available width. Resolver and Base precede the removable SDK. Preset actions expand to the right without moving the title, support keyboard focus/Esc and close after pointer departure; the title rolls gently on preset changes. Information expands as one clipped, animated footer surface, with a popup fallback when space is insufficient. Motion respects the system animation preference. Tooltips wait for 2.5 seconds of stationary hover, including compact navigation and repeated hovers.

## Project management and library

New catalogue additions always request `latest`, independently of a version remembered by the local package index. Existing project/preset pins and explicit version choices are preserved. Package edits reconcile the managed collection by ID instead of clearing it; catalogue Added toggles update the existing observable card. Native reposition transitions animate row moves. Package actions preserve the clicked card's viewport anchor across changes to the section above it, clamped only when the remaining content cannot support the previous offset. Fixture checks use cached fixed-version definitions to cover this distinction and verify collection/card identity and scroll position after toggling an addition.

Management embeds the addable catalogue below the project packages in one scroll. Its initial membership excludes packages already included when management opens; pending additions stay in the catalogue as Added and toggle back to Add until Apply. Managed rows expose pending addition/removal/version state. Apply reloads the project and establishes a new catalogue baseline. Revert restores the original policy. Leaving management requires explicit confirmation before discarding edits; cancelling navigation retains all pending versions. Project-library selections are cleared when opening management, preventing a later batch from reusing an old selection.

Favorite catalogue rows move to the top immediately when the star changes; the filter also applies immediately. The project library uses a compact search/refresh toolbar and clickable Name/Modified/Created headers, each cycling order/reverse/default without rescanning disk. Creation is the project folder creation time. Checkboxes appear after the first selection; the batch action appears in the toolbar only for two or more projects and covers no list rows. Native fixture checks cover these transitions, actual row containers, menu expansion bounds and staged additions; core tests cover independent date/name ordering and all three sort cycles.

Release compilation reports zero warnings/errors. Native WinUI fixture checks exercise the carousel buttons, stable version rows, tooltip delay/reshow, intermediate expansion heights, footer navigation and action bounds at 720, 900, 1024, 1180 and 1920 pixels. Dark/light screenshots and a separate Italian startup/render check are recorded under `.work/artifacts/2026-10-18-package-polish/`. These checks use isolated data and the actual WinUI renderer; no owner installation or Unity project is changed.

## Preview 12 verification

Installed implementation ae6ab80: installer exit 0, all 759 installed file hashes match, EXE/ZIP checksums verified, config unchanged, legacy command preserved. Nine targeted fixture checks pass on the final source. Release GUI/CLI/installer publishing reports zero compiler diagnostics. Positive missing-asset archive generation is verified with synthetic fixtures; the real Lulla recovery required no new asset import and validated Unity settle/completion instead.

Focused temporary-fixture checks cover writer-compatible log reads, missing-asset filtering that preserves edited paths/moved GUIDs, state/source classification, durable queue cancellation/restart and seven simultaneous jobs without an artificial worker cap. No desktop input is injected. Real Lulla Quest recovery completed with a backup and final Unity validation only; original package versions/assets were preserved, with no full avatar reimport. Evidence and reviewed plan: `.work/artifacts/2026-10-17-preview12-project-state/`. TUI keyboard/layout acceptance remains with Dom; source compilation is not a visual acceptance test.

## Preview 11 verification boundary

Installed implementation 003cd7d: installer exit 0, all 759 installed file hashes and release EXE/ZIP checksums verified, configuration hash unchanged, legacy launcher preserved. Release publication reports zero compiler diagnostics. No runtime/GUI/import execution; acceptance is pending with Dom.

Static review covers stable package row reconciliation and selection restoration, read-only editor fallback, current UI settings snapshot, inline preparation/failure feedback and a compact creation-only confirmation. Native assertion source now checks toggle/version identity and order preservation; no suite, GUI automation or Unity import is run. Release compilation/publishing and subsequent owner-authorized installation are recorded in `.work/artifacts/2026-10-17-preview11-import-review/`. Runtime acceptance remains with Dom.

Historical preview 10 installation: Windows x64, implementation a5f0c40; exit 0, 759 files verified, config unchanged and legacy launcher present. Superseded by preview 11. No clean Windows VM or human interactive acceptance is claimed.

## Preview 10 verification boundary

Reviewed draft snapshot/revision preservation, unified navigation, narrow field layout, stable job instances with active-first ordering, shared FIFO scheduling/cancellation, resource warning hysteresis and bounded asynchronous log reads. Release GUI and CLI compilation/publishing validate the candidate's syntax/resources. No test suite, UI automation, app launch, actual Unity import or installation is executed for this revision. The native smoke-check source follows the unified page but was not run. Evidence: `.work/artifacts/2026-10-17-unified-imports/`. Earlier test counts and installed-file checks apply only to their named revisions.

## Preview 9 verification

104 .NET tests pass, including durable queue completion/restart, a maximum of two concurrent jobs, queued cancellation without execution, duplicate target rejection, interruption without automatic resume, cache clearing that preserves knowledge/history and malformed-version sorting. Native published WinUI checks pass 132 assertions at 520/760/1024/1180/1366 pixels, including Projects action bounds/date materialization and the Imports idle layout. No input injection is used. Source and native evidence: `.work/artifacts/2026-10-17-projects-import-queue/`.

Real import target selection was limited to the four requested Desktop UnityPackages. Existing projects/source archives and personal preset pins are preserved. The GIGGLES PC archive's PCS editor assembly explicitly references Modular Avatar/NDMF, so its reviewed project plan adds Modular Avatar; this is not a change to the default preset or an automatic promotion of generic text hints.

Final installed build `03212f5` passes all 132 native checks. Distribution hashes and 759 installed files verify; config unchanged, installer exit0. The live queue started two GIGGLES projects and kept both Lulla projects waiting. Both Unity-create logs report successful initial project creation, followed by VPM preparation. Dom then requested stopping all further tests; verified owned CLI41776 was terminated and no further import/recovery checks were run. Partial GIGGLES folders are retained, with history converted to Interrupted at next owner GUI initialization. Avatar import/compilation and real recovery are NOT validated to completion. `real-import-plan.json`, original progress logs, `source-packages.json` and `owner-stopped-tests.json` preserve exact scope/evidence.

## Preview 8 verification boundary

Review follows input selection/background analysis, isolated dialog working copies, manual acceptance, creation review, shared planner/executor, existing-project fingerprints/backups, batch application, navigation/close protection, version/cache persistence and installer rollback/config preservation. Confirmed defects fixed: lookup/search adding personal entries, world-only recognition escaping avatar scope, unguarded window-close drafts and malformed-version numeric overflow. The research's unverified signatures are not treated as production data.

Validation for this revision consists of Release compilation/publishing, static inspection of resources/data and hash-based verification of the resulting installation and unchanged settings. No test suite, native UI automation, actual Unity import or functional subprocess check is run for preview 8, as requested. Previous preview 7 test counts below are historical evidence only and do not establish that these new changes passed those tests. Detailed evidence and installation outcome live in `.work/artifacts/2026-10-17-preview8-reviewed-install/`.

Completed outcome: published GUI/CLI/installer identify implementation `4bd3c36`; publishing exits successfully with zero compiler warning/error diagnostics. Both distributable SHA256 values and all 759 payload hashes verify before and after the silent per-user installation. Installer exits 0 and reports Success=true, with no visible installer window observed. Config and independent legacy launcher hashes are unchanged; shortcut/alias/rollback directory verify from filesystem and Windows metadata. No GUI/CLI application startup or project mutation is performed. The seed contains 3,410 metadata observations for 12 identities, excluding SDK Worlds. See `CODE-REVIEW.md`, `payload-verification.json`, `installer-result.json` and `installed-verification.json` in the artifact directory.

## Preview 7 offline inspection

The .NET suite passes 99 tests. New generated-archive cases cover member ordering, no extraction, internal references, sample downgrading, duplicate GUID owners, manifests/ranges, budget exhaustion, invalid gzip, changed files, corrupt caches, included-package learning, scene/variant scoping, excess-root limits, matching/consumer cancellation, intentionally skipped media and oversized semantic-version numbers. A cache-rematching test denies reading the original archive while matching a newly learned signature.

The expanded native run passes 112 checks, including seed publication, a usable dispatcher during a deliberately blocked inspection, stale-result rejection, detected-package staging, pin/SDK-kind preservation and no implicit range-to-latest conversion. The shared dialog initially exposed an undeclared XAML namespace and a layout-update loop while attaching evidence tooltips; the template declaration and per-row evidence update guard were corrected. Native test-mode unhandled exceptions now also produce an artifact instead of silently losing the diagnostic.

Real read-only source-CLI measurements: AudioLink 13.9 MiB scanned in 79 ms, Pumi Quest 122.5 MiB in 375 ms, Lulla PC 611.4 MiB in 1,086 ms; whole process durations were 365/579/1,331 ms. These are single local runs, not latency guarantees. Warm-cache inspection reports FromCache=true. Text coverage remains partial for entries above the configured budget. Source and distribution evidence live in `.work/artifacts/2026-10-17-inspection-preview7/`; installer execution is excluded.

Final packaged implementation `5957763` passes all 112 native checks with SDK/runtime directories removed from PATH. The verified distribution is `release-verified`; earlier generated release folders were superseded. Final CLI scans measure 93/427/1,232 ms for AudioLink/Pumi Quest/Lulla PC, with full eligible-text coverage on those three samples after excluding intentional FBX/HDR skips. Whole-process times are 442/668/1,525 ms; cached Lulla takes 274 ms including CLI startup. These are local single runs while native verification was also active, not controlled benchmark guarantees. Explicit local-index fixture learning and deep local inspection also run successfully. EXE/ZIP SHA256 checksums match.

The installed preview6 ProductVersion, desktop assembly hash and user configuration hash remain unchanged. Preview7's installer is built but never invoked, including in an isolated installation. No mouse/keyboard injection or personal Unity project changes were used. First-delivery limits and deferred repository-download discovery are explicit in PACKAGE-INSPECTION.md.

## Preview 6 responsive and shared-dialog verification

The core suite passes all 81 tests. Native checks exercise creation at 520/760/1024/1180/1366 window widths, readable narrow fields, a single-line filename, and client-window bounds. Shared-dialog checks cover inline version staging, required-package retention, compact actions, narrow-window resizing and discard on dismissal. Favorite checks assert that toggling updates the same row objects without collection changes or reordering. Evidence: `.work/artifacts/2026-10-17-responsive/`.

The final self-contained payload (`522f968`) passes 106 native checks with the SDK/runtime search path excluded. An initial published test exposed delayed template realization in the shared dialog; selector wiring now runs against realized controls after layout. The final installer exits 0 with Success=true. Installed CLI sees 45 projects; the user configuration SHA256 is unchanged, the Start-menu shortcut targets the installed GUI and `vrcsetup-legacy` remains present. Installed startup diagnostics use copied settings and metadata; no desktop input or Unity project writes were performed. Final distribution: `release-final`; live evidence: `live-install`. Earlier installer lifecycle counts belong to their historical builds and were not rerun for preview 6.

## Preview 5 package flow

The shared-core .NET suite remains 81 tests. Expanded native verification covers rejecting and accepting navigation, local project-package staging through the shared library, returning without losing the batch, Revert and per-row removal undo, beta filtering, a single off-dispatcher load for repeated dropdown opens, retry after an empty lookup, and full client-window form bounds across three widths. Native diagnostics with copied real config/metadata reproduce the old clipped form and verify the fix without touching Unity projects.

The browser is a reusable WinUI UserControl; publishing must include `PackageBrowserView.xbf` along with main XAML/PRI resources. Historical preview 5 candidate packaging and native evidence stay in `.work/artifacts/2026-10-16-package-flow/`. Its installer was not executed during the 16 October review hold; preview4's earlier lifecycle evidence remains historical. Desktop input and personal-project mutations are outside these native checks.

Final candidate: 76 published native checks pass with development runtime/SDK search paths excluded. This includes multiword local search, a project library screen at three widths, off-dispatcher lookup coalescing and retry, navigation confirmation, draft preservation and Revert. Real-config diagnostics load 45 projects and confirm the form fits the entire client. Packaged implementation commit: `ef8205b`. The installed preview4 assembly hash and user configuration hash remain identical to the baseline; the preview5 installer was built but never run. Final EXE/ZIP/SHA256 are in `release-2.0.0-preview.5-final`.

## Preview 4 visual polish

The shared .NET suite remains 81 tests. The expanded native WinUI sweep has 54 checks: three window widths, form bounds, preset/library consistency, contextual save strip, footer navigation, row-menu data context and required-package protection, a compact version picker including exact prereleases, and preset rename preserving staged edits/template references. `ui-verified` contains the development render evidence. The source adds no Unity project mutation path; real personal projects are inspected only for startup metadata.

The library-card state mismatch and transient ComboBox null selection during rename were caught and fixed during native testing. Review also identified case-insensitive template references, creation customization preservation and normal-user access to restore; these are handled in the final implementation. Final packaged/native/lifecycle results are recorded with the distributables in `.work/artifacts/2026-10-16-polish/`.

The final self-contained preview 4 passes 55 native checks with SDK/runtime search paths excluded, including creation-draft preservation during rename. Its standalone installer passes all 17 isolated lifecycle checks. The live installation succeeds with the configuration hash unchanged and the independent legacy alias retained. Published ProductVersion identifies implementation commit `870d2ff`; desktop acceptance still belongs to Dom/testers.

## Preview 3 follow-up

- 81 .NET tests pass, including legacy terminal panel rendering with special characters, shared developer flags/logging, malformed-project isolation and completed-state serializer compatibility.
- 32 native WinUI checks pass in the published self-contained executable, with SDK/runtime search paths excluded. They cover preset selection/binding, named-preset staging without changing Default or project files, persisted favorite stars, local package addition, configuration reload, a single platform/name binding, offline-root recovery, the project/package workspace, six screens at two sizes, dark theme and the package dialog. No desktop input is injected.
- Native startup with copied real installed configuration/cache loads 45 projects with no error activity. The reported previous empty/error screen was not reproduced. A portable diagnose without existing portable settings correctly fails and writes its diagnostic log; the real-configuration check explicitly selects the installed home.
- The standalone preview 3 installer passes all 17 isolated lifecycle checks. This run preserves the live preview 2 process and settings. Original PowerShell `vrcsetup-legacy` remains independent and available.
- Independent review findings about configuration reload, staged edits lost on nested Back/version failures, recovery navigation and unavailable project roots were resolved. Human input, DPI/accessibility and clean tester-PC acceptance remain open.

New evidence and EXE/ZIP/checksums: `.work/artifacts/2026-10-16-usability/` in the canonical checkout. The existing Unity/VPM E2E evidence below was retained; those setup operations did not change in this usability revision and personal projects were not mutated.

## Previous migration evidence (preview 2)

The latest real Unity run passed all 17 checks. Installer coverage includes legacy configuration migration, fresh installation, upgrade, repair and self-uninstall. The final tester artifacts are in `release-2.0.0-preview.2`, with final native/lifecycle evidence in `ui-preview2` and `installer-preview2`.

- 76 .NET tests pass: rules, planners/executor, filesystem/storage, configuration revisions, CLI subprocess contracts and failure/recovery paths. `powershell -NoProfile -File tests/Run-Tests.ps1 -BuildDesktop` is the routine entrypoint.
- Native WinUI smoke verification runs the actual dispatcher, viewmodel commands and renderer: 18 checks, six screens at 1180×800 and 1366×768, dark theme and a package-editor dialog. It uses `--smoke-test <fresh-artifact-directory>`, never mouse/keyboard injection. The self-contained published app passes too, with development PATH entries removed and DOTNET_ROOT pointing to an absent runtime.
- Real E2E harness: two disposable Unity 2022.3.22f1 projects, one created through the CLI subprocess and one through the same shared application service used by the GUI. A UnityPackage contains a text marker and a MonoBehaviour; asset existence, compiled Assembly-CSharp.dll, embedded SDK, completed state, add/remove/bulk package operations and preset save are checked. Test VCC registrations are removed in finally. Run explicitly with `dotnet run --project tests/VrcSetup.E2E -- --unity <Unity.exe> --artifacts <fresh-folder>`.
- Installer verification exercises the actual standalone EXE: install, user-data separation, bundled CLI, upgrade/config preservation, previous-install backup, missing-alias repair, alias execution and running-CLI self-uninstall. Settings, a project marker and owned shortcuts are checked afterward. `tests/Run-InstallerTests.ps1` uses an isolated root/Start menu and suppresses user PATH changes.
- Desktop and installer Debug/Release builds produce zero warnings. Publishing explicitly includes native XBF/PRI resources; build-only validation had missed their omission in the first publish candidate.
- Independent review identified unsafe asset restore, insufficient recovery lock scope, exact-pin mismatch, linked-SDK selection loss, cancellation reporting, latest-policy loss and interrupted legacy migration. Each finding was fixed; regression tests cover the relevant core/storage behavior.

Local evidence is under the canonical checkout's `.work/artifacts/2026-10-14-winui-migration/`: `e2e`, `e2e-final`, `ui-preview2`, `installer-preview2`, `live-install`, and `release-2.0.0-preview.2`. Disposable Unity projects and superseded packaging candidates were removed after retaining checks/logs. Generated content and private local paths are not published in Git.

The live upgrade exposed a portable-extraction marker incorrectly selecting temporary installer data storage. Preview 2 uses an explicit installed-data factory, shares migration logic with CLI installation, and has regression coverage for the omitted-home case. Dom's original configuration was restored from the hash-verified previous installation before retesting the corrected upgrade.

## Parity map

All rows below are implemented. Evidence is deliberately separated: tests prove the specified invariants and representative flows; native render checks do not prove every human interaction. Source names refer to projects under `src/`.

| Plan | Implementation | Verification |
| --- | --- | --- |
| A01 | UnityProjectDriver.FindEditors/ValidateEditor; Desktop Settings and direct project picker; CLI setup | Build, native Settings, validation tests |
| A02 | ProjectWorkspace.ScanAsync, bounded exclusions and content fingerprints/cache | Scanner/cache filesystem tests, native library |
| A03 | ProjectSnapshot metadata source/status; embedded package and generated csproj fallback | Metadata/fallback tests |
| A04 | Desktop/TUI project filter/sort/direct path; persisted ProjectLibrarySort | Native filter checks, CLI schema tests |
| A05 | VrcSetupApplication.PlanCreationAsync and staged exclusive destination creation | Real Unity/CLI E2E, name/path tests |
| A06 | ProjectRules naming, remembered UnityPackage names, persisted recent names; Desktop/TUI settings | Reserved-name/path/remembered-name tests |
| A07 | Creation defaults/preset/working overrides through shared planner | Exact/overlay tests, native Create |
| A08 | CreationRequest separates Unity target and SDK publishing platforms; UnityBridge | Build, Windows real Unity E2E; mobile acceptance remains external |
| A09 | Whole-word platform suggestion, module preflight, existing-project target preservation | Suggestion tests and adapter inspection |
| A10 | UnityProjectDriver.PrepareAsync, NUnit manifest, package phase before imports | Real Unity creation/import E2E |
| A11 | UnityBridge session, compilation/domain-reload wait, evidence file, timeout and settle | Compiled probe E2E |
| A12 | Configured extras, explicit existing-project imports, reviewed file fingerprints | Planner/storage checks, shared adapter |
| A13 | Successful creation registers with vpm add project; failure becomes warning | Real VCC registration/cleanup E2E |
| A14 | PackageEditSession shared single/bulk staging; both clients review before apply | Session/planner tests, GUI stage check |
| A15 | Desktop/TUI search and multi-selection; friendly names and IDs | Build/native workspace; repository search via adapter |
| A16 | SemanticVersion ordering, stable/prerelease filtering, prefix and exact/latest choice | SemVer/policy tests, native version UI |
| A17 | Foundation protection, SDK intersection and linked version policies | Foundation/SDK planner and imported-plan tests |
| A18 | Stable VPM adapter and exact prerelease vrc-get action selection | Driver build/inspection, version rules tests; prerelease live install not claimed |
| A19 | Direct and locked dependencies read separately, string/object manifests | Storage/schema tests, real metadata E2E |
| A20 | Installed exact versions become Keep; latest resolves concretely | Planner pin/latest/mismatch tests |
| A21 | Separate defaults/named presets, working-copy native dialog, save/delete/discard; TUI and CLI | Config/preset tests, dialog render and real preset save |
| A22 | Overlay and Match exactly implemented in shared planner/session | Overlay/exact/required-removal tests |
| A23 | Configured favorites and quick-add from known registry | Storage/session tests, both clients build |
| A24 | PackageCatalog known registry, cancellable refresh, preserved choices | Catalog/session storage checks, native Presets |
| A25 | Project profile keeps policy/installed/preset definition and manifest policy; external changes flagged | Profile roundtrip/latest/fingerprint tests |
| A26 | Mixed bulk rows and per-package project target selection | Bulk session/planner tests, real bulk E2E |
| A27 | Copy installed set or saved policy, overlay/exact preset and undo intents | Shared-session tests; Desktop/TUI bindings |
| A28 | Project lock/busy/stale review checks, isolated outcomes and cancellation | Executor failure/stale/cancellation tests |
| A29 | OperationLog JSON report, NotProcessed and per-project logs | CLI/report tests, real E2E reports |
| A30 | Manifest plus existing-project asset backup; staged restore with displaced-tree preservation and resolve | Recovery failure/cancel/filesystem tests |
| A31 | Conditional Vulpine Vault repository, recognized legacy Easy Login moved with meta to backup | Adapter inspection/build; no personal legacy avatar mutated |
| A32 | Known absent-legacy warning interpretation plus on-disk postcondition | Tool-result tests and adapter verification |
| A33 | SetupState, incomplete discovery, explicit retry/import/delete and confirmed blocking-Unity close | State/cache tests; recovery bindings; no personal project cleanup invoked |
| A34 | TUI Q/Esc, bounded scroll/follow viewport; GUI cancellation/activity/full log; owned child Job Object | Cancellation tests, real runner E2E, native Activity |
| A35 | CLI/TUI commands and PowerShell/kebab flags, JSON, dry-run, config/recipes/recovery/lifecycle | CLI subprocess tests under PS5.1 and PS7 |
| A36 | Shared normalization, argument arrays, quoted/special/relative paths | Path tests and installer path with brackets, &, and accent |
| A37 | Portable marker, self-contained payload, installer, alias/shortcuts, repair/uninstall and config migration | Actual standalone lifecycle test; app is replaced transactionally instead of overwriting locked DLLs |
| A38 | Same VPM mutex name, bounded retry, safe VCC repository cache read and vrc-get fallback | Real VPM E2E, runner/cache tests |
| B01 | Named creation recipes: package policy, preset, target/platforms, extras, naming fields | Config contracts and both client builds |
| B02 | Async MVVM WinUI client, system/light/dark theme, native controls, file drop, activity logs | Native render checks at two sizes and scale 1; interactive keyboard/screen reader/multiple DPI acceptance not claimed |
| B03 | Shared project lease, stale fingerprint, atomic config revisions, lifecycle lease | Lock/revision/stale/restore tests |

## Distribution boundaries

The app includes .NET, Windows App SDK runtimes and the native VPM package client; a separately installed VPM CLI/runtime is not required for package operations. Unity is required for creation/import; optional VCC registration remains separate. Android/iOS build modules and signing/upload flows are not bundled. Recipes reproduce setup choices and package sets; they do not clone private avatar assets or entire VCC template projects. The original PowerShell engine is retired on this branch, retained in Git history and stable main. No main merge is performed.

The tester's acceptance pass should cover mouse/keyboard navigation, file pickers/drop, actual monitor DPI/accessibility, and platform modules they intend to use. Dom explicitly requested continuing without desktop input, so these checks are reported as unperformed.

## Beginner usability verification — 19 October 2026

219 core fixture tests pass, including portable preset round trips, preserved latest/pinned choices, duplicate and reserved names, invalid choices, unsupported/oversized files and nontechnical error messages. Native WinUI verification passes 355 checks across narrow/wide windows and dark/light themes. Added checks cover settings Save/Discard/Cancel on leaving/closing, invalid settings without partial persistence, preset sharing and corrupt imports without replacing saved presets, filtered included counts, working share-menu commands and the minimal first-run state. Screenshots include the fixed settings action area, package surfaces and Activity outcomes.

Evidence: `.work/artifacts/2026-10-19-user-friendly/verified/`. These are real WinUI dispatcher/rendering checks with isolated data and no input injection, not a screen-reader study or end-to-end import of an owner's avatar. No owner installation or Unity project was changed. Installer/update compatibility is also exercised by release CI; email delivery remains under the user's mail client and is not sent by tests.

## Unified setup and import isolation (19 October 2026)

Local verification: 220 core tests, 364 native WinUI checks and 43 checks against the published headless installation engine passed. WinUI checks cover import-local package editing without saved-preset changes, maintenance refusal during active imports, preserved file selections and expanded-log scrolling. Actual renderer captures include compact project rows, bottom-aligned jobs and expanded logs at narrow and wide sizes.

The Native AOT public bootstrapper was built and its start/download/install/completion controls captured offscreen. File/byte progress tests cover cold versus cached delivery; the measured bar switches between determinate and indeterminate styles. Engine tests exercise manual Finish choices, declined reinstall/downgrade and automatic completion without an engine window. These tests use isolated folders and shell overrides; no owner installation or real Unity project is changed.

The real beta.14 app updated to beta.22 and reopened in 14.8 seconds with settings preserved, using the public Native AOT EXE and the verified component cache. This is local component-delivery evidence; CI additionally checks the newest preceding published release.
## Compact setup, project feedback and contact (19 October 2026)

222 core tests and 369 native UI checks pass. Executor tests now allow ordinary package-tool changes with Unity open while preserving the exclusive Unity requirement for extra file imports. Native checks cover pointer focus after deselection, cleared selection feedback, error-banner log actions and the contact dialog's two explicit copy choices. Contact tests do not change the owner's clipboard or send messages.

The installer keeps a fixed 560 × 260 logical-pixel window across choices/progress/completion, with a 14px phase label directly above the 24px bar. Actual native captures confirm all controls fit. Project surfaces animate hover/selection for 180ms, use the real IsSelected state and reserve the checkbox slot to prevent text shifting. Dark/light themes and system motion preferences remain supported.

Evidence: `.work/artifacts/2026-10-19-compact-setup-project-feedback/`. WinUI captures show distinct selected/hover/neutral background pixels after deselection. The owner's Apply diagnostics were read only; they recorded the previous Unity-open preflight refusal. No real project or installed app was modified. These changes are source commits for the next build, not a published release.
## Fresh Windows profile and support logs (19 October 2026)

230 core tests pass, including cold selected-package source preparation, native package-client argument routing, single-desktop ownership/reopening, bounded app/update ZIP export, minimal fresh defaults with preserved existing choices, project-package indexing and selective transient-download retry. 352 native UI checks pass with explicit legacy user configuration, including Save/Cancel navigation and saved preset versions. The changing number of package-row checks reflects the reduced initial library.

The public Windows job additionally resolves every fresh-default package using the published native client on a disposable Windows profile. That real source check is CI-only because vrc-get shares VCC's profile; no owner repositories, Unity projects or clipboard were mutated by fixture tests. Source logs supplied by the owner showed import-queue sharing violations and a selected package's unavailable metadata; the updater error was reported to succeed on retry, without its exact cause in those logs.

Evidence: `.work/artifacts/2026-10-19-fresh-machine-fixes/`. New defaults include only Resolver/Base/Avatars, Poiyomi and VRCFury; existing preferences and extra packages are retained. Error files have a readable product/date name. Shared ZIPs contain app/update diagnostics, exclude settings/assets, and open selected in Explorer.

## Additional PC compatibility checks (19 October 2026)

234 core tests pass. The public beta.24 EXE passed 19 isolated installation lifecycle checks on a non-elevated Windows 11 x64 token, starting with no cached components: 13 components / 182,384,465 bytes downloaded. Install, same-version reinstall, repair, bundled CLI, isolated shortcuts and uninstall passed; settings and the synthetic project remained intact. Public updater verification also passed discovery from beta.23, fresh/cache behavior, version/channel filtering, background preparation metadata and installer checksum. No installer is executed by the public updater check itself.

Run the opt-in network check with `dotnet run --project tests/VrcSetup.E2E/VrcSetup.E2E.csproj -c Release --property:Platform=x64 -- --verify-public-update <previous-version> <expected-version> <new-evidence-directory>`. It uses public release assets, no credentials and no real app settings. It intentionally stays outside normal unit tests and release CI to avoid repeated public downloads.

Evidence: `.work/artifacts/2026-10-19-pc-compatibility/`. See [VM acceptance](VM-ACCEPTANCE.md) for the remaining clean-machine Unity/user checks and explicit platform limits. No new application build is needed: only tests and verification documentation changed.

## Mixed VPM packages and first Unity compilation (23 October 2026)

`--verify-package-mix <editor> <new-evidence-directory>` is an opt-in E2E mode using an isolated project and the installed VPM client. It resolves and installs SDK Base/Avatars, Resolver, Poiyomi, VRCFury, Modular Avatar, AudioLink and Gesture Manager, verifies installed versions and setup completion, then explicitly launches Unity batch mode to compile a probe. The test does not add VPM repositories or change owner projects. Normal empty-project creation still does not launch Unity.

The first real run exposed missing built-in Unity module dependencies in the extracted Hub template. New-project preparation now adds the modules supplied with the selected editor, preserving existing manifest entries; recovery of an empty-project setup also performs this preparation. The repeated run passed with Unity 2022.3.22f1, all eight requested packages plus dependencies, exit code 0 and the compiled probe marker. Evidence: `.work/artifacts/2026-10-23-creation-flow/package-mix-fixed/result.json`. This validates that combination, not every possible third-party package or PC.

Native UI verification additionally covers custom-preset one-time creation without a save/review prompt, unchanged saved configuration, existing-project handoff without recreation or automatic apply, disposable navigation and the concurrent-operations flyout.

## Empty project creation and distinct import flow (20 October 2026)

The supplied Unity log ended in `ArgumentException: Empty import plan` inside `VrcSetupPostImport.RunImport`; Unity's script compiler had succeeded. The empty-project path must not launch Unity at all. It now creates source files from the selected editor's installed 3D template (or a minimal source scaffold when unavailable), installs/verifies packages through native VPM and marks setup complete. Empty-project recovery preserves assets and does not request a source archive. Completion reconciliation also checks the recorded project identity.

247 core tests pass, covering empty creation with zero Unity import/settle calls, recovery ownership, UnityPackage container checks and constrained template extraction. Native UI checks cover the dedicated New project package editor, local customizations without changing saved presets/import drafts, guarded navigation, import-without-file guidance, folder rejection and the single settings Save and restart action for language. The previous new-project dialog and separate language restart button are removed.

The opt-in `--verify-unity-import <editor> <new-evidence-directory>` E2E mode generated the project source in 0.19 seconds using the installed template, verified no Unity logs for empty creation/recovery, then explicitly imported a synthetic UnityPackage and verified the asset and recompiled assembly. The defensive bridge also accepts a valid empty queue while rejecting a missing package list. Evidence: `.work/artifacts/2026-10-20-empty-import/`. These timings exclude VPM downloads and the user's first interactive Unity compilation.

Release CI now extends the published-native-client check to actual installation of all fresh-default packages, postcondition/version checks and a completed setup marker. A deliberately non-executable editor fixture ensures that this creation cannot depend on launching Unity. This runs only on the disposable hosted runner because the native VPM client shares VCC's profile.

## Import pause and closing candidate (6 November 2026, not executed)

The 0.3 refactor candidate persists pause intent before requesting worker cancellation, retains paused imports across restart without automatic resume, and prevents clearing a paused row. Closing offers Pause and close, reserves new actions, waits for actual import stop, resolves unsaved choices and then releases the queue. Each stop/shutdown wait is limited to 20 seconds; a delayed worker leaves the window open and the queue lease retained. A canceled wait is not a successful stop. Normal close no longer requires a second click after a cooperative stop. This candidate does not yet implement definitive cancellation and owned-output cleanup.

Source regressions cover durable pause intent, queued imports not executed, journal-write failure without cancellation, completion winning over a pause request, restart, and a canceled shutdown wait retaining the lease. A controlled Windows runner test covers redirected output inherited by a child after the root exits. Native check source covers closing action guards, visible application status and canceling the unsaved-settings prompt. These tests and the candidate build have NOT been run while the owner uses the PC.

Deferred acceptance: normal/slow/failed stop, repeated close clicks, save/discard/cancel for settings/presets/workspace, apply selected from the close prompt, an action already queued at close time, startup after pause (including no automatic missing-package retry at startup or project opening), new action after a timed-out close, app update/language-restart races, and lock release only after the worker's final persistence. Use isolated fixtures. The real Trixie Reimporting assets/Cancelling incident remains undiagnosed; do not infer its cause from the runner patch or use the owner's open project as a fixture.

## Project import recovery candidate (6 November 2026, not executed)

The project attention action can offer the current unfinished import's recovery action directly, showing the source archive filename with its recorded path available in the tooltip. It reuses the same reviewed recovery workflow as Imports/Activity. Missing archives still use the manual picker; original source ownership stays separate from the replacement file path. No archive is copied automatically. Partial/missing projects still follow the existing separate-creation path until owned partial-output recovery is implemented.

Project selection and queue admission use append order in the durable journal, with active work taking priority. A hidden later completion does not revive an older paused record, and clock changes do not reverse operation order. Recovery rechecks the current import after dialogs/planning and passes its expected ID into EnqueueAsync, which validates it under the queue's journal gate before adding work. Core/native test sources cover these selection rules, stale requests and current-project recovery; they have NOT been run. Deferred acceptance includes changing the import during source selection/review, active same-target work, newer completion, repeated recovery, source relocation and clipboard/path accessibility. Complete archive identity validation, partial-project visibility, owned-output resume and definitive cancellation remain open.

## Quiet verification and partial initialization (6 November 2026, later checkpoint)

Activity/error UX follow-up: the normal history combines import and operation entries in one chronological list; mutation/recovery controls are removed from that history surface. Operational recovery remains in Projects/import queue, and the package-manager warning no longer routes to Activity. Queue details now separate a localized state/action summary from the complete technical log, including its newest entry. Italian uses “Riprendi importazione” consistently for the resume action. Native smoke `.work/artifacts/ui-history-retry/ui-smoke.json` passed for the unified history/message candidate; subsequent changes only restore the test ItemsSource to History and replace the warning's Activity link with Projects, followed by a fresh build. Full user-error matrix, help/FAQ, Italian copy audit and import expansion animation remain open.

Subsequent native checkpoint: after the owner lifted the GUI/Unity test restriction (Computer Use remains excluded), the Debug x64 native smoke completed with exit code 0 in `.work/artifacts/ui-current-final`. Build had zero warnings/errors. The first runs exposed missing closing guards in log export/navigation and an outdated content-relative layout assertion; these were corrected, then the full native verifier passed. The rendered 720px expanded-import frame was inspected: its action stays below the scrolling viewport and within the page. This is native renderer/fixture evidence, not proof of the Trixie incident's root cause or all localization/error paths. Preexisting recovery now takes a backup before mutation, with 28 planner/recovery tests passing; restore remains prohibited when the editor was or is open.

After the owner authorized hidden verification, the backend and x64 desktop compiled with zero warnings/errors. The final focused batch passed 112 tests covering proposals, inspection, preferences, queue/discard receipts, the runner's inherited-pipe cancellation and partial planning/initialization. Evidence: `.work/artifacts/quiet-verification/focused-combined.trx`, `desktop-build.log` and `results.md`. This supersedes the earlier unexecuted status only for these checks; no WinUI application, UI smoke mode, real Unity editor or personal project was launched.

Attributed partial projects now plan an explicit initialization phase from a read-only partial snapshot. Execution verifies the physical receipt and reviewed file fingerprints under the project lock, adds missing manifest/version/Assets scaffold and preserves existing files. A metadata-only test assembly copied as `Unity.exe` validates the positive path without executing an editor. Tests cover absent Packages, JSON roundtrip, receipt/file mismatch, policy preservation and initialization under the executor lock. Existing installed/locked package versions are retained during partial planning. Legacy partial projects without a creation receipt keep the conservative separate-creation route.

The checks exposed and corrected metadata-only handles that failed to pin a directory against rename, a runner fixture that did not inherit its pipe, partial reads that enumerated absent Packages, and fingerprint/request serialization incompatibilities. A bounded independent static review confirmed its missing-Packages finding was fixed; it is not runtime acceptance. Real Unity compilation/import after partial recovery, native UI layout and appearance crash reproduction, complete archive identity/corpus detection, preexisting-project rollback and the full regression suite remain required.
# Quick guide (0.3 candidate)

Clearing finished imports retains paused, interrupted, cancelled and failed
imports while they can be resumed. A previously hidden recoverable record is
made visible after restart. Pending Creator Companion notices are not hidden
automatically; the user may hide a completed row while the project warning
remains available. Journal-write failure restores the previous visibility.
Queue tests cover these cases, including a failed project creation with no folder;
native smoke checks the visible rows, translated states and current retry.

Import detail expansion animates the measured content height so the whole row
grows with it. Native smoke verification samples intermediate row heights,
checks rapid collapse/reopen and simulates disabled app animation to check that
content remains unclipped. A compact screenshot was inspected. These checks do
not replace keyboard or screen-reader acceptance, and do not change OS settings.

The native verifier exercises the project backup action through the view model
with simulated confirmation and an isolated filesystem fixture. Declining leaves
the changed file intact; an exclusive Unity lock produces a clear error without
restoring; confirming after release restores the saved file and displays success.
The action refreshes the Projects list after successful completion. This does not
simulate a mouse click on the project menu or prove every concurrent writer race.

For new creations the project state retains the original creation request,
archive identity and selected package policy, separately from the import queue.
If its journal is missing, the project warning can propose a new reviewed plan
only after validating the creation receipt. A moved archive must match its saved
identity; the queue also rejects a newly appeared history entry under its journal
lock. Legacy records without these inputs are not treated as verified ownership.
Focused fixture tests cover retained inputs, receipt mismatch, missing identity
and a concurrent history entry. The complete suite passed 415 tests and the
offscreen UI smoke passed 535 checks, but the orphan recovery action itself has
not yet been exercised end-to-end through WinUI and real Unity. The subsequent
native verifier exercises missing and wrong archives, cancellation, and a matching
relocated archive through the review decision without enqueuing after rejection.
An isolated queue test separately verifies enqueue, completion and journal reload.
These complementary checks do not yet prove a single uninterrupted UI-to-Unity run.

Creator Companion registration is a separate optional step after project completion.
Its failure is a warning, including exceptions and tool error output; it must not
restore or repeat the successful import. A pending notice survives restart and
offers registration-only retry from the import details or project warning.
The full core suite passed (407 tests), and the native UI smoke passed (530 checks)
before the final duplicate-click guard. The subsequent desktop build passed with
zero warnings/errors. Tests cover false/exception registration, retry persistence,
failed journal writes and superseded imports. Real VCC registration is not covered
by these isolated checks.

The native offscreen verifier opens and closes the guide in English and Italian,
expands all seven topics, checks translated content, and renders both dialogs.
Evidence: `.work/artifacts/ui-guide/` (smoke exit 0). The guide is available from
Information; it is optional and can be reopened. This does not verify a first-run
tutorial or keyboard/screen-reader acceptance.

An interrupted empty-project creation can be explicitly cancelled even when its
destination folder was never created. The durable outcome distinguishes this
case from removal of an owned folder; an unavailable parent or a folder observed
before finalization prevents the no-folder completion. A pending cancellation
remains available in the operations footer. The complete core suite passed 420
tests and the offscreen native verifier passed 562 checks. These checks use
isolated fixtures and do not establish atomic exclusion of external processes
creating a folder after the final absence check.

The optional native `--smoke-unity-recovery` check resumes an orphan project
using a relocated synthetic UnityPackage, approves its reviewed plan through the
test interaction, waits for the real queue and Unity editor, and verifies the
imported asset and completed project state. Evidence: `.work/artifacts/ui-unity-recovery-01/`;
565 native checks passed with Unity 2022.3.22f1. This uses an isolated home and
project, with Creator Companion registration disabled. It does not exercise a
physical click, a real avatar archive or actual VCC registration.

For legacy queue entries without a recorded source digest, the user must still
confirm the archive because its original identity cannot be reconstructed. The
confirmed file is now fingerprinted before planning so changes after that point
are rejected by the existing verified-source execution path. The native smoke
exercises confirmation refusal and the resulting reviewed identity (565 checks),
and five focused source-identity/recovery tests passed. This does not recover an
older project whose journal and trustworthy creation inputs are both missing.

Stale project-recovery and Creator Companion retry actions now give visible,
localized feedback instead of changing an undisplayed status. The native UI
verifier passed 575 checks, including both stale-action scenarios; focused
error/localization tests passed 15 cases. A project already open in Unity also
reports that state visibly. These fixtures do not exercise a real VCC registry.

With incomplete archive or eligible-text coverage, a typed GUID match remains a
possible package rather than strong add advice. Native package guidance omits
internal asset paths and observed signature versions, which remain in the
inspection report. The full Core suite passed 422 tests; native UI smoke passed
573 checks, including concise Italian guidance. Trixie and Lulla PC/Quest cached
observations rematched successfully with complete reported coverage; this does
not establish an independently validated dependency ground truth.

The import screen offers evidence-backed possible packages separately from
strong matches. The native verifier passed 582 checks including explicit
addition, unchanged saved preset, bulk exclusion and an updated status message.
The complete core suite passed 427 tests. Cached Trixie/Lulla observations were
rematched: possible matches remain suggestions rather than proven requirements;
those observations do not establish ground truth or compatibility.
The native verifier subsequently passed 585 checks, including switching between
two import drafts: an explicitly chosen suggestion remains with its own draft
and is not selected in the other. This covers staging, not two Unity imports.

For an uninitialized project whose recovery inputs are unavailable, the project
warning offers a new import in a separately named project after manual package
selection. It explains that edits in the old folder are not copied. An active or
recoverable import supersedes this fallback, and name collisions are avoided.
The native smoke passed 570 checks, including project scanning, a stale action,
cancelled selection, and preservation of a fixture file. The picker and decisions
were simulated; this check stops before starting the new import in Unity.

Unity asset import errors are treated conservatively as an incomplete operation,
even when the editor returns exit code zero and writes the bridge completion
marker. A timeout retains its diagnostic exception; when the log also contains
asset import failures, the user receives a concise recovery action. Long paths
are presented as a possible cause only when the corresponding file-not-found
message contains a path of at least 260 characters. The validator does not prove
whether a later Unity refresh might have recovered an earlier failure. While the
bridge has finished but the editor is still running, the queue indicates that it
is waiting for Unity to close rather than claiming completion.

Appearance persistence has a separate two-process WinUI verification: the
first process changes colors through the Settings pickers and saves the isolated
configuration; a new process verifies the loaded values, startup brush and
picker state. Both windows run offscreen. This verifies restart persistence but
does not reproduce or explain the previously reported appearance crash, and it
does not test an actual Windows high-contrast transition.

An isolated journal-write failure fixture completes the operation, makes the
journal path unwritable and checks that the in-memory outcome remains visible.
Retry writes the existing outcome without running the import again; a new queue
instance can then read the completed state. Native UI verification covers the
save-only action and declining to close with an unsaved result. The warning is
in memory: a crash or an explicit decision to close without saving can still
leave only the earlier journal and project marker, which may not preserve an
optional Creator Companion registration outcome.

