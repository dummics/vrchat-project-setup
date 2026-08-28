# CLI, VPM and Unity end-to-end verification — 2026-08-28

## Scope

This manual verification exercised the public source tree and a disposable
project under the configured Unity projects root. No existing creator project
was modified. The disposable project was removed after evidence collection.

Environment:

- Windows 11, PowerShell 7 and Windows PowerShell 5.1
- Unity 2022.3.22f1 in batch mode
- VPM CLI 0.1.28 and bundled `vrc-get` 1.9.1
- `LCS-2.0.3.unitypackage`, 711,805 bytes

## Scenarios and evidence

| Scenario | Result | Evidence |
| --- | --- | --- |
| Package search | Pass | `packages search gogoloco -Json` returned `gogoloco`, latest `1.8.6`, and its description. |
| Project list | Pass | `projects -Refresh -Json` returned the disposable project as Avatar, Unity 2022.3.22f1, with eight direct VPM packages. |
| Real manifest parsing | Pass after fix | Object-shaped versions such as `{ "version": "3.10.4" }` are returned as `3.10.4`; legacy string values remain accepted. |
| CLI creation dry-run | Pass | Printed destination, UnityPackage and eight-package plan without creating a folder. |
| Real CLI creation | Pass | Unity created the project in 12 seconds; VPM installed the configured preset plus GoGoLoco 1.8.6; the main import took 2:24 and finalization 0:11. State recorded every step as complete. |
| UnityPackage import | Pass | Two expected DLL files were present under `Assets`; Unity create/import logs ended normally with no compiler, fatal or crash pattern. |
| Package list | Pass | Returned eight direct packages with resolved versions, including SDK 3.10.4 and GoGoLoco 1.8.6. |
| Real remove | Pass | GoGoLoco was removed, the direct dependency count became seven and timestamped manifest backups were created. |
| Real add/version change | Pass | GoGoLoco 1.8.3 was installed, then changed to 1.8.6 and verified from `vpm-manifest.json`. |
| Incremental AIO optimization | Pass after fix | The 1.8.3 → 1.8.6 change kept seven unchanged packages and processed only GoGoLoco. End-to-end command time was 19 seconds including validation. |
| Required-package guard | Pass | A dry-run removal of `com.vrchat.base` returned exit code 1 and left the package present. |
| Unknown-package guard | Pass | Adding a nonexistent package returned exit code 1 before changing manifests. |
| Interactive terminal UX | Pass | Keyboard flow covered Home → Projects → Project library → disposable project → AIO package set → review → Cancel → Back. The cached library rendered 30 projects in 55 ms during the run. |
| Automated portability suite | Pass | All 11 groups passed under both PowerShell 7 and Windows PowerShell 5.1, including special-character paths, install, alias, repair and uninstall. |

## UX findings

- The common CLI verbs are short and predictable: `projects`, `packages` and
  `create`. Destructive package intent has a `-DryRun` path.
- JSON is available on read/search operations without terminal decoration.
- Project selection no longer requires copying a path in the interactive flow.
- The Spectre project library is readable and fast. The nested package editor is
  still the established keyboard TUI rather than Spectre; it was clear in the
  tested flow and made no changes after Cancel.
- Initial Unity creation remains dominated by SDK compilation and shader import,
  not by the small UnityPackage. Subsequent package-only operations are much
  faster, especially after unchanged-package skipping.

## Defects fixed from this run

1. Real VPM dependency versions were object-shaped and were previously rendered
   as PowerShell object text. The parser now extracts the `version` property.
2. AIO synchronization reapplied every direct dependency. It now removes only
   deselected packages and installs only added or version-changed packages.
3. The public CLI lacked first-class project/package verbs. It now supports
   discovery, search, list, add/change, remove, create, dry-run and JSON output.
