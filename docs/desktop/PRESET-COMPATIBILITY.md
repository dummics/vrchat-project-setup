# Preset compatibility

App updates replace program files, not the external user data home. Existing presets are user-owned selections: preserve names, package IDs, exact pinned/prerelease versions, `latest`, favorites and explicitly empty presets. New default packages apply to fresh configuration only; changing defaults must not rebuild existing user presets.

The configuration schema remains 2. Supported historical package policies include arrays of IDs (meaning `latest`), ID-to-version maps and ID-to-object maps with `version`/`Version`. All round-trip through the current string-map writer without resolving `latest` into a permanent pin. Portable preset exports retain their separate version-1 format and import as a new non-conflicting name.

Loading schema-1 configuration makes a small, content-addressed original JSON snapshot in the data home's `migration` folder before normalization. Repeated loads of the same original do not create duplicates. Loading does not rewrite the active file; a later explicit save uses atomic storage and revision conflict checks. Unknown top-level settings are retained via extension data.

Unsupported package shapes or incomplete preset entries fail visibly rather than silently becoming empty dictionaries. A newer configuration schema is rejected before save, rather than being relabeled as schema 2. The original file stays untouched. Recovery/reset remains an explicit user choice, never an automatic fallback that replaces presets.

Before changing schema or serialization, add upgrade/save/reopen fixtures for each supported historical shape and verify names, policies and unknown settings. Preserve the small original JSON for a real schema conversion. Do not create whole application/project backups for a preset migration.

The New project layout is integrated on `feature/winui-desktop` since e9dc820: compact name/platform fields, preset below them, package search/list below, and a reserved bottom row for creation/cancel. Saving a new preset is secondary and appears after customization. Layout changes do not change saved preset identity or serialization. These changes are pending the next release after beta.26.
