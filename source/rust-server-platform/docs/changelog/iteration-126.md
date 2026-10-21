# Iteration 126 — First-install provisioning diagnostics

## Changes

- Updater's verified first-install hook may report a bounded, structured set of fixed non-secret diagnostic identifiers. Executor records the stage, category, reason and exit code in the durable operation error; hook stdout and stderr remain outside the API.
- Catalog owns Host, Database and Media stages and classifies PostgreSQL command failures without leaking command output or configuration. Failed PostgreSQL existence checks now stop provisioning instead of being treated as absent objects.
- Older hooks remain compatible and missing or invalid diagnostics use an unknown fallback. Emergency Operation details uses the existing operation error.

## Boundary

Platform Bootstrap already registers the generic hook and needs no code or configuration change. The Executor continues to run it only for first Install from the protected prepared artifact; Update, Rollback and restart do not provision. No release was published or installation performed.
