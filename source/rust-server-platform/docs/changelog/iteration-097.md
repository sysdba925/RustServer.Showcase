# Iteration 097 — Control NetworkAccess feature

## Goal

Add a Control-owned API feature for enabling or blocking the public Rust game
and Steam Query UDP ports while retaining a separately configurable maintenance
UDP entry point to the live Docker container.

## Changes

- Added `GET` and `PUT /api/server/network-access` to RustServer.Control. The
  durable defaults are public access enabled and maintenance port `50015`.
- Added feature-local command/query handlers, orchestration, durable JSON
  storage, API adapters, and tests. Settings are persisted only after rules
  apply; a failed application never produces an API success response.
- Added a root-only, group-restricted Unix-socket helper supplied in the
  Control release. It invokes the packaged nftables script; the non-root API
  service does not call `sudo` and remains compatible with
  `NoNewPrivileges=true`.
- The helper obtains the current Docker Compose `rust-server` container IPv4
  address, DNATs the maintenance UDP port to game port `28015`, blocks public
  UDP `28015` and `28017` using conntrack original destination when public
  access is disabled, and permits maintenance traffic through Docker's
  `DOCKER-USER` chain.
- Added the generated OpenAPI contract and human-readable API-contract
  rendering, release content for the helper and starter systemd unit, and host
  integration documentation. Bootstrap contains no NetworkAccess business
  logic.

## Validation

- `dotnet build RustServer.Control.sln --configuration Release
  -p:TreatWarningsAsErrors=true` passed with zero warnings and errors.
- `dotnet test tests/RustServer.Control.Tests/RustServer.Control.Tests.csproj
  --configuration Release --no-build` passed: 143 tests.
- `dotnet run --project tools/RustServer.Control.ApiContractGenerator --
  --verify` passed.
- `bash -n` passed for the packaged nftables helper.
- A Linux-targeted Control publish confirmed both NetworkAccess helper files
  are present in the release output.

## Follow-up

- A separate host-provisioning task must install the supplied root helper
  systemd unit, create its group-restricted runtime socket directory, and keep
  the active Control release tree root-owned and non-writable by the API user.
- The helper requires Linux nftables, Docker's iptables-compatible nft backend
  with an `ip filter DOCKER-USER` chain, and exactly one running Compose
  `rust-server` container with one IPv4 address. It fails explicitly rather
  than applying an incomplete rule set when those prerequisites are absent.
