# Online installer and component updates

VRC Project Setup publishes one Windows EXE for install/update. The Native AOT bootstrapper needs no preinstalled .NET runtime. It downloads and verifies components before starting the existing WPF installer engine, which stages, validates, swaps, retains the previous installation, integrates shortcuts and reopens the app. Existing clients recognize the unchanged `Install-VrcSetup-<version>-win-x64.exe` name and verify it with SHA256SUMS.txt.

## Components and trust

`components.json` schema 1 is embedded in the EXE and published for inspection. It pins the exact target version, an HTTPS asset directory, each immutable `<sha256>.zip` name/size/hash and every extracted file hash. Modules contain `payload/` or `engine/` paths only. Duplicate paths, traversal, Windows path aliases, symlinks, undeclared/missing entries, oversized downloads and invalid hashes fail before installation. The payload version must equal the embedded target version. The installer independently verifies all payload files again before replacement. HTTPS and the verified bootstrapper authenticate the pinned metadata; builds are currently unsigned and this is not a substitute for future Authenticode signing.

Application assemblies/resources/metadata form a small changing component. CLI, WinUI and installer runtime/dependency files use stable filename-based buckets with deterministic ZIP timestamps/order. A version-only change does not invalidate runtime buckets; SDK/dependency updates intentionally do. Files are still installed as one coherent app version: components are delivery units, not independently activated plugins.

Before downloading a payload module, matching installed files are copied to fresh staging and rehashed. Otherwise a cached archive is rehashed or downloaded to a unique partial file, size/hash verified and atomically renamed. Corrupt cache is redownloaded; transient HTTP failures retry twice. Cancellation and verification failure preserve the installed app. The previous app is reopened on pre-handoff update failure where possible. No Unity process is killed. The installer engine retains its existing busy checks and rollback behavior.

`<data-home>/updates/components` holds verified archives and a per-cache exclusive lease. Concurrent bootstrappers fail clearly rather than racing. Abandoned partials are removed under that lease on the next attempt. Current components are retained; unused archives are limited by a 512 MiB cache budget and 30-day retention. `last-delivery.json` records downloaded/cached/reused components and bytes; `last-error.log` contains diagnostic details. Temporary reconstruction is removed after the installer exits. The optional portable ZIP remains a self-contained alternative; it has no install/update EXE.

First installation still transfers the required app/runtime content. The small initial EXE alone does not reduce that total. Later transfers avoid unchanged runtime modules, including when skipping versions. Runtimes remain bundled, so setup never changes machine-wide .NET installations.

## Automatic preparation and next-launch installation

The desktop waits 45 seconds after startup before its first scheduled check. A persisted deadline then limits checks to roughly four hours plus a small randomized delay, across application restarts and concurrent instances. Checks only run while the app is open; there is no Windows service or scheduled task. Manual checks remain immediate. The feed selects the newest compatible release, allowing intermediate builds to be skipped.

With automatic updates enabled (the default), an installed copy verifies the unified EXE and runs its hidden `--prefetch` protocol. This reconstructs/verifies delivery content and warms the component cache, then exits without invoking the installation engine. The helper cancels and cleans staging when its parent app exits. Preparation never restarts the active app or modifies its installed files. Portable copies keep manual delivery.

A pending record binds the installer hash, version, installation root and preparation time. At the next ordinary launch, before creating the main window or resuming import work, the app rechecks preferences, channel, version ordering and the installer hash. It defers while another window from the same installation is open. Prepared updates expire after 48 hours. Handoff consumes the record and remembers the attempted version before starting the existing transactional installer, preventing a failed update/relaunch loop. Manual retry or a newer release remains possible. The app stays usable after network/preparation failures; component cache validation and rollback remain unchanged.

The `--prefetch` installer protocol starts with beta.15 and is advertised by `Prefetch: true` in checksum-verified `components.json`. The desktop verifies that explicit capability and matching version before invoking it; unsupported installers remain manual. Older installed clients still install this release through the existing manual updater; automatic delivery starts once the new app is installed. Only the three newest downloaded bootstrap installers plus a pending one are retained; component archives keep their existing separate budget.

## Hosting and a future VPS

GitHub Releases is the initial static asset host. Components use plain HTTPS GET and no GitHub API, token or server process. CI uploads every module and checksum into a draft, checks the asset count, and only then publishes it. Manual workflow runs keep the verified draft unless publication was selected. Keep release assets immutable: corrections need a new version.

`Publish-Windows.ps1 -AssetBaseUri https://downloads.example.com/v<version>/` pins a different component directory in the bootstrapper. Upload the generated hash-named ZIPs to that exact directory before publishing its EXE. No app installer logic changes and no user-facing host field are required.

Release discovery uses a static JSON file at `https://raw.githubusercontent.com/dummics/vrchat-project-setup/main/.github/updates/releases.json`, outside the GitHub REST API's shared-IP quota. Release publication and presentation maintenance regenerate it from complete public releases using the Actions repository token; clients need no token. The contract retains `tag_name`, `draft`, `prerelease`, and `assets` with `name` and `browser_download_url`. `Export-UpdateFeed.ps1` excludes drafts, incomplete releases and external asset URLs; an empty valid set never overwrites the feed. Installer/components remain on GitHub Releases with unchanged hash checks and URL restrictions. There is no automatic fallback to the REST API. Older apps still using that API may need one manual installer download to receive this change.

`ApplicationUpdateSource` also supports an independent HTTPS feed/CDN. Maintainer build parameters `-UpdateFeedUri` and `-UpdateAssetPrefix` pin a future host without changing the format. Feed failures stay non-blocking, and the client cache/backoff still applies.

Migration sequence:

1. Prepare HTTPS hosting, static feed, immutable modules and checksums; test downloads/checksums first.
2. Build a bridge release with the new component host and, if desired, new feed/prefix metadata.
3. Publish that bootstrap EXE and its checksum on GitHub as well: old installed clients only trust GitHub asset URLs and must find the bridge there.
4. Verify an actual old-client upgrade; retain GitHub history/bridge assets. Only migrated clients can rely exclusively on the new feed.

There is no automatic untrusted host discovery, API key, VPS deployment or server credential in the app.

## Verification

Fixture tests cover cold/warm caches, changed application modules, reuse from installed files, corrupt/truncated content, cancellation, HTTP retries, traversal, duplicates, cache retention and static-host feeds. CI seeds only the normal hash-verified archive cache to test before public assets exist; it does not replace the bootstrapper transport or add a production source override. It runs install/upgrade/repair/uninstall, shortcut migration, old GUI updater handoff/relaunch and native UI checks. Public real-network upgrade and first-install evidence are recorded separately in task artifacts.

Build locally with `Publish-Windows.ps1`; release intentionally with `Release.ps1 -NextBeta -Publish`. The release workflow publishes exactly one EXE plus technical component assets, the optional portable ZIP and checksums. To test an unpublished build, pass its output folder as `-ComponentsDirectory` to `Run-InstallerTests.ps1` or `Test-ApplicationUpdate.ps1`. These tests use isolated roots/homes/menus and do not update the owner's app or Unity projects.
