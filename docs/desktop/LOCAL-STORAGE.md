# Keep the checkout small

Local verification and release copies are disposable. Published releases and Git history provide durable distribution/source history; small reports retain the useful verification evidence.

Preview cleanup from the desktop checkout:

```powershell
./scripts/Clear-LocalOutputs.ps1
./scripts/Clear-LocalOutputs.ps1 -Apply
```

The script covers registered checkouts inside this project's canonical folder. It removes project bin/obj directories, copied payload/install directories identified by application manifests, Unity Library caches in isolated artifact projects, and generated executable/library/archive outputs under `.work/artifacts`. It rejects project processes, linked paths and paths outside the canonical project. It preserves other evidence files, input UnityPackages, source, Git metadata, user settings and the actual installed application. Removed builds and fixture runtime files must be regenerated before rerunning a historical test; a retained report is evidence, not a runnable installation.

Keep only essential final evidence per task. Do not create repeated full self-contained runtimes for intermediate UI attempts. Reuse a disposable output directory or clean obsolete versions at the verified boundary. Before retaining a large unresolved fixture, state its purpose and removal condition in its result report.

Idle storage target: below 1 GiB; investigate growth above 2 GiB. Build/test peaks are temporary. Global NuGet/SDK caches are outside this cleanup and must not be duplicated into the project just to preserve an old run.
