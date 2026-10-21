# Iteration 070 — Audit runtime configuration migration dependencies

## Goal

Record the target-contract audit and cross-repository dependencies required to
continue the runtime-configuration migration without changing host behavior.

## Added and updated

- Updated the runtime configuration layout and migration inventory with a
  comparison of both Bootstrap implementations against the flat `/etc/rustserver`
  contract and `/opt/rustserver` runtime-assets target.
- Added a consumer, ownership, and access matrix for configuration files,
  secrets, durable state, and installed runtime assets.
- Documented Snapshots' direct WebRCON dependency and Rcon gRPC's existing
  generic command-execution capability, including the missing authorization
  boundary required before substituting one for the other.
- Recorded the implementation order by repository and the conflicts requiring
  an explicit architecture decision.

## Current capability

Operators and implementers have a source-backed inventory of current readers,
writers, permissions, and coupled path changes. Existing Bootstrap, runtime,
systemd, Docker Compose, applications, and secrets remain unchanged.

## Deliberately deferred

- Any relocation, deletion, or regeneration of configuration and secret files.
- Runtime, Bootstrap, systemd, Docker Compose, or application-code changes.
- Resolution of the omitted `bootstrap.env`, `auth.env`, and Control-state
  contract questions.
- A Snapshots migration from direct WebRCON to Rcon gRPC.

## Validation

- Searched the platform and available sibling repositories for configuration
  path reads, writes, generators, tests, templates, and documentation.
- Verified the two Bootstrap source trees, generated-unit inputs, deployment
  executor, direct RCON/Snapshots sources, and Rcon gRPC protocol/service.
- Confirmed that the requested
  `rustserver-runtime-configuration-migration-decision.md` is unavailable in
  the checkout, available local history/branches, and available siblings.

## Decisions

- No new architecture decision is made by this documentation-only iteration.
- The existing flat-layout document remains the available target contract until
  the missing agreed decision record is restored or supplied.
