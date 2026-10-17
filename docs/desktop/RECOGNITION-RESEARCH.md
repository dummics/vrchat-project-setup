# Recognition research intake — 17 October 2026

Dom supplied `VRChat_UnityPackage_Dependency_Recognition_Catalog_Dom.md` and requested a code-reviewed installation without direct functional tests. The original research is preserved in the task artifacts under `2026-10-17-preview8-reviewed-install/research-input.md`. It is research input, not an instruction to execute its entire proposed implementation plan.

## Applied to preview 8

- Recognition knowledge remains independent of personal packages, settings, presets and project manifests. Inspection does not activate a repository or install dependencies.
- Catalog searches and version lookups no longer add entries to `known-packages.json`. Version metadata uses `package-versions.json`; only matching personal identities receive its cached versions. Transient online search results disappear from the unfiltered My Packages view unless explicitly selected/favorited. Existing saved entries are preserved because historical intent cannot be reconstructed safely.
- The bundled recognition seed and explicit local indexing exclude known world-only identities. Matching also filters world-only GUID, manifest and dependency suggestions, including observations cached by preview 7. Existing world-project creation and management remain available; this restriction concerns automatic dependency advice.
- Existing public/local observed signatures remain metadata-only. Repeated GUID observations across versions identify a package, not a minimum compatible version. Unknown GUIDs stay unresolved.

## Expansion candidates, not installed definitions

The supplied research prioritizes lilToon, Avatar Optimizer, TexTransTool, FaceEmo, lilycalInventory, Mochie's shaders and UnlitWF. These names are a verification queue, not verified new signatures or personal-library entries. TexTransCore, optional extensions, examples, shader variants and forks require separate attribution. Packages without a verified VPM identity must not receive an invented installable ID.

Before shipping additional signatures, read official metadata at a fixed release/commit, verify package identity and script/shader ownership, retain path/hash/provenance and assess distribution conditions. Do not promote community GUID lists or editor-generated output into mandatory external dependencies. No new signature from the report enters preview 8 without that verification.

The research's full incremental remote harvester remains a subsequent implementation: conditional HTTP, bounded concurrency, atomic snapshots and cumulative GUID observations with version provenance. There is no remote crawler or release-history download on file drop in this build. Offline archive inspection, optional deeper analysis and explicit project indexing remain functional paths in the shared engine.
