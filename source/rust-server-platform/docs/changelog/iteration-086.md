# Iteration 086 — RCON secret access for the Rust container

## Goal

Allow the SteamCMD Rust container to read the canonical RCON secret without
relaxing access for ordinary users or copying secrets into another source.

## Added and updated

- Production Bootstrap provisions an ACL allowing SteamCMD UID 1000 read-only
  access to `/etc/rustserver/rcon-password`; root remains the owner.
- Repeated Bootstrap runs accept only the exact required ACL. The root-owned
  `0600` state is valid before the ACL grant; the resulting ACL-backed mode
  is `0640`, with no ordinary group/other permissions.
- The password content remains unchanged and continues to serve both the
  RCON gRPC credential handoff and Docker Compose file-backed secret.
- Added a bootstrap test for idempotent access and rejection of an overly
  broad ACL.

## Current capability

New Production Bootstrap runs provision the Docker-readable RCON secret
before a game-server Start request. Existing hosts can apply the same
ACL once with `sudo setfacl -m u:1000:r /etc/rustserver/rcon-password`.

## Validation

- Traced the container's read failure to the file-backed Docker secret and
  the Production Bootstrap root-only password permissions.
- Reviewed the ACL provisioning and validation code and added a mock-backed
  regression test.
- Live VM startup and the complete Bootstrap test suite are still unverified.
