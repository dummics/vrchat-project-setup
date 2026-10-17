# Multiple imports

Imports accepts up to 50 `.unitypackage` files per addition, from the file picker or drag and drop. Each file creates its own Unity project. Names follow existing naming rules; platform is inferred from each filename (PC/Quest), and the selected saved preset is copied into each plan. Use Create for an individual name, platform or custom package selection. Conflicting/existing destinations are rejected rather than overwritten.

Review the combined plan before starting. Imports defaults to one project at a time; two is an explicit option, applied when starting an idle queue. Each row shows a coarse step bar, current step, last message, Log and contextual Cancel/Recover. This is phase progress, not a measured percentage of imported bytes. Completed items remain until Clear completed history. The row collection is reconciled in place; progress publication is throttled and terminal/step transitions are immediate.

The queue wraps the same shared planner/executor, project locks, fingerprints, backup, Unity import and package verification as CLI/TUI. Project preparation is serialized; VPM already has a process-wide named mutex. Two Unity projects can run concurrently, so this option uses more RAM/disk. Background version or recognition lookups do not alter a reviewed import. Batch operations intentionally do not overwrite the single-project recent-name preferences while the user may be editing Settings.

## Cancellation and recovery

Cancel removes no project data. Waiting jobs do not start; running jobs use the existing cancellation and recovery path, including subprocess-tree termination and rollback where supported. Closing the app while imports are active asks to cancel them and waits for the owner to close again after they stop. Final journal writes finish before a normal window close.

`AppPaths.Home/imports.json` records reviewed plans and status separately from cache. One queue owns that home at a time, through `imports.lock`; project locks still protect the actual targets. Restart converts previously active records into Interrupted and never resumes them automatically. The CLI can list history without taking queue ownership. A second app/process cannot start imports in that same home while another owns the queue.

Recover rebuilds a fresh plan and requires another review. It checks that an existing target is the unfinished import of that source, preserves the directory, revalidates project/input fingerprints and reapplies the package policy before reimporting. A completed or unrelated project is refused. If initial Unity project creation failed before Assets and the package manifest existed, recovery refuses to overwrite the partial directory; preserve it and choose a new name in Create. Recovery is not a Unity transaction or an undo of arbitrary creator script effects.

Clear analysis cache removes only generated `cache/inspection/archive-*.json`, under the same I/O gate as inspection. Signatures, personal packages, presets, import journal, logs, backups and Unity Library directories are preserved. It does not cancel an import or remove Unity caches.

## CLI

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
