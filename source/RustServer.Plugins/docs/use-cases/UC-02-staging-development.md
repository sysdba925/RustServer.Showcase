# UC-02 — Work in Staging + Development

## Goal
Use Development as the normal working mode on Staging.

## Preconditions
- Environment is `Staging`.
- Source is `Development`.
- Baseline exists.

## Flow
1. Operator can view Git status.
2. Operator can view runtime diff while the Rust server is running.
3. Read-only actions do not require the server to stop.
4. Before Apply, Commit, Revert, or Source switch, the operator stops the Rust server.
5. If the server is running, the mutating operation is rejected with a clear error.
6. Operator completes the desired action.
7. System returns a detailed result.

## Result
Staging remains the unrestricted working environment for Development.
