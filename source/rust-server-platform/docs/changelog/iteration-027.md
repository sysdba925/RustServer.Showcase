# Iteration 027 — Explicit wipe action wire values

## Goal

Make the published Control wipe action contract unambiguous so clients can
construct semantic wipe plans without relying on C# enum ordinals.

## Added and updated

- `WipeAction` is now published as the string enum `keep`, `delete`, and
  `transform` in Control OpenAPI and the generated API contract.
- Control accepts only those string values for wipe plan actions; numeric enum
  values are rejected to keep runtime behavior aligned with the contract.
- Contract and API tests protect the OpenAPI schema and the numeric-input
  boundary.

## Current capability

Clients can safely send explicit semantic values for the `world`,
`blueprints`, and registered `pluginData` wipe actions.

## Deliberately deferred

- Discovering registered plugin-data wipe targets remains unavailable because
  Control does not yet publish a discovery endpoint.

## Validation

- `dotnet test tests/RustServer.Control.Tests/RustServer.Control.Tests.csproj`
  passed: 138 tests.
- `dotnet build RustServer.Control.sln --no-restore` passed with no warnings
  or errors.
- `dotnet run --project tools/RustServer.Control.ApiContractGenerator --
  --verify` passed.
- `git diff --check` passed for RustServer.Control and the shared changelog
  repository.

## Decisions

- Wipe action values are camel-case strings, matching Control's configured
  JSON enum serialization, rather than implicit numeric enum ordinals.
