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
The production installer and version stay on the existing WinUI path.

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
| Import | Six archives, Quest name and prefab-path detection, version review, queue and real Unity import from React. Recovery planner validates relocated sources against the original bytes | Explorer drop, native relocation picker, existing destination recovery, pause/resume, owned-project cancellation and Creator Companion in an installed fixture |
| Projects | Discovery, search and invalid name validation on an isolated Unity folder | Verify folder picker, create using installed Unity, opening and live editor indicator, batch and project options, external registrations |
| Project packages | Read installed/locked versions, review restore and removal, protect required packages. Real vrc-get restored damaged locked SDK metadata via the React UI and preserved an imported asset | Exercise catalog and package edits from React with a real repository and validate Unity-open behavior |
| Activity | Import and package operation history, per-project details, inline logs, and diagnostic ZIP containing the operation report and log | Native Save picker and multi-project failure presentation |
| Settings | Import preferences, independent alpha opt-in, migrated Unity paths, themes and custom colors, quick help and version label. React applies the 0.3.7 language and appearance on startup. Italian and English switch without restart | Native pickers and edits, color picker interaction, live feed and preparation, review translated errors in native failure flows |
| Presets | Select a saved preset during import, create and rename in the library, reopen and read saved state | Verify catalog browsing, pins, favourites, delete, unsaved edits and portable sharing flows |
| Installer | CLI and component bootstrapper upgrade 0.3.7; React reads the existing profile; prepared alpha restarts on the new version; shortcut failure restores 0.3.7 | Live feed/download/prefetch, Fixed Version offline portable, same-version build ordering, final production release |

Run the offscreen `--smoke` with a fresh directory. It seeds the journal and
Unity folder in that directory, exercises the React UI, and records
`result.json`. The smoke uses no live Unity editor or project.

For a real Unity/VPM test, launch the built React executable with
`--verify-unity-workflow <fresh-short-path> <Unity.exe>`. It creates the
test archive, starts import from the React review, and verifies the installed
SDK, a compiled assembly and the imported asset. A second run with
`--verify-restore-workflow <same-path>` uses that owned project to verify
the restore action and unchanged imported asset. If the automatic scheduler
holds the import, the test permits one slot only when at least 6 GiB of
physical and commit memory remain. This override applies to the fixture only.

## Distribution checkpoint

To package a private React candidate in the normal installation layout:

```powershell
./scripts/Publish-Windows.ps1 -Version 0.4.0-alpha.2 -OutputDirectory .work/artifacts/react-040-layout -ReactCandidate
```

The switch is explicit and restricted to the 0.4.0 alpha series. The React
entry point is `app/desktop/VrcSetup.Desktop.exe`, and the generated HTML, CSS
and JavaScript are in a separate checksum-verified web component. The default
publication path still builds WinUI.

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
download/prefetch still require integration verification.

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
