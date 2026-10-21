# UC-12 — Git Unavailable / Broken Git State

## Goal
Prevent unsafe mutations when Git is unavailable or inconsistent.

## Examples
- Git not configured.
- Local repository missing.
- Remote missing.
- Authentication failed.
- Network unavailable.
- Remote unavailable.
- Workspace dirty.
- Local/remote diverged with a merge conflict or failed recovery.

## Flow
1. System detects an invalid Git state.
2. UI shows diagnostic information.
3. Read-only information that can still be read may remain visible.
4. Source switch is disabled.
5. Apply is disabled.
6. Runtime Commit + Push is disabled.
7. Release switch is disabled.
8. For a clean supported `diverged` branch operator may first run Git Sync;
   it performs a regular merge and push. Conflicts and recovery failures still
   require external Git tools.
9. Operator fixes Git externally when necessary.
10. Operator runs Fetch again.

## Result
No mutating Git/server operation starts while Git state is unsafe.
