# RustServer.Snapshots — Agent Instructions

Before starting a task in this repository, read this file completely.

## Architectural source of truth

[`docs/snapshot-design.md`](docs/snapshot-design.md) defines this service's
responsibilities and integration boundaries. Do not introduce snapshot storage,
server lifecycle control, RCON integration, scheduling, or retention behavior
unless that document and the task explicitly require it.

## Public API contract

The committed `openapi.json` and `docs/api-contract.md` are generated from the
production ASP.NET API. Do not edit either artifact manually. Whenever public
HTTP API behavior changes, regenerate them with:

```powershell
dotnet run --project tools/RustServer.Snapshots.ApiContractGenerator
```

Verify without writing files with:

```powershell
dotnet run --project tools/RustServer.Snapshots.ApiContractGenerator -- --verify
```

Implementation, tests, `openapi.json`, and `docs/api-contract.md` must remain
consistent.

## Definition of done

Before reporting completion, build the solution, run the full test suite, and
verify the generated contract artifacts. Keep changes focused, tested, and
within the documented service boundary.
