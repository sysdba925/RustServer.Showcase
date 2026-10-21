# UC-03 — Fetch and Synchronize Development with Remote

## Goal
Refresh known remote state, then explicitly synchronize a chosen Development
repository when required.

## Preconditions
- Environment is `Staging`.
- Git is configured.

## Flow
1. Operator runs `Fetch`.
2. System fetches Plugins and Configs.
3. System validates remote access and authentication.
4. System updates known remote state.
5. System preserves both local Development branches and HEADs.
6. System reports local/remote relation for both repositories.
7. When one repository is `behind`, `ahead` or `diverged`, operator selects
   that repository and runs `Git Sync`.
8. System fast-forwards, pushes, or merges then pushes according to the
   freshly observed relation; a conflict is aborted and reported for manual
   resolution.
9. If Development HEAD changed, UI shows that Development now differs from the current server baseline.

## Failure
If Fetch or Sync cannot safely complete, no Source switch or Apply is
performed. Dirty, unsupported or invalid workspaces are not repaired
implicitly.

## Result
Fetch updates remote-tracking state only. A successful explicit Sync updates
the selected local Development branch without changing server state.
