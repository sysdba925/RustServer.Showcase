# Iteration 098 — NetworkAccess helper refactor

## Goal

Simplify the privileged Control NetworkAccess implementation by moving the
packaged Bash helper logic into the existing C# Unix-socket helper host without
changing the HTTP API or deployment topology.

## Changes

- Removed `network-access-nftables.sh` and its publish content entry.
- Moved Docker container discovery, single-IPv4 validation, host/Docker UDP
  port-conflict checks, Docker firewall validation, and nftables state checks
  into `NetworkAccessHelperHost`.
- Generate the Control-owned nftables batch in C# and submit it once through
  `nft -f -`, preserving atomic application and avoiding changes outside the
  Control-owned table and chain.
- Extended the existing process executor with optional standard input for the
  nftables batch. The helper records external-command diagnostics through
  `ILogger`; its Unix-socket response continues to use stable failure kinds.
- Added tests for successful batch generation and an nftables rejection.
- Updated the NetworkAccess host-integration documentation for the C# helper
  and its reduced command dependencies.

## Validation

- `dotnet build RustServer.Control.sln --configuration Release
  -p:TreatWarningsAsErrors=true` passed with zero warnings and errors.
- `dotnet test tests/RustServer.Control.Tests/RustServer.Control.Tests.csproj
  --configuration Release --no-build` passed: 145 tests.
- `dotnet run --project tools/RustServer.Control.ApiContractGenerator --
  --verify` passed.
- A clean Release publish check confirmed that only the NetworkAccess systemd
  unit remains under `network-access/`; the deleted Bash script is absent.
