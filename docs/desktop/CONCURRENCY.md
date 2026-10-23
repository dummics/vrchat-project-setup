# Desktop concurrency

The bottom status opens a live operations flyout for concurrent creation/import jobs, package apply and update work. Each operation occupies a compact name/state row, with ellipsis and a 200-pixel scrolling list limit; detailed messages stay in Activity. Opening it never navigates away or triggers an unsaved-preset decision. New-project planning is protected from automatic update installation and window closure before its queued job exists.

Apply executes staged changes directly without a second confirmation. A compact central progress dialog dims the editor and temporarily disables navigation, showing project position and readable package action/name/version. Cancellation remains available. Preparation validates the plan and target safety before any write. Structured package events keep the current action readable through VPM output; raw tool lines remain in diagnostics. Already queued imports and independent background work continue; the dialog does not acquire a global project lock.

| Work | Coordination |
| --- | --- |
| Package apply | One active desktop apply. Planning captures the session, configuration and package choices before awaiting. The modal editor surface prevents competing clicks; background workers retain their per-target coordination. |
| Navigation and manager loading | A request revision, page and session identity are checked after asynchronous loading. An obsolete completion cannot reopen a manager or overwrite another editor. |
| Project writes | The executor, restore and cleanup share a per-project file lease. The import queue deduplicates normalized destinations. Desktop creation/import requests reject targets in the active apply, before review and again before enqueue. |
| VPM | The shared package-tool mutex is intentional: VCC repository/tool state is shared across projects. Unity work for distinct projects can continue concurrently. |
| Preset/configuration saves | Short commands remain serialized. Persistence additionally uses a dedicated gate, immutable snapshots and preservation of edits made while a save awaits storage. |
| Project scans | Directory scanning/fingerprinting runs off the dispatcher, with cancellation and root/sort checks before displaying results. |
| Updates and maintenance | Installation/restart, reset, configuration import and destructive maintenance stay excluded during active project work. Confirmation-based maintenance rechecks eligibility after the dialog. Update checks retain their single-flight flag through previous-result processing. |
| Read-only support | Diagnostic export does not take the project mutation gate. Its user-facing actions are reserved for technical errors and Activity containing failed operations; normal progress and contact do not advertise it. |

An unavailable competing action reports a short wait message instead of silently doing nothing. Completing package work does not force navigation to the editor that started it. A failed operation remains discoverable in Activity and diagnostic export.

Native verification uses isolated fixtures: a suspended execution boundary proves direct apply without a review dialog, modal navigation exclusion, background preset persistence and maintenance exclusion; a held catalogue lease proves that delayed manager reload cannot reopen an old page. No owner Unity projects are modified by these checks.

This is not a claim that every possible concurrent action or external Unity/plugin process has been exhaustively tested. The current unrestricted import concurrency can exhaust RAM/CPU on smaller PCs; existing resource warnings remain in place. Real low-memory and slow/network-drive acceptance should be tested on representative machines before changing scheduling policy.
