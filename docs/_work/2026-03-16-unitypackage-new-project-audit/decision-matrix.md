# UnityPackage Flow Decision Matrix

Date: 2026-03-16

| Situation | Current behavior | Desired behavior after refactor |
| --- | --- | --- |
| Package path invalid | Error screen, then return | Stay inside the subflow and re-prompt with context |
| Package name implies a suggested project name | Show suggested name | Show suggestion, saved name for that package, target preview |
| Target folder does not exist | Create new project | Keep as default path with explicit review before start |
| Target folder already exists | Ask action in a separate menu | Make it a dedicated step in the subwizard |
| Delete existing target fails | Error or recovery depending on path | Consistent recovery with PID selection and retry |
| Extras folder configured with extra packages | Imported implicitly later | Show extras count in the review step before execution |
| Extras folder configured but empty | User has little visibility | Show `0 extra packages` explicitly in review |
| Cancel during create/import/finalize | Contextual cancel messages exist | Preserve behavior, surface it consistently in the flow |
| Partial project remains after crash | Cleanup flow available | Keep cleanup flow, align wording and outcomes with the main subflow |
| Logging fails during VPM steps | Now fixed in branch | Keep robust single-writer logging |

## Decisions Locked For This Wave

- Keep the current backend entrypoint `Start-Installer`
- Build a dedicated subwizard only for `UnityPackage -> new project`
- Keep the existing menu/TUI primitives
- Include reliability hardening inside the same wave
- No PR; same worktree; regular local commits
