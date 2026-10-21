# Iteration 029 — Plugins frontend architecture boundary

## Goal

Prepare RustServer.Web for a future RustServer.Plugins operator surface by
documenting its authoritative sibling contracts, workflow semantics, and
frontend ownership boundaries without shipping Plugins UI behavior.

## Added and updated

- Web agent instructions now identify the RustServer.Plugins OpenAPI and
  generated API-contract sources, including the verified generator commands.
- Plugins UI work is required to consult operator use cases first, then the
  architecture and deployment workflow, and finally the HTTP contract; only
  OpenAPI may define available HTTP operations and DTOs.
- The instructions preserve separate Incoming Changes, Runtime Changes,
  development/release deployment, release creation, runtime import/discard,
  and production release deployment/rollback workflows.
- Web remains a modular React monolith. A future Plugins capability has an
  explicit feature and API boundary and does not expand ControlPlaneProvider
  without genuine shared UI semantics.
- Control retains lifecycle and wipe ownership. Plugins metadata cannot define
  supported wipe targets or actions.

## Current capability

Future frontend agents can identify the correct Plugins sources and design
against published capabilities without treating use cases as an API contract or
inventing Plugins UI behavior.

## Deliberately deferred

- Plugins routes, components, clients, DTOs, and runtime state.
- Environment mutation, deployment progress, release comparison, plugin
  catalog, wipe-target discovery, and lifecycle orchestration until their
  owning backend contracts publish them.

## Validation

- Reviewed all published RustServer.Plugins contract, architecture, deployment,
  and use-case documents referenced by the new instructions.
- Confirmed the actual Plugins contract generator and non-writing verification
  commands from the sibling repository instructions and tool project.
- Confirmed the published OpenAPI operations do not expose the deferred
  capabilities listed above.

## Decisions

- Plugins UI is workflow-oriented, rather than an endpoint-to-screen mapping.
- Web may compose independently published Control and Plugins capabilities;
  that composition does not create backend coupling.
