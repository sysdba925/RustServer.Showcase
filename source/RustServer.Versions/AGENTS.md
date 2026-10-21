# RustServer.Versions — Agent Instructions

Before starting a task in this repository, read this file completely.

## Repository responsibilities

This service owns the Versions API.

Public API artifacts:

- `openapi.json`
- `docs/api-contract.md`

The running service exposes the generated contract at `GET /openapi.json`.
Swagger UI is available in the Development environment at `/swagger`.

The committed artifacts are generated from the running ASP.NET API. Update them with:

```powershell
dotnet run --project tools/RustServer.Versions.ApiContractGenerator
```

Verify them without writing files with:

```powershell
dotnet run --project tools/RustServer.Versions.ApiContractGenerator -- --verify
```

Tests are in `tests/RustServer.Versions.Tests`.

The shared project changelog is in `../rust-server-platform/docs/changelog/`. Determine the current iteration from the highest existing `iteration-NNN.md` file; do not create an iteration automatically.

## Definition of Done

A task is not complete until all applicable checks below have been performed.

1. Implement the requested change while preserving existing behavior unless the task explicitly changes it.
2. Run the complete `RustServer.Versions.Tests` suite. Add or update tests whenever behavior changes. Do not modify tests only to conceal a regression.
3. For every HTTP API change, verify routes, methods, request and response models, status codes, field names, types, and nullability. The ASP.NET implementation, tests, `openapi.json`, and `docs/api-contract.md` must describe the same contract.
4. Regenerate the contract artifacts after a public API change, then run the generator with `--verify`.
5. For a meaningful implementation change, update the highest existing iteration changelog without duplicating an entry.
6. Before reporting completion, build the solution, run all tests, verify the contract artifacts, and report any remaining problem.

`openapi.json` is the machine-readable API contract. `docs/api-contract.md` is a generated human-readable rendering of that same contract; neither file is edited by hand.
