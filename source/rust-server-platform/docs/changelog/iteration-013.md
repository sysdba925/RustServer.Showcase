# Iteration 013 — UI documentation contract

## Goal

Establish and correct the initial documentation contract for the future
`RustServer.Web` control-plane UI without implementing the frontend.

## Documentation added and updated

- `README.md` retains the `rust-server-platform` bootstrap documentation and
  points to the UI design contract. It identifies `RustServer.Web` as a
  future separate frontend component/repository.
- `docs/ui/prototypes/01-overview.md` preserves Prototype #1 as the selected
  visual direction: Server, Rust and Oxide software cards, then current
  operation/activity context.
- `docs/ui/information-architecture.md` defines current product areas,
  future extension points, and the rule that navigation exposes only
  implemented destinations.
- `docs/ui/states.md` defines independent Loading, Available, and
  Unavailable request states; derived server states; operation states; and
  lifecycle/update action availability.
- `docs/ui/api-mapping.md` maps the current Control and Versions API to UI
  concepts using SOURCE, DERIVED, and FUTURE categories.
- `docs/ui/screens/overview.md` defines the contract for the first screen at
  route `/`.

## Decisions

- `rust-server-platform` remains responsible for platform deployment,
  orchestration, and documentation. It is not the `RustServer.Web`
  repository.
- Prototype #1 is the selected visual reference, but displayed design concepts
  are not treated as existing backend capabilities without an API source.
- The server state is derived from `isRunning` and `isReady`:
  `false/false` is Stopped, `true/false` is Starting, and `true/true` is
  Ready. The defensive `false/true` combination is presented as
  Inconsistent / Unavailable.
- During Starting, Stop is available so an operator can abort a slow or
  unhealthy startup. Start, Restart, Rust Update, and Oxide Update are
  unavailable. Restart is withheld by first-implementation UX policy, not by a
  backend limitation.
- Rust and Oxide updates require a stopped server, are asynchronous operations,
  and cannot run concurrently with another server-mutating update or lifecycle
  mutation.
- Maintenance is a frontend-derived state for a known Rust or Oxide update in
  Pending or Running status.
- Server, Rust version, and Oxide version requests fail independently. The UI
  does not expose a binary aggregate connectivity/health state.
- Rust installed and available values are Steam Build IDs. Rust `running`
  contains RCON runtime metadata, not a Build ID, so Rust restart-required
  cannot be derived from current contracts.
- Oxide restart-required is derived only when
  `runningVersion != null && installedVersion != runningVersion`. The UI
  reports the observed mismatch without inferring how it occurred.
- Operation records are in-memory and addressable only by known ID. There is
  no operation list/history, cancellation, live output, percentage progress,
  stage model, or compound stop-update-start endpoint.

## Scope

No React application, frontend source code, package configuration, build or
deployment files, Docker files, CI configuration, or frontend architecture
were added.

## Validation

- Documentation was checked against the current `RustServer.Control` and
  `RustServer.Versions` API contracts.
- UI documentation uses SOURCE, DERIVED, and FUTURE consistently where API
  mapping is required.
- README platform/bootstrap content was preserved while adding the UI-design
  pointer.
