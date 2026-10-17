# Offline package inspection

Preview 7 implements the first delivery of the dependency-detection design. Inspection is advisory and shared between the C# core, desktop and CLI. It never starts Unity, extracts avatar assets, installs a package or rewrites a preset.

## Desktop

Selecting a UnityPackage starts one cancellable background inspection. A short status appears if work lasts longer than 300 ms; name/platform/preset controls remain usable. Changing the file cancels that consumer and rejects old results. The selected file's creation overrides reset when the file changes.

Customize packages includes the detected package definitions in the existing selection dialog. Rows show Detected, Possible, Included or a declared version requirement, with evidence in tooltips. Add detected stages eligible additions; Done accepts the working copy and Cancel discards it. Existing pins and opposite SDK kinds are excluded from batch additions. Reopening customization preserves accepted creation overrides.

Declared exact versions can be staged. Declared ranges are preserved in the report and shown on the row; they are excluded from Add detected and require an explicit exact-version choice. The current planner accepts exact versions/latest, not arbitrary VPM range expressions. Inspection does not replace VPM's dependency resolver or guarantee compatibility for a manually chosen version.

The analysis-options menu offers Inspect further with a larger local budget. The existing creation review notes a still-running or partial analysis. The plan uses the user's selected packages; a result arriving later never modifies a pending operation.

## CLI

```powershell
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --json
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --deep --json
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --index-project 'F:\Unity Projects\Existing Avatar' --json
```

Inspection is local-only, including deep mode. `--index-project` explicitly reads script/shader metadata from an existing project's Packages folder before matching; it has a 30-second work budget and yields the shared I/O slot between files. Successfully indexed package versions can be skipped on later runs. Partial indexing is retained without marking that package version complete. No project contents are changed.

JSON contains the archive observation, coverage flags/limitations, index revision, scope, unresolved reference count, suggestions with evidence/constraints, and FromCache. Exit 0 means archive traversal completed; text analysis can still be partial. Exit 2 means partial/invalid archive traversal or cancellation. Always inspect coverage, not exit code alone. ScanMilliseconds is the duration of the original scan, including when its observation is subsequently served from cache.

## Detection and safeguards

GZipStream/TarReader traverse the archive sequentially. Asset/path/meta entries are associated by GUID independently of member order. Bounded text parsing observes serialized script/shader/asset references and embedded package manifests. Binary assets are not imported or decoded. Gzip bytes still need decompression when skipping their contents.

Internal and known built-in GUIDs do not become external requirements. Exact external script/shader matches are stronger than generic asset matches or namespace hints. Avatar-prefab/scene candidates scope graph traversal; multiple candidates require a package to occur in all candidate graphs before promoting its typed references. Examples/Editor/Samples remain lower confidence. This is a candidate graph, not complete Unity object/override resolution.

Included package manifests identify bundled content separately. If bundled content overlaps the selected preset, the dialog requests review instead of installing a duplicate automatically. Duplicate GUID ownership remains ambiguous. No GUID match establishes a minimum supported version. Unknown references are not declared missing packages. The AudioLink corpus case specifically preserves its empty declared dependency map while treating unrelated integration hints conservatively.

## Seed and personal index

The included metadata-only seed has 3,735 signatures across 13 package identities and 17 observed package/version records. It is about 575 KiB. It covers VRChat SDK Base/Avatars/Worlds/Resolver, VRCFury, Poiyomi/Thry, Modular Avatar/NDMF, GoGo Loco, AudioLink, VRCFT Templates and Easy Login. Some signatures are generic controller/asset anchors and intentionally produce Possible rather than Detected results.

`scripts/Build-DependencySeed.py` regenerates the seed from explicitly named Packages directories. The allowlist excludes unrelated creator packages. Its provenance JSON records relative metadata paths, observed versions and SHA256 values; it is an observation of local installed package metadata, **not a publisher attestation**. No source asset/script/DLL content is distributed in the seed. Version coverage is finite; the seed is shipped with application updates.

An inspected archive containing a valid Packages/<id>/package.json and script/shader metadata can teach its signatures to the personal index. The explicit project-index command can extend that index. No scan of all projects occurs at startup. Local observations and signatures keep separate revisions: new signatures reclassify cached observations without rereading the UnityPackage.

## Cache, concurrency and limits

Cache lives in AppPaths.Cache/inspection, separate from settings and projects. Keys include normalized file path, size, last-write timestamp, scanner schema and options. The scanner verifies file metadata after reading. Cache corruption or an unwritable cache does not prevent manual selection; structurally invalid observation caches are ignored. Size/mtime identity is a local optimization, not a cryptographic proof.

Only one archive decompression runs at once. Concurrent consumers for the same file/options share work; canceling one does not cancel remaining consumers. Completed unused requests are removed. All filesystem work and matching stay off the desktop dispatcher.

| Budget | Automatic | Deep |
| --- | ---: | ---: |
| Work time | 8 s | 45 s |
| Expanded stream | 4 GiB | 8 GiB |
| Text per entry | 4 MiB | 16 MiB |
| Text overall | 64 MiB | 256 MiB |
| TAR entries | 100,000 | 200,000 |
| Asset nodes | 50,000 | 100,000 |
| References | 250,000 | 500,000 |

Retained path bytes are capped at 16 MiB. Limits and cancellation are checked during reads/skips as well as between entries. No files are extracted, including link/traversal entries. ArchiveComplete and TextComplete are independent; reaching EOF does not prove full dependency coverage or publisher authenticity.

Observation files over 10 MiB are not retained. Up to 256 observations/240 MiB are kept with generated-cache eviction; local signatures are bounded to 50,000 records and 16 MiB when reading. Atomic cache writes preserve existing files on interruption. Temporary/cache cleanup never targets Unity projects.

## Delivery boundary

This release implements the useful offline path, personal learning from included package metadata, explicit local indexing, deep local inspection, CLI and desktop integration. Automatic repository-content downloads, broad creator-package discovery, remote seed updates, full VPM-range solving and exact Unity object-level dependency analysis remain later work. Existing catalog search is still available manually. No online crawler was introduced to the creation path.

Verification includes generated TAR/gzip fixtures, cache rematching under a denied archive read, optional examples, collisions, manifests/ranges, internal GUIDs, scene/multi-avatar roots, cancellation and native WinUI staging/responsiveness. Real-corpus timings and final published evidence are recorded in VERIFICATION.md and the task artifacts; no fixed latency or complete detection guarantee is claimed.
