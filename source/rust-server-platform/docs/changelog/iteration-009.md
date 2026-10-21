# Iteration 009 — Independent Oxide/uMod update operation

## Goal

Add an independently executable Oxide/uMod update operation for the existing Rust server installation without coupling it to the Rust/SteamCMD updater.

## Changed components

- New one-shot Docker Compose job in docker/oxide/
- Alpine-based Oxide updater script that downloads the current Linux package, verifies the archive, and installs it over the mounted Rust server directory
- Development launcher and Control systemd installer configuration for OxideUpdate__OxideComposePath
- Dedicated IOxideUpdater, OxideUpdater, result model, options, queue, operation service, and background worker in RustServer.Control
- OperationType.OxideUpdate and shared server-mutation active-state checks
- POST /api/updates/oxide endpoint and operation lookup support through GET /api/operations/{id}
- Unit and API tests for the updater, operation lifecycle, duplicate requests, server-running protection, and Rust/Oxide/lifecycle conflicts

## Decisions

- Oxide remains independent from IRustServerUpdater and RustServerUpdater; no combined Rust-and-Oxide workflow was introduced.
- The updater is a one-shot docker compose run --rm oxide job. It mounts the existing RUST_SERVER_PATH host directory as /server, does not start Rust, run SteamCMD, or mount plugin/configuration repositories.
- The job uses alpine:3.24, installs only CA certificates, curl, and unzip, downloads the current Linux Oxide release from the official OxideMod release route, validates the archive, and extracts it over /server.
- The read-only updater script remains mode 100644 and is deliberately invoked through /bin/sh /scripts/update-oxide.sh; the Compose command preserves exec after the package-installation step.
- OxideUpdate:OxideComposePath is the dedicated Control configuration section. Bootstrap and the development launcher set it to docker/oxide/compose.yml; no additional .env file or secret is introduced.
- Rust and Oxide updates are explicit operation implementations. The operation store provides the small shared concept of an active server-mutating operation, covering RustUpdate and OxideUpdate.
- Rust/Oxide queue acceptance atomically rejects a request while either update is pending or running. Both workers share IServerMutationLock, so they cannot execute concurrently.
- Start, stop, and restart reject while a server-mutating update is active. The existing Rust update and the new Oxide update both check that Rust is stopped before acceptance and repeat that check inside the mutation lock before modifying the installation.
- Updater results intentionally expose only Succeeded and ExitCode; stdout and stderr are not returned through REST.

## Current capability

POST /api/updates/oxide returns 202 Accepted with an operation ID, Pending status, and an operation location at /api/operations/{id}. The background worker transitions the operation through Pending, Running, and then Succeeded or Failed.

The operation is rejected with 409 Conflict when Rust is running, when a Rust or Oxide update is already pending/running, or when a lifecycle mutation conflicts with an active update. Oxide failures and unexpected exceptions are represented as sanitized failed operations.

## Validation

- dotnet build RustServer.Control.sln --configuration Release --no-restore -p:TreatWarningsAsErrors=true completed with zero warnings and zero errors.
- dotnet test RustServer.Control.sln --configuration Release --no-build --no-restore passed 77 of 77 tests.
- Shell syntax checks passed for the updater, Control development launcher, and Control service installer.
- docker compose -f docker/oxide/compose.yml config passed with RUST_SERVER_PATH set to the established server path.

A real Oxide download/extraction was not run against the local Rust installation. The updater is designed to modify that installation only after the stopped-state protections succeed.

## Out of scope

- Oxide version detection or latest-version REST endpoints
- Rust and Oxide combined updates
- Automatic server stop/start around updates
- Plugin or plugin-configuration deployment
- Rollback, persistent operation storage, monitoring, XSite, Terraform, or workflow engines
- Changes to RustServer.Versions or RustServer.Rcon
- Commits, pushes, tags, or remote publication
