# Desktop migration verification

Build: 2.0.0-preview.7 candidate, Windows x64. Verified on Dom's Windows machine on 14–17 October 2026. **Preview 6 remains installed; do not install the new candidate.** No clean Windows VM or human interactive acceptance is claimed.

## Preview 7 offline inspection

The .NET suite passes 96 tests. New generated-archive cases cover member ordering, no extraction, internal references, sample downgrading, duplicate GUID owners, manifests/ranges, budget exhaustion, invalid gzip, changed files, corrupt caches, included-package learning, scene/variant scoping, consumer cancellation and oversized semantic-version numbers. A cache-rematching test denies reading the original archive while matching a newly learned signature.

The expanded native run passes 112 checks, including seed publication, a usable dispatcher during a deliberately blocked inspection, stale-result rejection, detected-package staging, pin/SDK-kind preservation and no implicit range-to-latest conversion. The shared dialog initially exposed an undeclared XAML namespace and a layout-update loop while attaching evidence tooltips; the template declaration and per-row evidence update guard were corrected. Native test-mode unhandled exceptions now also produce an artifact instead of silently losing the diagnostic.

Real read-only source-CLI measurements: AudioLink 13.9 MiB scanned in 79 ms, Pumi Quest 122.5 MiB in 375 ms, Lulla PC 611.4 MiB in 1,086 ms; whole process durations were 365/579/1,331 ms. These are single local runs, not latency guarantees. Warm-cache inspection reports FromCache=true. Text coverage remains partial for entries above the configured budget. Source and distribution evidence live in `.work/artifacts/2026-10-17-inspection-preview7/`; installer execution is excluded.

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

The app includes .NET and Windows App SDK runtimes; Unity/VCC/VPM and VPM's own runtime are external prerequisites. Android/iOS build modules and signing/upload flows are not bundled. Recipes reproduce setup choices and package sets; they do not clone private avatar assets or entire VCC template projects. The original PowerShell engine is retired on this branch, retained in Git history and stable main. No main merge is performed.

The tester's acceptance pass should cover mouse/keyboard navigation, file pickers/drop, actual monitor DPI/accessibility, and platform modules they intend to use. Dom explicitly requested continuing without desktop input, so these checks are reported as unperformed.
