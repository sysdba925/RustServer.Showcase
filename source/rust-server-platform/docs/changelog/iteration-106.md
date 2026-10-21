# Iteration 106 — Fail-closed NetworkAccess without Docker

## Goal

Preserve configured public reachability independently of Docker while guaranteeing
the two public UDP ports remain closed until Rust has finished starting.

## Implementation

- RustServer.Control saves the desired NetworkAccess state only after a
  successfully installed closed nftables guard. A stopped Docker daemon or an
  absent Rust container no longer prevents saving settings.
- The Helper configures host input/forward guards independently of Docker.
  After one Rust container becomes available it also configures maintenance
  UDP DNAT, without opening the public ports before Rust readiness.
- The periodic Control reconciler permits Public ON only after RCON readiness
  and the final startup milestone. Managed Start/Stop/Restart close public
  access before container lifecycle mutations.
- The Helper guard is installed on startup and after the Helper stops. The
  Updater Executor installs a Docker systemd drop-in which applies the
  fail-closed nftables guard in Docker's ExecStartPre; Docker startup fails if
  the guard fails, without stopping Docker on Helper updates.
  Failed first installation removes the managed Docker drop-in.
- The Control API returns safe, structured failure codes and useful error
  descriptions. The Web panel displays the code and explains deferred opening.

## Validation

- Added offline-Docker, missing-container, DNAT, firewall rejection, and
  desired-versus-effective-mode tests in Control.
- Extended Updater Executor integration tests for Docker drop-in installation
  and first-install rollback.
- Added a Web test for structured NetworkAccess error display.
- .NET integration suite, UI tests, and actual Linux nftables/systemd boot
  validation remain to be run before deploying this change to Production.

## Decisions

- Bootstrap provisions prerequisites; Updater Executor owns Helper deployment
  and its Docker ordering; systemd owns process activation; Control owns the
  desired mode and readiness-based effective access.
- No Docker container restart is required solely to change NetworkAccess.
