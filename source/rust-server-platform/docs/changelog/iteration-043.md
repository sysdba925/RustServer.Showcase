# Iteration 043 — Plugins API contract verification and acceptance coverage

## Goal

Make the rebuilt RustServer.Plugins HTTP contract mechanically verifiable and
prove its principal operator workflows end to end without changing deployment
topology or production feature ownership.

## Added and updated

- RustServer.Plugins now commits `openapi.json`, generated from its running
  Production API by `tools/RustServer.Plugins.ApiContractGenerator`. Its
  `--verify` mode performs a semantic JSON comparison without writing files.
- Contract regression coverage compares committed OpenAPI with the running
  Production API and protects the published endpoint set, mutation response
  codes, string wire schemas, and structured error response schema.
- Acceptance coverage uses temporary Git remotes, Runtime files, and the real
  ASP.NET Core host for first initialization, the Runtime → Git → Apply loop,
  forward/backward Release Apply, and Production Apply/Revert restrictions.

## Current capability

Operators and CI can detect drift between the active Plugins HTTP API and its
committed OpenAPI artifact. The principal PluginManagement workflows are
verified across HTTP, Git workspaces, Baseline persistence, Runtime files, and
operation reports rather than only through isolated handlers.

## Deliberately deferred

- `docs/api-contract.md` remains the human-maintained behavioral HTTP
  specification; it is not replaced by generated Markdown.
- No new deployment configuration, topology value, port, filesystem path,
  service unit, Bootstrap action, or production architecture boundary was
  introduced.
- Stage 21 release-readiness work remains out of scope.

## Validation

- RustServer.Plugins Release build passed with warnings as errors.
- The committed OpenAPI generator `--verify` passed against the Production
  test host.
- RustServer.Plugins Release test suite passed: 45 tests, including three new
  acceptance workflows and existing unsafe remote-source-change coverage.

## Decisions

- OpenAPI is a generated, committed transport artifact; behavioral semantics
  remain owned by the hand-written API contract and domain/operation specs.
- API-contract generation uses deterministic process-local loopback topology
  values only; it does not alter `/etc/rustserver/topology.env` or introduce a
  second topology source of truth.
