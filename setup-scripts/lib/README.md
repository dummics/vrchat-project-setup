Helpers for vrcsetup scripts
 - `menu.ps1`: exports `Show-Menu` function that presents an interactive menu with arrow key navigation and numeric selection.
 - `utils.ps1`: exports `Install-NUnitPackage` helper function.
 - `config.ps1`: exports `Load-Config` and `Save-Config` helper functions.
 - `projects.ps1`: scans configured folders for Unity projects and maintains the incremental project index.
 - `spectre.ps1`: Spectre.Console rendering helpers with a built-in menu fallback.
 - `navigation-ui.ps1`: flat menu navigation and responsive, searchable project library.

Usage:
 Dot-source the helpers in your script to use functions:
 . "${scriptDir}\lib\menu.ps1"
 . "${scriptDir}\lib\utils.ps1"
