# Iteration 023 — Startup milestone state enum contract

## Goal

Make the startup-progress state vocabulary machine-readable before Web clients
consume the Control API.

## Added and updated

- `GET /api/server/startup` now documents each `milestones` array item in
  OpenAPI as a string enum with exactly `pending`, `reached`, and `skipped`.
- Runtime JSON serialization uses the same lower-case string values, and an API
  test asserts the generated OpenAPI enum.

## Validation

- RustServer.Control solution build completed with no warnings or errors.
- All 119 RustServer.Control tests passed.
- Generated OpenAPI artifacts were regenerated and API-contract verification
  completed successfully.
