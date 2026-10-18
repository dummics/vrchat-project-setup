# Multiple imports

## Preview 12: state-aware imports and recovery

Destination checks now distinguish new, active, completed, recoverable, partial, unrelated and invalid-state folders using the existing setup JSON, source identity and project/process locks. A same-source initialized project can resume or import missing assets; it is never deleted or recreated in place. Completed projects from another source and unmanaged folders remain protected. Partially initialized projects offer a new copy while retaining the partial folder. On reopening, one contextual prompt offers Resume/New copy/Later for the most recent eligible interrupted import. Completed markers reconcile older history as Recovered.

Recovery preserves already installed versions and retains unrelated packages; version changes remain available in project management. Verified package preparation is skipped when nothing needs changing. Existing asset paths and GUIDs (including moved assets) are preserved; only absent assets enter a temporary filtered UnityPackage. GUID conflicts stop recovery rather than overwrite files. Missing assets are imported and Unity settles/validates before completion. This is asset-level preservation, not a merge of individual prefab/material properties. A full backup remains available before project mutation.

Unity logs are read with writer-compatible sharing and bounded retry, including final validation. Temporary log contention does not interrupt the running Unity process. Elapsed time is recorded per job and shown beside its name. Steps use plain labels, and RAM/CPU remain in the header without queue counts. Resource warnings reserve one line; import errors reserve two lines adjacent to the action. The shared console updates automatically when expanded; only Resume/Cancel remain per job, with Open logs in the page menu. Templates were removed from the creation UI; optional manual file paths are controlled in Settings. CLI recipes remain available.

The terminal import now begins with its boxed file-input context, validates dropped/quoted paths before proceeding, supports blank input to return and shows separate status/timer and console panels during execution. It continues to use the shared planner/executor.

Preview 11: Review and import immediately shows preparation activity, with failures next to the action and a Settings link when appropriate. Planning snapshots the editor/projects-folder fields currently shown in Settings. If the editor is unavailable, a single discovered Unity Hub installation can be used; multiple installations require an explicit choice. Nothing is persisted by this fallback. The creation confirmation is a short per-project recap of name, platform, preset and package count; one Import action starts the reviewed jobs. Existing-project mutation reviews retain their detailed diffs.

The shared package-selection dialog keeps its opening order and existing row/container identities throughout toggles and version changes. Search can change visibility and append new results, but selecting a package does not move it. Reopening creates a fresh order from the accepted selection; Cancel still discards changes.

Import projects is one page at every window width. Add `.unitypackage` files through the picker or drag and drop, including while other imports run. Each file creates its own Unity project. Select a staged file to change its inferred name, platform or package selection before reviewing the batch. The selected saved preset is copied into each plan. Conflicting/existing destinations are rejected rather than overwritten. New or edited drafts are preserved if input changes while planning.

Review the combined plan before starting. Active imports appear above the input form. Each compact row shows its name, elapsed time, current step and coarse progress bar; expand it for messages, a bounded console tail and contextual Cancel/Resume. Reviewed job inputs remain fixed after starting. This is phase progress, not a measured percentage of imported bytes. The small Clear completed button hides successful rows without removing their journal, plan or logs. Settings optionally hides successful rows automatically (off by default). Activity retains their history. Row instances and expanded state survive progress updates.

Preset and Customize are compact controls. Below them a flat package preview merges the chosen policy with detection evidence, without duplicate entries. Missing Declared/Detected dependencies offer Add only for an exact declared version or an explicit latest suggestion; ranges use Review. Included and Possible packages are not promoted into missing-dependency recommendations. Adding changes only the selected import draft, never saved presets/defaults or the personal registry. Existing pins remain unchanged. Rows keep their position when added. Review is at the right on wide forms and below the preview on narrow forms.

Activity has Imports and Operations tabs backed by the import journal and operation reports. New reports retain the reviewed plan for package/change details; old reports remain readable with fewer details. The latest 300 operation-report files are loaded; older files remain on disk. Expand an item for its outcome, paths, packages/changes and own log. Session diagnostics are developer-only. Open logs and Open project folders remain in the import menu; analysis-cache clearing lives in Settings.

The GUI has no package-count or worker-count selector and no artificial two/six-job cap. Reviewed jobs can run concurrently; required preparation/VPM locks still serialize their own critical sections. RAM/CPU are sampled every three seconds. Sustained high usage produces an inline text warning while imports are active, without a popup, automatic cancellation or GUI resource throttling. This is advisory, not a guarantee against resource exhaustion. The optional CLI scheduling limit remains available separately.

The queue wraps the same shared planner/executor, project locks, fingerprints, backup, Unity import and package verification as CLI/TUI. Background version or recognition lookups do not alter a reviewed import. Batch operations intentionally do not overwrite recent-name preferences while the user may be editing Settings. Console tails read at most 64 KiB from the log asynchronously, only for expanded rows.

## Cancellation and recovery

Cancel removes no project data. Waiting jobs do not start; running jobs use the existing cancellation and recovery path, including subprocess-tree termination and rollback where supported. Closing the app while imports are active asks to cancel them and waits for the owner to close again after they stop. Final journal writes finish before a normal window close.

`AppPaths.Home/imports.json` records reviewed plans and status separately from cache. One queue owns that home at a time, through `imports.lock`; project locks still protect the actual targets. Restart converts previously active records into Interrupted and never resumes them automatically. The CLI can list history without taking queue ownership. A second app/process cannot start imports in that same home while another owns the queue.

Recovery is contextual to each saved import in Activity, rather than a global unfinished-project/restore picker. Check project reads the state/finished steps and actual installed VPM package metadata asynchronously; it does not launch Unity, inspect every asset, or certify script/compile health. Resume import / Reimport missing builds a fresh reviewed plan against the original target. Matching completed projects can also receive missing assets; unrelated folders are refused. Existing assets and installed versions are preserved, with backup and fresh project/input fingerprint checks. Missing source archives can be chosen again with explicit confirmation; original state ownership is retained in the reviewed recovery plan so retries remain source-bound. Partial initial creation offers a new named copy and preserves the partial directory. Restore backup appears only when that operation has an associated backup record and requires confirmation. Recovery is not a Unity transaction or an undo of arbitrary creator script effects.

Clear analysis cache removes only generated `cache/inspection/archive-*.json`, under the same I/O gate as inspection. Signatures, personal packages, presets, import journal, logs, backups and Unity Library directories are preserved. It does not cancel an import or remove Unity caches.

## CLI

`--parallel` accepts any positive worker count or `auto` for a resource-based recommendation. The CLI defaults to one worker. Changing capacity never interrupts a running job; only new starts observe the capacity.

```powershell
vrcsetup imports run 'C:\Downloads\Avatar PC.unitypackage' 'C:\Downloads\Avatar Quest.unitypackage' --dry-run --json
vrcsetup imports run 'C:\Downloads\Avatar PC.unitypackage' 'C:\Downloads\Avatar Quest.unitypackage' --parallel 2 --yes
vrcsetup imports list --json
vrcsetup imports recover <job-id> --yes
vrcsetup imports apply-plan reviewed-creations.json --parallel 2 --yes --json
```

`--plan` exports the generated plan. `apply-plan` accepts creation plans only; export per-project `create --dry-run --plan` plans and combine their Projects arrays when different files need different reviewed package overrides. It does not bypass executor preflight. Ctrl+C cancels jobs owned by that CLI process and waits for cancellation/journal completion. The existing terminal workflow and single-project commands remain available.

## Projects and package layout

Projects shows name/path, relative last modification and Open project plus the package-management icon. Kind and Unity version remain available in the model/CLI but are removed from the list. The date uses existing project metadata timestamps, not a recursive asset scan; its tooltip shows the local timestamp. Narrow rows stack actions/date below the name. Open project launches the configured Unity editor after availability/busy checks; management remains separate and supports multiple selections.

The project package sheet uses flat rows, package icons and inline installed-version dropdowns. Open project is in its header; Apply/Revert remain contextual batch actions. Creator Companion screenshots guide hierarchy and spacing; no unimplemented repository filter or speculative latest-version data is shown.

Open project is available only for single-project management, with a command-level guard as well as UI visibility. Pending drafts survive confirmed navigation and reopening the same target set; switching to another set asks before discarding. Revert explicitly resets the draft. Review groups identical transitions for the same affected project set, retaining distinct old/new version combinations. No-op latest selections save policy without reinstalling an already-installed version; missing embedded packages remain repair operations.

Package Apply uses a dedicated dimmed blocking overlay, separate from the nonblocking import workflow. It shows preparation/current project/phase, disables navigation and blocks normal app close until the awaited worker and recovery finish. Cancel requests cooperative cancellation, without claiming to undo previously completed projects; the executor restores the affected project's backup where supported. Busy editors are named before review; fresh executor preflight still protects projects opened later. Skips/failures are shown explicitly and retain pending choices. Phase progress is indeterminate rather than an invented byte percentage.
