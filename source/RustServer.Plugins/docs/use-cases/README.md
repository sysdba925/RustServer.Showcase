# RustServer.Plugins — Use Cases

These use cases describe operator workflows for the new RustServer.Plugins architecture.

They are derived from:

- `requirements.md`
- `domain-model.md`
- `operations.md`
- `api-contract.md`

Use cases describe **what the operator is trying to achieve and in what order**.

They are not API contracts and they are not UI mockups.

## Index

1. [UC-01 — First Start / Not Initialized](UC-01-first-start.md)
2. [UC-02 — Work in Staging + Development](UC-02-staging-development.md)
3. [UC-03 — Update Development from Remote](UC-03-update-development-from-remote.md)
4. [UC-04 — Apply Development to Staging](UC-04-apply-development-to-staging.md)
5. [UC-05 — Commit Runtime Changes from Development](UC-05-commit-runtime-development.md)
6. [UC-06 — Switch Staging from Development to Release](UC-06-switch-staging-development-to-release.md)
7. [UC-07 — Switch Staging between Releases](UC-07-switch-staging-between-releases.md)
8. [UC-08 — Hotfix Release in Staging](UC-08-hotfix-release-staging.md)
9. [UC-09 — Switch Production Release](UC-09-switch-production-release.md)
10. [UC-10 — Production Runtime Diff / Reset to Release](UC-10-production-runtime-reset.md)
11. [UC-11 — Incomplete Release](UC-11-incomplete-release.md)
12. [UC-12 — Git Unavailable / Broken Git State](UC-12-git-unavailable.md)
13. [UC-13 — Plugin Update Package — Future](UC-13-plugin-update-package-future.md)
14. [UC-14 — Review Persistent Operation History](UC-14-operation-history.md)

## Common rules

- Default UI selection before the first Apply:
  - Environment: `Staging`
  - Source: `Development`
- Before the first successful Apply, the server state is `Not initialized`.
- Read-only diff/status operations may be used while the Rust server is running.
- A Runtime change notification is only a prompt to refresh Runtime Diff; it
  does not replace the operator's read of the diff and carries no persistent
  state.
- Operations that change Git state or managed server files require the Rust server to be stopped.
- The operator is responsible for stopping and starting the Rust server.
- Apply/commit/revert/switch operations must produce a detailed operation report.
- `Production` never allows runtime commit.
- A valid Release must exist in both Plugins and Configs repositories.
