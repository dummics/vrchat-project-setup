# Local development storage

Dom wants this secondary project to occupy the minimum practical disk space. Treat build/test environments as temporary, not a permanent archive.

- At a completed work boundary, remove local release payloads, downloaded installers, repeated isolated installations and generated bin/obj output when no project process needs them. Use `scripts/Clear-LocalOutputs.ps1` (preview by default, `-Apply` for verified disposal).
- Keep compact conclusions, essential failed-case logs and a useful final screenshot set. Do not retain a complete runtime/install copy for every retry. GitHub releases remain the distribution archive.
- Prefer reuse of one disposable verification directory. Retain a large fixture only for a concrete unresolved investigation; record why and when it can be removed.
- Aim for less than 1 GiB at rest, investigate more than 2 GiB. Temporary builds may exceed that; clean them after verification/publication rather than accumulating versions.
- Never clean the real installed app, user data, Unity projects, source, Git history, uncommitted work or shared dependency caches as project output.

See `docs/desktop/LOCAL-STORAGE.md` for scope and commands.
