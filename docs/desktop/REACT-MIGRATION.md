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
It also previews project discovery and VPM restore, reads Activity history,
and saves import preferences in the isolated candidate profile.
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

The isolated React host now uses the same import journal, project workspace,
VPM restore planner, activity reader and configuration store as the released
app. This does not enable the candidate to operate on the installed profile.

| Surface | Verified in the candidate | Still required |
| --- | --- | --- |
| Import | Six archives, independent draft targets, package exclusions, distinct compilation error action and dismissible notice | Explorer drop, full Unity import, source relocation, existing destination recovery, pause and resume across restart, owned-project cancellation and Creator Companion with real fixtures |
| Projects | Discovery and search on an isolated Unity folder | Opening and live editor indicator, add/create, batch and project options, external registrations |
| Project packages | Read installed/locked versions and review restore using VPM metadata | Execute restore with real VPM packages, browse/add/update/downgrade/remove and review Unity-open behavior |
| Activity | Import history and opening its log | Operation detail, operation log and diagnostic ZIP verification |
| Settings | Save and reload import limit and hide-completed preference | Editor/path options, appearance, native update service, help and complete IT/EN |
| Presets | Select a saved preset during import | Standalone management, browsing, versions, favourites and sharing |
| Installer | Bridge 0.3.7 on the stable feed | Production payload, updater and offline portable, clean upgrade from 0.3.7 with preserved data, same-version builds and rollback |

Run the offscreen `--smoke` with a fresh directory. It seeds the journal and
Unity folder in that directory, exercises the React UI, and records
`result.json`. The smoke uses no live Unity editor or project.

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
