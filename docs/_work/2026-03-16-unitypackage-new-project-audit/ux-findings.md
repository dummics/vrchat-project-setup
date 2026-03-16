# UnityPackage Flow UX Findings

Date: 2026-03-16
Scope: most-used path `UnityPackage -> new project`, including all downstream branches

## High Priority

1. The main path is not represented as an explicit flow.
   The user moves across prompts and hidden branches instead of steps with a stable mental model.

2. The `existing project` branch is functionally rich but visually under-explained.
   Delete, reuse, and reuse+extras are real product decisions but are presented as a short branch menu.

3. The user reaches execution with limited review.
   There is no strong preflight screen showing package, final project name, target path, chosen action, and extras count together.

4. Recovery exists, but the system still feels reactive.
   Failures such as locked folders and partial project cleanup are handled, yet they are still discovered late instead of anticipated in the flow.

## Medium Priority

5. Naming and target path are still too tightly coupled to immediate execution.
   They should be part of a dedicated identity step, not just a prompt followed by a branch.

6. Extra UnityPackages are powerful but under-communicated.
   The user should know before execution whether extras are configured and how many will be imported.

7. Success and failure exits are still too uniform.
   Returning with a generic `Press ENTER` gives little guidance about what just happened and what to do next.

## Reliability Findings Relevant To UX

1. Drag and drop path normalization was incomplete for PowerShell escaped characters.
2. Folder delete could fail without guided recovery when Unity still owned the project path.
3. Installer logging could fail because of concurrent writes to the same log file.

These are not only reliability bugs. They are also UX issues because they break trust during the main creation path.
