# RustServer.Web — Agent Instructions

## Backend API contracts are authoritative

Before changing `src/shared/api/`, API-facing TypeScript types, HTTP requests, mutations, polling, or Overview data loading, read the current relevant backend artifacts below. In the final implementation report, name every contract file used.

### RustServer.Control

- OpenAPI source of truth: `E:\Projects\rust\RustServer.Control\openapi.json`
- Generated semantic reference: `E:\Projects\rust\RustServer.Control\docs\api-contract.md`
- Regenerate (from `E:\Projects\rust\RustServer.Control`): `dotnet run --project tools/RustServer.Control.ApiContractGenerator`
- Validate without writing: `dotnet run --project tools/RustServer.Control.ApiContractGenerator -- --verify`

### RustServer.Versions

- OpenAPI source of truth: `E:\Projects\rust\RustServer.Versions\openapi.json`
- Generated semantic reference: `E:\Projects\rust\RustServer.Versions\docs\api-contract.md`
- Regenerate (from `E:\Projects\rust\RustServer.Versions`): `dotnet run --project tools/RustServer.Versions.ApiContractGenerator`
- Validate without writing: `dotnet run --project tools/RustServer.Versions.ApiContractGenerator -- --verify`

### RustServer.Plugins

- OpenAPI source of truth: `E:\Projects\rust\RustServer.Plugins\openapi.json`
- Generated semantic reference: `E:\Projects\rust\RustServer.Plugins\docs\api-contract.md`
- Regenerate (from `E:\Projects\rust\RustServer.Plugins`): `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator`
- Validate without writing: `dotnet run --project tools/RustServer.Plugins.ApiContractGenerator -- --verify`

### RustServer.PluginCatalog

- Published semantic contract: `E:\Projects\rust\RustServer.PluginCatalog\docs\api-contract.md`
- Development OpenAPI cross-check: `GET http://localhost:5180/openapi/v1.json` after starting the Catalog Development profile. The Catalog repository does not keep a checked-in OpenAPI file at this stage.

Before changing Catalog-facing TypeScript types, requests, mutations, list
loading, or media URL resolution, read the Catalog contract and cross-check
Development OpenAPI when available. `RustServer.PluginCatalog` owns known
plugin catalog metadata, sources, selected icon metadata, and media; it does
not establish installed, deployed, or running state. It is independent of
`RustServer.Plugins`, which owns Git-managed files, releases, deployment, and
runtime-change workflows.

#### Plugins UI semantics

Before implementing or changing Plugins UI, read these sibling `RustServer.Plugins`
sources in this order:

1. `docs\use-cases\README.md` and the relevant `docs\use-cases\UC-*.md`
   files for operator workflows and their action sequences.
2. `docs\architecture.md` and `docs\deployment-workflow.md` for domain
   boundaries, invariants, Environment/Source/Target semantics, and safety
   rules.
3. `docs\api-contract.md` and `openapi.json` for the published HTTP operations
   and DTOs.

Use cases describe the operator outcome; architecture and deployment workflow
define the safety and domain constraints; OpenAPI defines how Web communicates
with the service. Use cases and architecture are not HTTP contracts and never
authorize an endpoint, field, action, or workflow state absent from OpenAPI.

For every backend, use OpenAPI for endpoints, HTTP methods, request and
response bodies, field names and types, nullability, enum/string values, and
status codes. Use the generated Markdown contract for semantics OpenAPI does
not make clear, such as lifecycle or update preconditions, operation lifetime
and limitations, Installed/Running/Available distinctions, frontend-derived
values, and unsupported capabilities. For Plugins, compose those available
HTTP capabilities into the documented operator workflows; do not model the UI
as one endpoint per screen or action.

## Contract boundaries

Do not normally inspect backend `Program.cs`, controllers, application/domain/infrastructure classes, or internal readers/providers to discover frontend behavior. Backend implementation is not the frontend contract. Inspect it only when the published contract is missing, internally contradictory, or the user explicitly requests implementation-level investigation. In those cases, report the contract gap; do not silently make implementation details public API.

Do not invent endpoints, fields, response properties, operation states or progress, cancellation, history, server metadata, plugin/player/log APIs, restart semantics, or error shapes. Treat UI needs absent from the published contract as unsupported/currently unavailable.

For Plugins specifically, the current OpenAPI explicitly provides
`GET /api/environment` and `PUT /api/environment`. The Environment API reads
and changes the persistent logical Plugins Environment. It permits
`Staging + Release → Production` and `Production + Release → Staging`, while
`Staging + Development → Production` returns `409 Conflict`; the operator must
explicitly deploy a Release first. An Environment switch does not deploy,
start, stop, or restart Rust, and does not automatically select a Release.

UI may use only capabilities present in the current OpenAPI. Do not infer
deployment progress, a release-comparison API, a plugin-catalog API,
wipe-target discovery API, or lifecycle orchestration from use cases or
architecture. If such a capability is required, report it as a gap in the
owning backend public contract before adding Web behavior.

When a frontend requirement conflicts with a backend contract, do not patch around it in this repository. Identify the precise mismatch and owning backend contract, stop before adding compatibility behavior, and report that the backend public contract must change first. Update frontend types and clients only after that contract changes.

Do not copy either `openapi.json` or `docs/api-contract.md` into this repository; they remain owned by the sibling backend repositories. Do not add OpenAPI-generated frontend code unless an explicit architectural decision authorizes it.

## Types and UI behavior

API-facing TypeScript types must mirror published OpenAPI exactly, including field names, types, nullability, and enum/string values. Keep frontend-derived concepts—such as `Starting`, `Ready`, `Maintenance`, `restartRequired`, and action availability—separate from API response types unless they become explicit API fields.

The platform UI contract is separate and lives at `E:\Projects\rust\rust-server-platform\docs\ui\`:

```text
backend API contract: what data and actions exist
frontend UI contract: how those capabilities are represented to the operator
```

Neither overrides the other. Report any UI expectation that is not exposed by the backend contract.

### Cross-repository contract alignment

When a public backend contract or use case changes an operator-visible workflow
or UI semantic, review the relevant
`E:\Projects\rust\rust-server-platform\docs\ui\**` documents before Web
implementation for stale assumptions.

```text
Backend OpenAPI and use cases
        ↓
Platform UI contract
        ↓
RustServer.Web implementation
```

Backend OpenAPI remains the source of truth for available capabilities. Backend
use cases remain the source of truth for operator workflow. Platform UI
documents remain the source of truth for representing those capabilities in
Web; they do not override backend contracts or use cases.

## Frontend architecture and Plugins boundaries

`RustServer.Web` remains one deployable React application: a modular frontend /
modular monolith. Do not introduce microfrontends or one React application per
backend microservice without a separate architectural decision.

Organize frontend features around operator capabilities, not mechanically around
backend services. The planned Plugins surface should use a dedicated feature
boundary such as `src/features/plugins/`; keep Plugins-specific UI state,
components, and workflows within that feature where possible. Give Plugins API
integration its own explicit boundary in `src/shared/api/`, rather than mixing
it into Control or Versions clients.

Do not extend `ControlPlaneProvider` with Plugins-specific state merely because
it already exists. Cross-feature or global state belongs there only when there
is real shared UI semantics. A frontend feature may compositionally call more
than one backend API; for example, a future Wipe UI may use Control capabilities
and Plugins metadata. That is frontend composition, not backend-to-backend
coupling.

### Plugins workflow design

Design Plugins UI around the operator use cases, preserving distinct workflows
for Incoming Changes, Runtime Changes, Development deployment, Release
deployment, Release creation, runtime Import, runtime Discard, and Production
Release deployment or rollback. Do not collapse Incoming Changes and Runtime
Changes into one generic diff workflow: they have different sources, safety
rules, and subsequent actions.

An `UpdatePackage` remains one operator operation even when it affects both
managed repositories. Do not use `Plugin.cs` as a frontend deployment boundary:
the backend unit is an `UpdatePackage`, which can contain multiple files and
route selected paths to both managed repositories.

### Control and Plugins ownership

`RustServer.Plugins` owns Git-managed plugin/configuration files, deployments,
releases, and runtime-change workflows. `RustServer.Control` owns Rust
lifecycle and wipe execution. Web may compose their published capabilities,
but must not assume Plugins starts, stops, restarts, or wipes Rust.

For wipe, do not hardcode semantic targets such as `economics`. Supported
targets and actions must come from a published Control capability/discovery
contract when one exists. Plugins metadata may describe a relationship between
plugin and runtime data, but is not the source of truth for supported wipe
actions.

## Shared changelog

Record meaningful product, architecture, contract, or user-visible presentation
changes in the shared changelog at
`E:\Projects\rust\rust-server-platform\docs\changelog`. Do not create a
changelog entry for a small bug fix, routine dependency update, formatting-only
change, or internal refactor with no material behavior change.

`rust-server-platform` is a sibling repository and is used by this frontend
only to store the shared changelog history. Do not modify its platform source,
UI contracts, or other documentation as part of frontend work unless the user
explicitly expands the scope.

For a changelog-worthy change:

1. Determine the latest iteration from the highest numeric
   `iteration-NNN.md` file. Create a new `iteration-NNN.md` using the next
   numeric value for each changelog-worthy change. Do not edit an existing
   iteration unless the user explicitly requests it.
2. Follow `E:\Projects\rust\rust-server-platform\docs\changelog\template.md`.
   If it is unavailable, read at least two existing iteration entries, preserve
   their established style, and add the template file for future work.
3. Describe the goal, material changes, current capability, deliberately
   deferred scope, validation, and relevant decisions. Keep entries factual;
   do not claim unavailable validation or future capability as complete.
