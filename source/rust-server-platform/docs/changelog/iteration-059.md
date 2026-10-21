# Iteration 059 — Control RCON gRPC migration

## Goal

Move RustServer.Control from its direct WebRCON client dependency to the
standalone RustServer.Rcon gRPC service.

## Added and updated

- Control now generates a gRPC client from the shared `rcon.proto` contract
  and sends console commands through `RUSTSERVER_RCON_GRPC_URL`.
- Docker lifecycle readiness uses the service's `GetConnectionStatus` RPC;
  graceful shutdown and the Development-only command endpoint use
  `ExecuteCommand`.
- Control no longer reads an RCON password or configures a WebRCON client.
  It retains `RUSTSERVER_RCON_URL` only to preserve Docker Compose's existing
  host-port mapping for the Rust container.
- Existing command error translation, cancellation behavior, readiness
  timeout, graceful-shutdown polling, and Docker Compose fallback are
  preserved.

## Current capability

Control owns Docker Compose lifecycle operations while the standalone service
owns the WebRCON connection and credentials. A failed gRPC command remains a
sanitized Control API `502` response, and lifecycle operations fall back to
Docker Compose when graceful RCON shutdown cannot be completed.

## Publishing update

- RustServer.Rcon now publishes the framework-dependent Linux `RustServer.Rcon.Grpc`
  executable as GitHub Release archives, with the shared RCON library included as
  a deployment dependency rather than as a NuGet release artifact.

## Integration note

The existing Control bootstrap script still validates a `RustServer.Rcon`
package reference and injects the RCON password into Control. It must be
updated in the platform repository as a separate coordinated deployment
change; it was intentionally not changed by this migration.

## Publishing

- Control now has Development and Production publish profiles and GitHub
  release automation for framework-dependent `linux-x64` archives, checksums,
  non-interactive GitHub CLI authentication checks, and two-release retention
  per channel.
