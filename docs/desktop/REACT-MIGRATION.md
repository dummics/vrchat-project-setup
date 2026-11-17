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
