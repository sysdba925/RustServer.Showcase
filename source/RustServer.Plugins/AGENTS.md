# RustServer.Plugins — Agent Instructions

Before starting work in this repository, read this file completely.

---

## 1. New implementation

`RustServer.Plugins` is being rebuilt.

The current implementation must follow the new specifications in `docs/`.

Legacy code is preserved under:

```text
old/
```

Do not treat `old/` as the current architecture.

Use it only as an implementation reference when a specific technical piece is useful.

Do not restore old workflows, API shapes, domain concepts, or use cases unless they are explicitly present in the new documentation.

---

## 2. Source of truth

Before implementing or modifying behavior, read the relevant documentation.

Read in this order:

1. `docs/requirements.md`
2. `docs/domain-model.md`
3. `docs/operations.md`
4. relevant files under `docs/use-cases/`
5. `docs/api-contract.md`

Their roles are different:

### `requirements.md`

Defines what the system must support.

### `domain-model.md`

Defines the domain concepts and terminology.

Examples:

- Environment
- Source
- Development
- Release
- Remote Git
- Local Git Workspace
- Baseline
- Runtime Files
- Runtime Diff

### `operations.md`

Defines what the system can do and the rules around each operation.

Examples:

- Fetch / Update Git
- Apply Source Change
- Commit + Push Runtime Changes
- Revert Runtime Changes
- Switch Release

### `docs/use-cases/`

Defines operator workflows.

Use cases are the primary source for understanding how a human uses the system and how UI workflows should be grouped.

### `api-contract.md`

Defines the HTTP boundary between backend and clients.

It must implement the domain and operations; it must not redefine them.

---

## 3. Documentation consistency

Do not silently implement behavior that contradicts the documentation.

If a requested change changes product behavior:

1. update the relevant specification first;
2. update related use cases if the operator workflow changes;
3. update the API contract if the HTTP boundary changes;
4. then implement the code and tests.

If documents conflict, stop and report the conflict instead of guessing.

---

## 4. Legacy code

Everything under `old/` is legacy.

Rules:

- do not modify legacy code unless explicitly requested;
- do not build new code on top of legacy application flows;
- do not copy entire legacy services without reviewing whether their behavior still matches the new specification;
- isolated infrastructure code may be reused when appropriate;
- copied code becomes new code and must satisfy the new tests and documentation.

Potentially reusable legacy ideas may include:

- safe file operations;
- Git process execution;
- file classification;
- diff helpers;
- rollback mechanisms.

Reuse is optional, not preferred by default.

---

## 5. Core architectural rules

The system manages two Git repositories:

```text
Plugins
Configs
```

Always distinguish these four states:

```text
Remote Git
Local Git Workspace
Server Baseline
Runtime Files
```

Do not collapse them into one generic "state".

A diff is a comparison between states. It is not itself a source of truth.

---

## 6. Environment rules

Supported environments:

```text
Staging
Production
```

Supported sources:

```text
Development
Release
```

Allowed combinations:

```text
Staging + Development
Staging + Release
Production + Release
```

Forbidden:

```text
Production + Development
```

Production must never commit runtime changes to Git.

---

## 7. Release rules

Release branches use:

```text
release/<version>
```

Examples:

```text
release/1.0
release/2.0
```

A Release is complete only when the same branch exists in both Plugins and Configs repositories.

Example:

```text
Plugins  release/2.0
Configs  release/2.0
```

A branch present in only one repository is an `Incomplete Release`.

Incomplete Releases may be shown diagnostically but must not be applied.

Hotfixes are committed directly into the current release branch.

Do not create additional hotfix branches unless the specification changes.

---

## 8. Git rules

Git remote state is not assumed to be current forever.

Read-only views may use the last known fetched state.

Before any operation that mutates Git or applies Git state, perform a fresh remote validation/fetch as required by `operations.md` and `api-contract.md`.

Examples:

- Apply Source
- Commit + Push
- Switch Release
- Rollback Release

Do not allow hidden server-side commits to remain unpushed during normal operator flow.

For the user, runtime commit and push are one operation.

If Git is unavailable, diverged, unauthenticated, incorrectly configured, or otherwise unsafe:

- show diagnostic state;
- block mutating operations;
- do not guess how to repair Git.

---

## 9. Baseline rules

Baseline describes the last state successfully applied to the Rust server.

It is persistent.

Missing baseline on first start is a normal state:

```text
Not initialized
```

It is not an exceptional failure.

The first successful Apply creates the baseline.

A broken or unreadable baseline is different from a missing baseline and must be reported clearly.

---

## 10. Server lifecycle rules

Read-only operations may run while the Rust server is running.

Examples:

- status;
- Git status;
- Release list;
- Runtime Diff;
- Preview.

All PluginManagement Git and managed Oxide Runtime operations may execute while Rust is running: Fetch, Git Sync, Commit + Push, Apply, Revert, Source/Release switch and rollback. These operations manage Oxide plugin/config/data files, not the Rust process lifecycle. **Do not require Rust stopped or require a Control server-status check for these commands.** The operator accepts the effects of modifying live plugin files; the service does not attempt to predict plugin reload behavior or guarantee that an application change takes effect immediately.

Keep the exclusive mutation guard, Git source/baseline invariants, explicit confirmation, filesystem safety, rollback and the Staging/Production restrictions. Do not add automatic Rust stop/restart or plugin reload calls.

---

## 11. Apply rules

Apply means:

> Make the managed files on the Rust server exactly match the selected Source.

It is not a simple copy-over.

Apply must handle:

- added files;
- modified files;
- deleted files;
- moved/renamed files, at minimum as delete + add;
- obsolete managed files.

A Source switch must be treated as one coherent operator operation:

```text
Select
→ Preview
→ Confirm
→ Fresh Git validation
→ Checkout/update
→ Apply
→ Baseline update
→ Detailed report
```

Do not expose a normal workflow where checkout completes but the server is intentionally left on an unrelated baseline.

---

## 12. Operation reports

Mutating operations must return detailed reports.

A response such as:

```text
Failed
```

is insufficient.

Reports should identify, when relevant:

- operation id;
- operation type;
- phase;
- repository;
- source;
- file path;
- added/modified/deleted counts;
- changed files;
- human-readable error;
- machine-readable error code;
- completed steps;
- failed step;
- rollback attempt/result;
- final Git state;
- final baseline state.

Reports must be useful both in UI and when copied into debugging or support discussions.

---

## 13. UI / Web guidance

Do not design UI by mapping one endpoint to one page or button.

Design UI from the use cases.

Prefer visual state over long explanatory text.

Examples:

```text
Plugins  ✓ release/2.0
Configs  ✓ release/2.0
          ↓
       Complete
```

and:

```text
Plugins  ✓ release/3.0
Configs  ✕
          ↓
       Incomplete
```

The UI must clearly distinguish:

- Environment;
- selected Source;
- Git HEAD;
- Server Baseline;
- Runtime Diff.

Do not use generic `Sync` wording when the operation has a specific direction.

Prefer explicit actions such as:

```text
Fetch / Update
Apply to Server
Commit + Push
Revert Runtime Changes
```

---

## 14. Plugin Updater

Plugin Updater is required future scope.

It is documented but does not need to be implemented until explicitly requested.

It belongs to:

```text
Staging + Development
```

Future package inputs may include:

```text
.cs
.zip
other supported archives
```

Do not assume third-party packages have one universal structure.

Ambiguous file destinations must require review instead of silent guessing.

---

## 15. API implementation

Treat `docs/api-contract.md` as the current human-maintained behavioral HTTP
contract. `openapi.json` is the generated, committed transport artifact for
the implemented API; verify it with
`tools/RustServer.Plugins.ApiContractGenerator -- --verify`.

Do not assume the legacy `openapi.json` or legacy API generator under `old/`
is authoritative.

When HTTP behavior changes, update the behavioral contract and generated
artifact consistently. Until a new API decision is documented:

- implement only documented endpoints;
- keep requests/responses/errors consistent with the contract;
- update the contract first when HTTP behavior changes.

---

## 16. Engineering expectations

Keep code focused, testable, and explicit.

Prefer:

- small services with clear responsibilities;
- explicit domain types;
- deterministic operations;
- safe file handling;
- safe Git handling;
- rollback where an operation can partially mutate state;
- tests for success and failure paths.

Avoid:

- hidden state transitions;
- implicit Git mutations;
- broad destructive cleanup;
- silent recovery that hides an unsafe state;
- coupling Web/UI concepts directly into infrastructure.

---

---

## 17. Platform integration

`RustServer.Plugins` is part of the wider `rust-server-platform` project.

For every meaningful implementation or contract change, review two integration points in the sibling `rust-server-platform` repository:

### Shared changelog

The shared changelog lives under:

```text
../rust-server-platform/docs/changelog/
```

When the change is meaningful for product behavior, architecture, public contract, deployment, or operator-visible workflow:

- determine the next/current changelog iteration according to the platform repository convention;
- add the required entry;
- do not create changelog noise for trivial formatting-only or internal refactors.

### Platform Bootstrap

Review the platform Bootstrap when a `RustServer.Plugins` change affects any of the following:

- service installation;
- build/publish output;
- executable or assembly paths;
- configuration files;
- environment variables;
- filesystem layout;
- permissions;
- systemd/service registration;
- startup validation;
- dependency checks;
- deployment validation;
- health/readiness checks.

If Bootstrap changes are required, update the relevant `rust-server-platform` bootstrap steps as part of the same task, or explicitly report the integration work as remaining/blocking scope.

If Bootstrap changes are not required, say so explicitly in the final implementation report.

Do not confuse this with any CSS framework. `Bootstrap` here means the deployment/bootstrap automation in `rust-server-platform`.

---

## 18. Definition of Done

A task is not complete until all applicable checks below have been performed:

1. The implementation matches `requirements.md`, `domain-model.md`, `operations.md`, relevant use cases, and `api-contract.md`.
2. Relevant tests are added or updated and the full applicable test suite is run.
3. Public API changes are reflected consistently in implementation, tests, and API contract artifacts.
4. The shared platform changelog is reviewed and updated when required.
5. The `rust-server-platform` Bootstrap is reviewed and updated when required.
6. The solution is built successfully.
7. Remaining TODOs, unsupported paths, or integration gaps are reported explicitly.

## 19. Architecture (mandatory)

Before creating the new solution/project skeleton or changing application structure, read **`docs/architecture.md`** completely, in addition to the existing source-of-truth documents.

The approved application design is **Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters**, with dependency injection and explicit guards.

- Organize by cohesive **features**, not by endpoint. The initial feature `PluginManagement` owns the full Git/source-to-runtime workflow; later updater/version capabilities may be separate features.
- Inside `PluginManagement`, use `Controllers/`, `Commands/`, `RemoteGit/`, `LocalGit/`, `Baseline/`, `Runtime/`, `Guards/`, and feature registration. Add other directories only when justified by implementation.
- `RemoteGit`, `LocalGit`, `Baseline`, and `Runtime` represent concrete data locations/capabilities; **they are not state-machine states**.
- Command handlers orchestrate documented use cases; capability components operate on their own data and do not secretly coordinate the full workflow.
- Ports are interfaces, adapters their implementations; colocate them with the owning capability instead of enforcing `Ports/` / `Adapters/` folders. Use DI; do not mandate interfaces for every class.
- Guard conflicting mutations and recheck preconditions within the exclusive execution scope. Reject conflicts rather than silently queueing.
- Do not introduce a global `Domain/Application/Infrastructure/Api` project split, MediatR, a state machine, or resurrect `old/` architecture without an explicit documented decision.
- Keep behavioral specifications and the HTTP contract authoritative. If architecture and specs conflict, report the conflict and update documentation **before** writing code.

For meaningful changes, follow the existing `AGENTS.md` rules on shared platform changelog and platform Bootstrap. The Bootstrap here is deployment automation, not CSS.
