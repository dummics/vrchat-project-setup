# UnityPackage -> New Project Flow Map

Date: 2026-03-16
Branch: `ux/2026-03-16-wizard-pass`

## Current Flow

1. Main menu -> `Setup project`
2. Choose `UnityPackage (.unitypackage) -> create new project`
3. Prompt for `UnityPackage path`
4. Validate:
   - path exists
   - extension is `.unitypackage`
5. Derive naming:
   - raw package filename
   - naming rules
   - saved name for that package, if present
6. Prompt for `Project name`
7. Compute `targetProjectPath`
8. Branch on target existence:
   - target missing -> create new project
   - target exists -> ask user:
     - delete existing and recreate
     - use existing and setup VPM only
     - use existing and setup VPM + import extras
     - cancel
9. Show final confirm
10. Execute selected action via `Start-Installer`
11. Return to wizard

## Execution Branches

### A. Create new project

1. Create Unity project
2. Apply Unity Test Framework / manifest tweaks
3. Install configured VPM packages
4. Import main UnityPackage
5. Import extra UnityPackages, if configured
6. Run bounded finalize / settle pass
7. Mark state as completed

### B. Delete existing and recreate

1. Confirm delete
2. Delete existing folder
3. If delete fails and Unity is using the folder:
   - detect matching Unity PID(s)
   - allow closing the specific PID
   - retry delete or cancel
4. Continue with branch A

### C. Use existing project -> VPM only

1. Skip project creation
2. Apply package/config setup to existing project

### D. Use existing project -> VPM + extras

1. Skip project creation
2. Setup existing project
3. Import configured extra UnityPackages only

## Failure / Recovery Paths

- Invalid package path
- Package path escaped incorrectly by PowerShell drag and drop
- Missing config essentials
- Existing target blocked by Unity
- Cancel during create / import / finalize
- Partial project left on disk after crash
- Log file contention during VPM steps

## Main UX Problem

The code already supports the main variants, but the user experiences them as a sequence of prompts and branches instead of a guided flow with explicit state, review, and recovery.
