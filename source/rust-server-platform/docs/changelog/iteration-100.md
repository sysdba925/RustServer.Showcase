# Iteration 100 — Production NetworkAccess host integration

## Goal

Prepare Ubuntu 24.04 Production hosts to run the NetworkAccess helper supplied
by a RustServer.Control release without granting the Control API root access or
changing firewall policy outside the helper's game-port rules.

## Added and updated

- Production Bootstrap installs and validates `nftables`, `iproute2` and
  `kmod` when absent, loads `nf_tables`, and persists that module requirement
  in a root-owned `/etc/modules-load.d` declaration.
- Bootstrap links and enables the NetworkAccess systemd unit from the active
  immutable Control release instead of copying helper code or nftables logic
  into the platform repository.
- The release-owned helper unit installs a fail-closed guard for the two
  public UDP destinations before Docker can publish them; the helper replaces
  that guard only after a successful persisted-mode reconciliation.
- Bootstrap validates that Control release paths are root-owned and not
  writable by group or other users before trusting their helper unit.
- `rustserver-control.service` requires the helper, starts after it, and is
  restarted with Docker. Control reapplies the persisted NetworkAccess settings
  after a Docker restart and then retries reconciliation every 15 seconds,
  covering Docker's container-start race and a recreated container's current
  IP address.
- Production installation guidance now documents helper-boundary checks and a
  real external-UDP validation sequence.

## Current capability

The non-root Control API reaches only the group-restricted Unix socket. The
root helper remains a fixed release-owned process that applies its own
Docker-aware nftables transaction; Bootstrap does not introduce sudo access or
an arbitrary root command interface.

## Deliberately deferred

- Bootstrap does not enable `nftables.service` or modify `/etc/nftables.conf`,
  because those actions could replace an administrator's non-Rust firewall
  policy.
- Docker's native nftables backend and firewall layouts without the
  iptables-compatible `ip filter DOCKER-USER` chain remain unsupported by the
  Control helper.

## Validation

- Added the NetworkAccess Production Bootstrap source-contract check to the
  existing Bootstrap test suite.
- The complete Ubuntu external-UDP scenario requires a real VPS plus a second
  network host and remains to be run there; it is not claimed as verified by
  shell or unit tests alone.

## Decisions

- The helper unit is referenced through the stable
  `/opt/rustserver/control/current` link so its configuration and executable
  are versioned with the Control release, while the systemd enablement link is
  root-owned host infrastructure.
