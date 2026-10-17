# Multiple imports

Preview 11: Review and import immediately shows preparation activity, with failures next to the action and a Settings link when appropriate. Planning snapshots the editor/projects-folder fields currently shown in Settings. If the editor is unavailable, a single discovered Unity Hub installation can be used; multiple installations require an explicit choice. Nothing is persisted by this fallback. The creation confirmation is a short per-project recap of name, platform, preset and package count; one Import action starts the reviewed jobs. Existing-project mutation reviews retain their detailed diffs.

The shared package-selection dialog keeps its opening order and existing row/container identities throughout toggles and version changes. Search can change visibility and append new results, but selecting a package does not move it. Reopening creates a fresh order from the accepted selection; Cancel still discards changes.

Import projects is one page at every window width. Add `.unitypackage` files through the picker or drag and drop, including while other imports run. Each file creates its own Unity project. Select a staged file to change its inferred name, platform or package selection before reviewing the batch. The selected saved preset is copied into each plan. Conflicting/existing destinations are rejected rather than overwritten. New or edited drafts are preserved if input changes while planning.

Review the combined plan before starting. Active imports appear above the input form, ahead of finished history. Each compact row shows its name, current step and coarse progress bar; expand it for messages, a bounded live console tail, the full log and contextual Cancel/Recover. Reviewed job inputs remain fixed after starting. This is phase progress, not a measured percentage of imported bytes. Completed items remain until Clear completed history. Row instances and expanded state survive progress updates; transitions are immediate and repetitive progress is throttled.

The GUI has no package-count or worker-count selector and no artificial two/six-job cap. Reviewed jobs can run concurrently; required preparation/VPM locks still serialize their own critical sections. RAM/CPU are sampled every three seconds. Sustained high usage produces an inline text warning while imports are active, without a popup, automatic cancellation or GUI resource throttling. This is advisory, not a guarantee against resource exhaustion. The optional CLI scheduling limit remains available separately.

The queue wraps the same shared planner/executor, project locks, fingerprints, backup, Unity import and package verification as CLI/TUI. Background version or recognition lookups do not alter a reviewed import. Batch operations intentionally do not overwrite recent-name preferences while the user may be editing Settings. Console tails read at most 64 KiB from the log asynchronously, only for expanded rows with live updates enabled.

## Cancellation and recovery

Cancel removes no project data. Waiting jobs do not start; running jobs use the existing cancellation and recovery path, including subprocess-tree termination and rollback where supported. Closing the app while imports are active asks to cancel them and waits for the owner to close again after they stop. Final journal writes finish before a normal window close.

`AppPaths.Home/imports.json` records reviewed plans and status separately from cache. One queue owns that home at a time, through `imports.lock`; project locks still protect the actual targets. Restart converts previously active records into Interrupted and never resumes them automatically. The CLI can list history without taking queue ownership. A second app/process cannot start imports in that same home while another owns the queue.

Recover rebuilds a fresh plan and requires another review. It checks that an existing target is the unfinished import of that source, preserves the directory, revalidates project/input fingerprints and reapplies the package policy before reimporting. A completed or unrelated project is refused. If initial Unity project creation failed before Assets and the package manifest existed, recovery refuses to overwrite the partial directory; preserve it and choose a new name in Create. Recovery is not a Unity transaction or an undo of arbitrary creator script effects.

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
