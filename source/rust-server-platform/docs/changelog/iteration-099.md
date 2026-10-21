# Iteration 099 — NetworkAccess configurator separation

## Goal

Keep the privileged NetworkAccess Unix-socket host focused solely on transport
while preserving all existing Docker-aware nftables behavior.

## Changes

- Moved container discovery, IPv4 and UDP port validation, nftables rule-batch
  construction, application, and command diagnostics from
  `NetworkAccessHelperHost` to `NftablesNetworkConfigurator`.
- Kept `NetworkAccessHelperHost` as the Unix-socket request/response boundary;
  it now delegates each validated request to the configurator.
- Kept Docker interactions read-only (`ps` and `inspect`).
- Ensured external-command exceptions are logged through `ILogger` and return
  the established NetworkAccess failure response rather than terminating the
  helper.
- Moved the existing configurator behavior tests with the implementation; no
  API, orchestration, storage, port, or socket contract changed.

## Validation

- `dotnet build RustServer.Control.sln --configuration Release
  -p:TreatWarningsAsErrors=true` passed with zero warnings and errors.
- `dotnet test tests/RustServer.Control.Tests/RustServer.Control.Tests.csproj
  --configuration Release --no-build` passed: 145 tests.
- `dotnet run --project tools/RustServer.Control.ApiContractGenerator --
  --verify` passed.
