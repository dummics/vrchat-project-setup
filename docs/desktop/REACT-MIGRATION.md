# React desktop 0.4.0

## Private Import candidate

Build on Windows x64 with .NET 10, Node 24, npm and WebView2 Evergreen:

```powershell
./scripts/Build-ReactCandidate.ps1 -OutputDirectory .work/artifacts/react-040-candidate
```

Use a fresh output directory. Extract the generated archive and launch
`VrcSetup.ReactDesktop.exe`. Configuration, WebView2 profile and generated test
projects are under `%LOCALAPPDATA%/VrcSetup-React-Candidate`. The candidate
discovers Unity 2022.3.22f1 when Configure Unity is selected. Existing app data
and Creator Companion registrations are outside the candidate's workflow.

React requests are handled inside the native host through WebView2 messages.
C# owns archive paths, inspection, configuration, the reviewed plan and the
import queue. Dropped files use native WebView2 file objects and browsers load
only the bundled UI. The current candidate covers Import with independent
drafts, presets, exclusions, explicit additions, review and queue controls.
It also covers project discovery, empty project review, VPM package selection
and restore review, preset management, Activity history and import
preferences in the isolated candidate profile.
The initial private candidate left the public installer on the WinUI path.

## Initial measurements and milestone

The private alpha.1 artifact measured 63,659 bytes for the compressed frontend
and 68,053,580 bytes for the compressed WPF desktop. The WinUI desktop from
`e79023a` measured 74,298,756 bytes under the same local publish and ZIP
compression strategy. Extracted desktop size was 161,135,449 versus
195,601,308 bytes. The candidate included the WebView2 loader and used the
system Evergreen runtime. These measurements exclude the installer, CLI,
WebView2 runtime and browser profile.

The offscreen native smoke exercised the startup handshake, six real fixture
archives, Quest filename detection, independent draft platform state and
package exclusion/reinclusion. Build reports retain timings and the candidate
archive hash. First-ready time includes fixture staging; compare startup and
process-tree RAM with equivalent workloads before promoting the candidate.
Exercise Explorer drag-and-drop and a real Unity import in a disposable
workspace before beta.

## Parity checkpoint

The React host uses the existing project workspace, import journal, VPM
planner, activity reader and configuration store. The standalone candidate
has a private profile. A package in the installation layout reads the home
recorded by the installer, or its own `data/` in portable mode.

| Surface | Verified in the candidate | Still required |
| --- | --- | --- |
| Import | Six archives, Quest name and prefab-path detection, manual file path using the saved naming policy, version review, queue and real Unity import from React. Pausing during Unity import preserves the owned project and persists across restart. A queued retry interrupted by process exit resumed and compiled in place. Cancellation after restart uses an inline confirmation with the exact folder and preserves the original archive. Reimporting into a completed project restored a missing asset and its original metadata while keeping an edited script. The installed React app handled a Creator Companion rejection and retry with the official VPM CLI on a disposable Windows runner | Explorer drop and native relocation picker. The recovery planner has separate checks for matching the original archive bytes |
| Projects | Discovery, search, multi-project selection and invalid name validation on isolated Unity folders. The Unity indicator follows editor start and exit while the list stays open. Project options restore the newest VPM backup after confirmation, keeping displaced edits in a recovery folder. Empty project creation supports per-project package choices, numeric version review and saving choices as a preset. The Projects button opens the selected project with a matching installed editor if the configured path is stale | Verify the native folder picker and external registrations |
| Project packages | Read installed/locked versions, protect required packages and restore damaged locked SDK metadata. Real vrc-get added, upgraded, downgraded and removed Gesture Manager from React, with Unity closed and in one open Unity batch session. Batch review and apply use per-package targets. A project edited after review was skipped with a localized next action. Cancelling before the project lock preserved both manifests. Cancelling after VPM changed the manifest while Unity was open retained the backup and displayed the recovery action. After closing Unity, restoring the backup recovered the exact original manifest and kept the displaced edit in the recovery copy | |
| Activity | Import and package operation history, per-project details, inline logs and diagnostic ZIP. A real batch with one successful project and one skipped project displays a partially completed operation and both outcomes | Native Save picker |
| Settings | Import preferences, manual file entry, project naming, independent alpha opt-in, migrated Unity paths, themes and custom colors, quick help and version label. React applies the 0.3.7 language and appearance on startup. Italian and English switch without restart. Settings discovers, prepares and applies an alpha using a fixture feed. Diagnostics preferences persist. Cache clearing and reset confirmations preserve a Unity project and its archive; reset preserves the old configuration. Installed repair and confirmed self-uninstall were exercised through React Settings with redirected shortcuts and an isolated profile | Verify native file restore and folder pickers, color picker interaction and translated errors in native failure flows |
| Presets | Select a saved preset during import. Create, rename, pin, favourite, discard unsaved edits, save, reopen and delete through the library. The fixture exercises catalog choices and confirms a saved pin after reopening | Verify portable sharing with the native file dialogs |
| Installer | CLI and component bootstrapper upgrade 0.3.7; React reads the existing profile; prepared alpha restarts on the new version; shortcut failure restores 0.3.7; offline archive runs its bundled Fixed Version. Same-release build ordering, installer checksum rejection and real prefetch, swap and restart verified with a fixture feed and cached components | Verify download over the production feed and component transport, Windows 10 Fixed Version and final production release |

Run the offscreen `--smoke` with a fresh directory. It seeds the journal and
Unity folder in that directory, exercises the React UI, and records
`result.json`. The smoke uses no live Unity editor or project.

With the bundled package tool available, run `--verify-batch-packages <fresh-path>`
to remove a fixture package from one of two projects through the React review.
The verifier checks both UI outcomes and that the other manifest is unchanged.
Run `--verify-batch-package-conflict <fresh-path>` to select both projects,
edit the second manifest after review and confirm the first project changes.
The verifier checks that the second manifest is preserved, the packages
screen explains the conflict and Activity records the partial outcome.
Run `--verify-package-cancellation <fresh-path>` to cancel an apply after it
enters the executor and before it can acquire the project lock. The verifier
checks that the localized outcome is visible and neither manifest has changed.

For a real Unity/VPM test, launch the built React executable with
`--verify-unity-workflow <fresh-short-path> <Unity.exe>`. It creates the
test archive, starts import from the React review, and verifies the installed
SDK, a compiled assembly and the imported asset. A second run with
`--verify-restore-workflow <same-path>` uses that owned project to verify
the restore action and unchanged imported asset. If the automatic scheduler
holds the import, the test permits one slot only when at least 6 GiB of
physical and commit memory remain. This override applies to the fixture only.

With a completed import fixture, run `--verify-import-missing-assets <same-path>`.
The verifier edits the fixture script and moves its probe asset and metadata
aside. It adds the original archive back through React's manual path field
and confirms **Finish import** for the existing project. A passing
`react-import-missing-assets.json` records that the asset and its original
metadata returned byte-for-byte and the edited script remained intact.

For import recovery, `--verify-pause-import <fresh-short-path> <Unity.exe>`
pauses from the queue after the real Unity batch import begins and records
the persisted state in `paused-import.json`. Reopen that profile with
`--verify-resume-import <same-path>` to continue the owned project or with
`--verify-discard-import <same-path>` to exercise both dismissing and accepting
the inline confirmation. Use a separate paused fixture for each outcome.
The discard check verifies the terminal journal entry and preservation of the
source and a neighboring file. The resume check requires the same project and
creation identity, an explicit existing-project label in the review,
the imported asset and a compiled C# assembly. The fixture
runs one Unity import at a time following a 6 GiB physical and commit check.

Use `--verify-vpm-edits <resumed-fixture> [<Unity.exe>]` to exercise search,
review, installation, upgrade, downgrade and removal through React with the
bundled vrc-get and the curated Gesture Manager package. With Unity specified,
the verifier launches the fixture in batch mode and waits for the same
editor to register each requested version. The asset and VPM lock are checked
after every change. Full restoration remains disabled while Unity is open.
The open-editor run showed transient compiler errors during package
replacement and a subsequent successful compilation before the editor exited.
The Projects list stays open for the start and exit assertions, without
manually refreshing it. After removal, run `--verify-project-backup <same-path>`
to restore the latest backup through Project options and check that the
post-backup fixture edit was preserved in the recovery folder.

Run `--verify-open-package-cancellation <resumed-fixture> <Unity.exe>` on
a completed fixture without Gesture Manager. It installs 3.9.7, starts an
update to 3.9.9 and cancels from React after VPM changes the manifest.
`react-vpm-cancellation-open.json` records that Unity stayed open, the backup
held the original manifest and React displayed the recovery action.
Then run `--verify-cancelled-package-recovery <same-path>` after the fixture
editor exits. It dismisses and reopens the backup confirmation, then verifies
the original manifest, the restored installed/locked version and the preserved
edit. The receipt is `react-vpm-cancellation-recovered.json`.

Run `--verify-new-project <fresh-short-path> <Unity.exe>` to create an empty
project through React. The verifier saves a preset with Gesture Manager
3.9.7, chooses 3.9.9 for the new project only, then checks both policies
and the new Projects row. It also opens the resulting project in a separate
batch editor and checks the compiled SDK assembly.
Then run `--verify-open-project <same-path>` to click Open from Projects and
test fallback from a missing configured executable. It launches the installed
Unity that matches the project version in batch mode for the fixture and
checks the exit receipt and unchanged preferences.

The Windows release workflow runs `--verify-hosted-vcc` on an installed
React fixture with the official VPM CLI. It verifies that a rejected
registration leaves the import completed and gives the user a manual
alternative. The second attempt must appear in the official project list,
clear the persisted registration warning and preserve the fixture asset and
package policy. The step writes `react-vcc-registration.json`.

## Distribution checkpoint

To package a private React candidate in the normal installation layout:

```powershell
./scripts/Publish-Windows.ps1 -Version 0.4.0-alpha.2 -OutputDirectory .work/artifacts/react-040-layout
```

The React entry point is `app/desktop/VrcSetup.Desktop.exe`. The generated
HTML, CSS and JavaScript are in a separate checksum-verified web component.
The Windows release workflow uses React for 0.4.0 and later. Use
`verify_only` to exercise this path from a branch whose source version still
points to 0.3.7. The workflow builds and checks the React payload and exercises
setup and restart over a verified WinUI release.

For a second portable archive using the bundled browser, pass
`-FixedWebView2Runtime <extracted-x64-runtime-folder>`. Obtain the Microsoft
Fixed Version CAB and extract it using the [Microsoft distribution guide](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).
The packaging step checks the signature and architecture of
`msedgewebview2.exe` and hashes each runtime file in the offline manifest.
Evergreen and installer payloads stay unchanged. The first verified archives
were about 104 MiB (Evergreen) and 400 MiB (offline). On Windows 11, two
isolated launches verified both the normal portable `data/` directory and
the actual browser executable from the ZIP with external browser DNS
resolution disabled. On Windows 10 the app
grants read and execute access to the two AppContainer groups for the bundled
runtime folder. That branch still needs a Windows 10 fixture.

The CLI installer and component bootstrapper both upgraded the verified
0.3.7 portable release in an isolated install directory. The bootstrapper used
locally cached components. Checks confirmed the shortcut target, unchanged
configuration and import journal, removal of active WinUI resources and a
preserved WinUI rollback copy. An isolated profile originally installed by
0.3.7 then started React from that same entry point. React showed the saved
project, preset and history without modifying the configuration.

With verified components in the isolated profile cache, React rejected a
prepared alpha with alpha opt-in off. After opting in, `0.4.0-alpha.3`
handed off to the bootstrapper, exited, and relaunched as
`0.4.0-alpha.4` with the same profile. The shipped 0.3.7 also handed off to
the same bootstrapper format and reopened the fixture directly in
`0.4.0-alpha.5`, without constructing a WinUI window. A forced Start menu path collision
after the program swap restored the original 0.3.7 marker, WinUI files and
configuration. The prepared-update test seeds the receipt and cached files
to exercise channel gating, installation and restart. Feed discovery and
download/prefetch now also have a separate integration fixture.

Run the installed React executable with `--verify-feed-update <fixture-root>`.
The fixture needs the `.react-profile-fixture` and `.vrcsetup-update-smoke`
markers, an installed profile with alpha off, and `spec.json` with its
absolute `Root` and a newer alpha `Version`. Stage that release's installer,
`SHA256SUMS.txt` and `components.json` under `delivery/` and the hash-named ZIPs
under the fixture home's `updates/components/`. The verifier exercises Settings
against the React feed URL with fixture responses, rejects corrupt
installer bytes, then runs the real bootstrapper's prefetch and install. It
redirects shortcuts and relaunches offscreen with the same install and profile
paths. The `feed-handoff.json`, `restarted-profile.json` and installation receipt
show what was offered, prepared and applied. A local integration run upgraded
alpha.3 to alpha.5 with the profile unchanged after alpha was enabled, and
retained the older installation. Production HTTP delivery remains a release check.

The same path upgraded `0.4.0-alpha.11+build.101` to
`0.4.0-alpha.11+build.102` using two packaged builds. Alpha consent,
installer checksum rejection, component prefetch, restart and retention of
build 101 passed. The configuration hash was identical before and after
handoff. React renders the identity as `0.4.0-alpha.11 · build 102`.

Publish the canonical unnumbered release first so 0.3.7 can discover the
migration. Subsequent revisions of that release use a Windows workflow
dispatch from `main` with the same base version and a higher `+build.N`.
Git tags carry the whole identity, and asset filenames use `-build.N`.
The old feeds keep canonical versions. React checks the build feed and
falls back to the appropriate legacy feed if the build feed is absent.

## Integration order

1. Review the private candidate and finish the comparable runtime measurements.
2. Complete React parity for Projects/VPM, presets, Activity, Settings, import
   recovery and localization. Preserve installed paths and persisted data.
3. Package the localized web assets as their own verified component. Produce
   both an Evergreen portable and a Fixed Version offline portable alongside
   the existing setup format.
4. Add application build numbers across release identity, version comparison,
   installation records and prepared updates. Keep the canonical SemVer release
   visible to 0.3.x and use unique immutable assets for subsequent builds.
5. Validate offline first launch, update bytes, an upgrade from 0.3.6, same
   version build updates and rollback before publishing beta and stable builds.

## 0.3.7 bridge and React replacement criteria

The bridge offers a separate alpha opt-in in Updates. The ordinary feed keeps
alphas out of reach of older clients. The experimental feed offers stable,
beta/RC and alpha builds to opted-in clients. Disabling alpha invalidates an
offer and blocks a prepared alpha at the next launch. It does not downgrade an
already installed build.

Ship the complete React alpha only when these workflows pass from their normal
entry points with an existing 0.3.x profile:

| Surface | Required checks |
| --- | --- |
| Import | Picker/drop, multiple files, target detection and manual selection, presets and versions, review, Unity import, progress and logs, pause, resume after restart, retry, cancel, cleanup, VCC registration |
| Projects | Discovery and sorting, add existing, create/open, open-editor state, attention and recovery actions, single and batch selection, project options |
| VPM workspace | Installed and available packages, pins, add/update/downgrade/remove/restore, confirmation, Unity-open protections, backups and failure messaging |
| Presets | Manage saved presets, share/import/export, favourites, browsing and version policy, unsaved edits |
| Activity | Imports and operations, log access, diagnostic export, keeping recovery actions on the owning screen |
| Settings and shell | First run, editor and project paths, IT/EN, accessibility, appearance persistence, update channels, help and restart |
| Installation and entry points | Start menu, shortcuts, CLI GUI launch, portable, runtime check, same-path upgrade from 0.3.7, reopening React, no active WinUI files in the installed payload, rollback to the previous installation |

The existing installer validates a fresh staging folder and swaps it into the
same install path. A complete React payload can replace WinUI by preserving
the same entry points and omitting WinUI assemblies from the new manifest.
Keep old app files in the installer's rollback folder. Never remove Unity
projects, activity records, configuration or presets as part of replacement.
