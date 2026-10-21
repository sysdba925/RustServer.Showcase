# UC-01 — First Start / Not Initialized

## Goal
Prepare RustServer.Plugins for first use and create the first baseline.

## Initial state
- Baseline does not exist.
- Git may be configured but has not been fetched yet.
- Local repositories may not exist (or Bootstrap may have prepared empty directories).
- Rust server folders or managed files may not exist.
- UI defaults to `Staging + Development`.
- Server state is `Not initialized`.

## Flow
0. On Plugins service start, if workspace initialization is enabled, the service checks existing repositories and clones only missing ones; failed cloning leaves a diagnostic state and must not erase operator files.
1. Operator opens Plugins.
2. System shows `Not initialized`.
3. Operator runs `Fetch`.
4. System validates Plugins and Configs Git repositories.
5. System shows current Development state.
6. Operator opens Preview.
7. System shows the initial set of files that will be applied.
8. Operator stops the Rust server if required.
9. Operator confirms Apply.
10. System applies Development files to the Rust server.
11. System creates the first baseline.
12. System returns a detailed operation report.

## Result
- Environment remains `Staging`.
- Source is `Development`.
- Baseline exists.
- Managed server files match Development.
