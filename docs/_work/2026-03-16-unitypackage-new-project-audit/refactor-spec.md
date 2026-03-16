# Refactor Spec - UnityPackage New Project Subwizard

Date: 2026-03-16

## Goal

Turn the current `UnityPackage -> new project` path into a guided subwizard with explicit state, explicit review, and explicit recovery, while keeping the existing installer backend and entrypoints.

## Target Flow

### Step 1 - Select package

- Prompt for UnityPackage path
- Validate inside the flow
- Re-prompt on invalid input instead of dropping back to the main menu

### Step 2 - Project identity

- Show:
  - source package path
  - suggested project name
  - saved name for that package, if present
  - projects root
  - target path preview
- Accept custom project name
- Recompute target preview immediately

### Step 3 - Existing target decision

- If target does not exist, default action is `create new`
- If target exists, show a dedicated action screen:
  - delete existing and recreate
  - use existing: setup VPM only
  - use existing: setup VPM + import extras
  - back / cancel

### Step 4 - Review

- Present a single review screen with:
  - package path
  - project name
  - target path
  - selected action
  - extras folder status
  - extras count excluding the main package
- From review, allow:
  - start setup
  - change project name
  - choose another package
  - cancel

### Step 5 - Execute

- Delegate to the existing `Start-Installer`
- Preserve current execution and hardening

### Step 6 - Outcome

- Return with clearer outcome copy:
  - success
  - cancelled
  - failed with next action hint

## Internal Implementation Shape

- Add a small internal state object for the subwizard
- Add dedicated helpers for:
  - state creation / refresh
  - package validation
  - target path preview
  - extra package count preview
  - existing target decision
  - review screen
- Keep `Start-Installer` signature compatible

## Commit Plan

1. `docs:` audit artifacts
2. `refactor:` introduce state object + subwizard helpers
3. `refactor:` wire the new UnityPackage flow and review step
4. `fix:` align reliability and branch-specific copy
