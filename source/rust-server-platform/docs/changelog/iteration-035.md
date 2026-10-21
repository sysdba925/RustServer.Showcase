# Iteration 035 — Add World snapshots management

## Goal

Expose the published RustServer.Snapshots capability in the existing World
area, while preserving RustServer.Control ownership of server lifecycle.

## Added and updated

- Added a World Snapshots section with newest-first list, local-time created
  labels, human-readable archive sizes, Fast and Consistent create actions,
  restore, and delete.
- Added typed Snapshots API clients for the published snapshot and operation
  endpoints, plus the development `/snapshots-api` proxy configuration.
- Added local operation polling for asynchronous create and restore. Completion
  refreshes the snapshot list; failure and lost in-process operation state are
  shown to the operator.
- Added explicit restore and delete confirmations. Restore communicates that it
  replaces runtime state and requires the server to be stopped.
- Added operator-readable handling for `ServerMustBeStopped` and
  `SnapshotOperationInProgress` conflicts.
- Started the approved RustServer.Plugins rebuild with a .NET 10 solution,
  runnable ASP.NET Core host, test project, and `PluginManagement` DI
  registration point only.
- Updated Plugins bootstrap to run the new `RustServer.Plugins.dll` skeleton
  without obsolete API-contract, storage, or configuration assumptions.
- Added the Stage 02 Plugins application foundation: controller support,
  validated technical OpenAPI configuration, a common API-error DTO, generated
  OpenAPI, Development Swagger UI, and a minimal set of integration tests.
- Kept Bootstrap validation independent from OpenAPI: it confirms that the
  generic Plugins host responds at `/`.
- Added the Stage 03 internal PluginManagement models for Environment, Source,
  Release, repository identity, Baseline, and future operation reports, with
  the Environment/Source compatibility rule centralized in the feature.
- Added Stage 04 Environment management to RustServer.Plugins: `GET` and
  `PUT /api/environment`, with a default `staging` first-start response and
  explicit `InvalidEnvironment` and `EnvironmentStateCorrupted` API errors.
- Added atomic, service-owned Environment persistence in
  `environment.json` under `PluginManagement:StateRootPath`; it never selects
  Source or starts Git, Apply, baseline, or runtime workflows.
- Updated Plugins Bootstrap to preserve
  `<WORKSPACE_ROOT>/state/RustServer.Plugins`, verify service-user access, and
  pass it to systemd as `PluginManagement__StateRootPath`.
- Added the Stage 05 internal LocalGit capability for read-only snapshots of
  the existing Plugins and Configs workspaces: repository validity, branch,
  HEAD, clean/dirty state, tracking branch, ahead/behind relation, and local
  diagnostics including unsupported branches.
- Updated Plugins Bootstrap to pass the existing `oxide` and `oxide/plugins`
  workspaces as PluginManagement configuration after verifying service-user
  read and traverse access; it performs no Git synchronization or mutation.
- Added Stage 06 RemoteGit and explicit Plugins Git endpoints:
  `GET /api/git/status` reads only existing local and remote-tracking refs,
  while `POST /api/git/fetch` fetches canonical `origin` for both workspaces.
- Added safe Development fast-forward after a successful Fetch only for a
  clean, behind-only `develop` tracking `origin/develop`; dirty, ahead,
  diverged, detached, and unsupported local states remain untouched.
- Added Stage 07 release discovery: `GET /api/releases` combines only the
  already known `origin/release/*` refs from Plugins and Configs, exposes
  complete and incomplete releases with both commit heads, and never fetches,
  checks out, or otherwise mutates Git.
- Added Stage 08 Baseline persistence: `GET /api/baseline`, an atomic
  `baseline.json` store under the existing Plugins state root, explicit
  `notInitialized` and `BaselineStateCorrupted` behavior, and baseline-aware
  Environment `systemState` (`uninitialized`, `ready`, or `degraded`).
- Added Stage 09 Runtime file access in RustServer.Plugins: explicit
  source-to-`oxide` mappings, binary-safe managed-file reads and writes, and
  rejection of traversal and reparse-point escapes. Environment state now
  degrades when the configured Runtime root is unavailable.
- Updated Plugins Bootstrap to read the pre-existing `RUST_SERVER_PATH` value
  without sourcing `.env`, verify the service account's Runtime access, and
  pass it to systemd as `PluginManagement__Runtime__ServerRootPath`.
- Added Stage 10 `GET /api/runtime/diff`: a binary-safe, read-only comparison
  of Runtime files with the exact Plugins and Configs commit SHA persisted in
  Baseline. It reports added, modified, and deleted managed paths, with
  explicit diagnostics for missing baseline Git objects and Runtime access.
- Added Stage 11 mutation safety for Plugins: one non-blocking exclusive
  guard and a stopped-server precondition obtained from RustServer.Control.
  Fetch now rejects concurrent work, running Rust, or unavailable Control
  status before it changes Git.
- Updated Plugins Bootstrap to depend on `rustserver-control.service` and pass
  `PluginManagement__Control__BaseUrl=http://127.0.0.1:5112`; it does not
  require the Rust game-server container itself to be running.
- Added Stage 12 process-local operation reports and
  `GET /api/operations/{operationId}`. Fetch records actual completed, failed,
  and uncompleted steps, failure context, no-rollback result, and final Git
  snapshots without turning reporting into a workflow engine.

## Current capability

Operators can manage runtime snapshots from World. Fast creation remains
available while Rust is running. The UI disables Consistent creation and
restore when the shared Control status already shows Rust running, but the
Snapshots backend remains authoritative and its conflict responses are shown.

RustServer.Plugins now provides controller infrastructure, a validated
`ApiDocumentation` configuration section, `/openapi.json`, and Swagger UI in
the Development host environment. Its current PluginManagement business scope
is Environment only; Git, baseline, and runtime-file behavior remain absent.

RustServer.Plugins now also exposes the first PluginManagement business
capability: operators can inspect or persistently switch between Staging and
Production. The setting survives a service restart, but it is deliberately
independent from Source selection and all Git and server-file operations.

The service now also has an internal LocalGit capability that reads already
available local workspace and tracking-ref state. It has no HTTP endpoint and
does not fetch, authenticate, discover remotes, or modify Git workspaces.

Operators can now inspect Git readiness and explicitly fetch the canonical
remote for both workspaces. A successful Fetch updates remote-tracking refs;
it does not conceal a partial failure or automatically repair unsafe local
history. Status never initiates network access.

Operators can now inspect known remote-tracking Release branches without
network access. A branch found in just one repository remains visible as an
incomplete diagnostic and is not available to a future Apply workflow.

Operators can now inspect the persisted server baseline. Missing state is a
normal first-start condition; corrupted state is explicit and causes the
Environment system state to become degraded. No existing workflow writes a
baseline yet.

Operators can now inspect the managed Runtime file boundary and its diff from
the saved Baseline. Runtime Diff never substitutes current local or remote
HEAD for Baseline commits, and it does not fetch, check out, or mutate Git.

Operators can now run Fetch only while Rust is stopped and inspect the
resulting Fetch report by its returned operation ID. A report remains available
only until the Plugins service restarts; successful first-repository Fetches
are reported honestly when the second repository fails.

## Deliberately deferred

- The UI does not stop or start Rust, orchestrate Stop → Snapshot → Start,
  schedule snapshots, or configure retention, storage, remote backup, or
  restore orchestration.
- Snapshots remain a World section rather than a separate top-level
  destination or operation-history page.
- Plugins Apply, runtime-file, preview, commit, revert, and source-switch
  workflows remain staged work.
- Baseline writes remain reserved for the future Apply orchestration; no API
  accepts a Baseline write request.
- Runtime mutation, Apply, Revert, source switching, and commit/push remain
  absent; Runtime Diff is inspection only.

## Validation

- `npm run check` passed in RustServer.Web: typecheck, lint, 23 test files /
  180 tests, and production build. Lint retains two pre-existing
  `react(set-state-in-effect)` warnings in `usePluginsResources.ts`.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (1 test). The host started on loopback and returned the expected
  `404` for `/`, confirming that no endpoint has been introduced.
- `bash -n` passed for the updated Plugins build and service bootstrap scripts.
- RustServer.Plugins Release build passed with warnings as errors; its minimal
  test suite passed (3 tests). The Development host returned `404` at `/` and
  `200` from `/openapi.json`.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (6 tests), including the Stage 03 Environment/Source and
  representative Release Baseline invariants.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (8 tests), including first-start and restart-persistence HTTP
  coverage for Environment management. `bash -n` passed for the updated
  Plugins service Bootstrap script.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (10 tests), including clean tracked and dirty real temporary
  Git workspaces for LocalGit. `bash -n` passed for the updated Plugins
  service Bootstrap script.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (12 tests), including status-without-fetch, explicit Fetch,
  safe fast-forward, and dirty-workspace preservation with real temporary Git
  repositories. Bootstrap required no Stage 06 script change because Stage 05
  already supplies both workspace paths.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (16 tests), including complete/incomplete release discovery
  with real temporary Git repositories, missing/corrupted baseline handling,
  restart persistence, and baseline-aware Environment state. Bootstrap
  required no Stage 07/08 script change because it already supplies the
  workspace paths and the writable Plugins state root.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (20 tests), including Runtime mapping and path-escape coverage,
  plus binary added/modified/deleted comparison against a saved Baseline SHA.
  `bash -n` passed for the updated Plugins Bootstrap script.
- RustServer.Plugins Release build passed with warnings as errors; its test
  suite passed (24 tests), including non-blocking mutation exclusion,
  running-server Fetch rejection before Git mutation, and successful and
  partial-failure Fetch reports. `bash -n` passed for the updated Plugins
  Bootstrap script.

## Decisions

- Snapshot operation state is local to the World Snapshots section because the
  published in-process operation API is owned by RustServer.Snapshots and does
  not share Control's operation identity or lifecycle.
- Control status is a convenience preflight only; it never replaces the
  Snapshot service's stopped-server validation.
- Plugins bootstrap verifies a generic host response; OpenAPI remains technical
  tooling rather than an operational readiness dependency. Contract and
  feature-specific readiness checks return when those capabilities exist.
- Stage 03 models are internal feature types only: they add no HTTP endpoint,
  persistent state, Git access, filesystem access, or operation execution.
- Environment persistence is owned directly by PluginManagement rather than a
  legacy storage abstraction. A missing file represents first start; a
  malformed or unsupported persisted value remains diagnostic and is never
  reset automatically.
- LocalGit is a capability-local Git CLI adapter. It reports expected local
  workspace states diagnostically and only observes existing refs; RemoteGit
  and all Git mutations remain separately staged responsibilities.
- `origin` is the canonical PluginManagement remote. Its URL and credentials
  remain owned by the existing local repositories and are never supplied by
  Bootstrap or returned by the API. The shared GitProcess is intentionally a
  small concrete component used by LocalGit and RemoteGit, not a shell
  framework or a separate abstraction layer.
- Release discovery is a read capability over previously fetched
  remote-tracking refs, not a current-server identity or a remote query.
  Baseline is the sole future source of truth for the installed Release.
- Baseline persistence is capability-owned and stored beside Environment
  state. It intentionally duplicates the small atomic-write protocol rather
  than introducing a generic persistence framework.
- Runtime owns only managed physical-file access. Runtime Diff is a separate
  read component that joins Baseline, LocalGit object reads, and Runtime files;
  it does not orchestrate Apply or perform Git mutation.
- Plugins owns only the mutation precondition that Rust is stopped; lifecycle
  state remains owned by RustServer.Control and is read through its HTTP API.
  Operation reports are in-memory observations supplied by command handlers,
  not a queue, scheduler, state machine, or workflow executor.
