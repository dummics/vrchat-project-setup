# Offline package inspection

## 0.3 Trixie reference check

The read-only deep scan of `Trixie_By_Ray9_Updatedd.unitypackage` traversed all
530 assets. Binary content is classified before applying the text budget; a large
BMP no longer incorrectly marks the text analysis incomplete. Both coverage flags
are true after that fix. SDK Base, SDK Avatars and VRCFury have typed reference
evidence; Poiyomi and AudioLink remain suggestions to confirm. There are 21
unresolved references and no declared package version requirements. Observed
signature versions must not be shown as required versions.

The creator screenshot describes an installed environment. It does not establish
that Gesture Manager, Jerry's Templates, GoGo Loco, Pumkin tools or the SPS
Configurator must all be separately installed for this archive. Optimized shaders
and some SPS content are bundled, but that alone does not prove the corresponding
external package is either required or removable. Detailed evidence and the
comparison are kept in `.work/artifacts/trixie-inspection/`. This is one avatar,
not a validated multi-avatar corpus or a successful Unity compilation.

`inspect --json --advice` is accepted by the CLI argument parser and has a real
process regression covering the proposal envelope. Proposals refer to the current
selection; an empty proposal list can mean that the detected packages are already
selected. It does not mean that no dependencies were detected.

Preview 7 implements the first delivery of the dependency-detection design. Inspection is advisory and shared between the C# core, desktop and CLI. It never starts Unity, extracts avatar assets, installs a package or rewrites a preset.

## Desktop

The import recap displays numeric available versions for floating policies, using
a lazy metadata lookup that cannot register package sources. Explicit pins remain
unchanged; an available version is not a resolved dependency plan. Loading and
unavailable states are shown explicitly. These 0.3 candidate changes still await
runtime verification.

Selecting a UnityPackage starts one cancellable background inspection with immediate status and progress feedback; name/platform/preset controls remain usable. Review is unavailable while analysis or review preparation is running, and a partial or unavailable final analysis still permits explicit manual review. Changing the file cancels that consumer and rejects old results. The selected file's creation overrides reset when the file changes. The 0.3 refactor changes are pending native verification.

Strong matches are displayed separately from “Other packages to check”. A
possible match with evidence remains unselected until the user chooses its
individual Add action; that stages the floating `latest` policy for this import
only and does not edit the saved preset. Possible matches are excluded from
the bulk Add detected action. Included packages and unknown GUIDs are not
implicitly installed. A possible text hint can reflect optional integration;
the user-facing distinction must not be read as proof of a requirement.

Customize packages includes the detected package definitions in the existing selection dialog. Rows show Detected, Possible, Included or a declared version requirement, with short explanations in tooltips. Detailed paths and signature provenance remain in the inspection report. Add detected stages eligible additions; Done accepts the working copy and Cancel discards it. Existing pins and opposite SDK kinds are excluded from batch additions. Reopening customization preserves accepted creation overrides.

Declared exact versions can be staged. Declared ranges are preserved in the report and shown on the row; they are excluded from Add detected and require an explicit exact-version choice. The current planner accepts exact versions/latest, not arbitrary VPM range expressions. Inspection does not replace VPM's dependency resolver or guarantee compatibility for a manually chosen version.

If an archive contains matching package metadata for a package already selected,
the import screen names it and offers a dedicated review containing only relevant
suggested additions and bundled-package overlaps. The user explicitly chooses
whether to add or remove each item; closing the review leaves the selection
unchanged. The full preset editor remains available separately. Metadata alone
does not prove the external package is redundant, so the user decides whether to
remove it. Foundation and
VRChat SDK packages are excluded from this removal-review prompt.

The file can also declare a version requirement for an already selected package.
When the selected value differs from that declaration, the review asks the user
to check the version in package settings; it does not infer incompatibility or
replace a pin automatically. A version range is shown as declared, not as an
installed version.

Import automatically retries a budget-limited scan once with the larger local budget. The first result stays available during that cancellable retry. Invalid archives are not retried. The existing creation review notes a still-running or partial analysis. The plan uses the user's selected packages; a result arriving later never modifies a pending operation.

## CLI

```powershell
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --json
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --deep --json
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --json --advice
vrcsetup inspect 'C:\Downloads\Avatar.unitypackage' --index-project 'F:\Unity Projects\Existing Avatar' --json
```

Inspection is local-only, including deep mode. `--index-project` explicitly reads script/shader metadata from an existing project's Packages folder before matching; it has a 30-second work budget and yields the shared I/O slot between files. Successfully indexed package versions can be skipped on later runs. Partial indexing is retained without marking that package version complete. No project contents are changed.

JSON contains the archive observation, coverage flags/limitations, index revision, scope, unresolved reference count, suggestions with evidence/constraints, and FromCache. Exit 0 means archive traversal completed; text analysis can still be partial. Exit 2 means partial/invalid archive traversal or cancellation. Always inspect coverage, not exit code alone. ScanMilliseconds is the duration of the original scan, including when its observation is subsequently served from cache.

## Detection and safeguards

The 0.3 candidate adds advisory proposals for missing packages, declared version
requirements and bundled package overlap. `--json --advice` wraps the inspection
with AdviceSchema, the current configuration's default package selection and
proposals; plain `--json` retains the inspection-report shape. No proposal edits
the selection or project. A range without an exact proposed version needs manual
choice. Bundled overlap requests review, not automatic removal. Absence of a
reference is never a removal recommendation. Evidence-backed Possible matches
produce `ReviewPossible`, without a proposed version or automatic selection.

GUID evidence records bounded signature sources and observed versions. These
versions describe the index observations, not a minimum compatible release.
Strong script/shader detection requires a matching signature kind; generic
anchors and ambiguous matches in other avatar variants cannot establish a
shared typed requirement. Strong GUID-based detection also requires complete
archive and eligible-text coverage: an incomplete scan keeps a match as Possible
because another avatar variant may have been missed. Explicit manifest
declarations from a package manifest at its matching `Packages/<id>/package.json`
are reported independently of this GUID confidence rule. Dependencies found in
other `package.json` files, including files under `Assets`, remain possible
suggestions because they do not establish an installed VPM package. A weaker
manifest hint cannot alter the constraint declared by an embedded package.
Possible suggestions show the available version that the Add action would use;
an unverified version hint is not presented as a requirement.
Normal UI copy presents suggested actions; archive/text limits and unresolved
references remain available in the diagnostic report, without claiming complete
dependency certainty.

GZipStream/TarReader traverse the archive sequentially. Asset/path/meta entries are associated by GUID independently of member order. Bounded text parsing observes serialized script/shader/asset references and embedded package manifests. Binary/model/media assets are not imported or decoded. Their intentional exclusion does not mark eligible Unity text as truncated: TextComplete refers to supported text analysis, not every byte or importer feature. Gzip bytes still need decompression when skipping their contents.

Internal and known built-in GUIDs do not become external requirements. Exact external script/shader matches are stronger than generic asset matches or namespace hints. Avatar-prefab/scene candidates scope graph traversal; multiple candidates require a package to occur in all candidate graphs before promoting its typed references. More than eight candidates conservatively downgrade inferred requirements instead of multiplying graph walks. Matching itself also observes cancellation. Examples/Editor/Samples remain lower confidence. This is a candidate graph, not complete Unity object/override resolution.

Included package manifests identify bundled content separately. If bundled content overlaps the selected preset, the dialog requests review instead of installing a duplicate automatically. Duplicate GUID ownership remains ambiguous. No GUID match establishes a minimum supported version. Unknown references are not declared missing packages. The AudioLink corpus case specifically preserves its empty declared dependency map while treating unrelated integration hints conservatively.

## Seed and personal index

The included metadata-only seed has 3,410 signatures across 12 package identities and 16 observed package/version records. It is about 526 KiB. It covers VRChat SDK Base/Avatars/Resolver, VRCFury, Poiyomi/Thry, Modular Avatar/NDMF, GoGo Loco, AudioLink, VRCFT Templates and Easy Login. Some signatures are generic controller/asset anchors and intentionally produce Possible rather than Detected results. Preview 8 excludes known world-only identities from automatic advice and indexing. This does not disable existing world-project management. See [research intake and expansion boundary](RECOGNITION-RESEARCH.md).

`scripts/Build-DependencySeed.py` regenerates the seed from explicitly named Packages directories. The allowlist excludes unrelated creator packages. Its provenance JSON records relative metadata paths, observed versions and SHA256 values; it is an observation of local installed package metadata, **not a publisher attestation**. No source asset/script/DLL content is distributed in the seed. Version coverage is finite; the seed is shipped with application updates.

An inspected archive containing a valid Packages/<id>/package.json and script/shader metadata can teach its signatures to the personal index. The explicit project-index command can extend that index. No scan of all projects occurs at startup. Local observations and signatures keep separate revisions: new signatures reclassify cached observations after verifying the UnityPackage content.

## Cache, concurrency and limits

Cache lives in AppPaths.Cache/inspection, separate from settings and projects. Keys include normalized file path, size, last-write timestamp, scanner schema, options and a SHA-256 digest of the archive. The archive is read to verify its digest even on a cache hit, so large files take time; cached observations still avoid repeating archive analysis. On Windows the source is opened without write or delete sharing while inspection completes. Cache corruption or an unwritable cache does not prevent manual selection; structurally invalid observation caches are ignored. Suggestions applied to an import are checked against the identity used to plan it; if the source changed, the user can recheck the suggestions while retaining their selections.

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

Verification includes generated TAR/gzip fixtures, cache rematching and same-size/same-timestamp source replacement, optional examples, collisions, manifests/ranges, internal GUIDs, scene/multi-avatar roots, cancellation and native WinUI staging/responsiveness. Real-corpus timings and final published evidence are recorded in VERIFICATION.md and the task artifacts; no fixed latency or complete detection guarantee is claimed.
