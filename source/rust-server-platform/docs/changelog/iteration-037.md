# Iteration 037 — Configure HTTP bind hosts from topology

## Goal

Make each HTTP listener's IPv4 bind host independently configurable from the
central runtime topology without changing service connection URLs or ports.

## Added and updated

- Added the five `RUSTSERVER_*_BIND_HOST` development defaults to the versioned
  topology template.
- Made Control, Versions, Plugins, Snapshots, and Vite require and validate
  their own canonical IPv4 bind-host setting while continuing to obtain their
  ports from the existing service URLs.
- Extended topology parsing and Bootstrap step 35 to validate the new keys and
  append only missing defaults to an existing runtime topology file.
- Documented the future Web-only listener configuration and the required
  service restart workflow.

## Current capability

An operator can change a service's listener from all interfaces to loopback,
or back again, by editing its bind-host key in
`/etc/rustserver/topology.env` and restarting that service. Connection URLs,
ports, service-to-service communication, and WebRCON remain unchanged.

## Deliberately deferred

- Public reverse proxy, authentication, HTTPS, and firewall configuration are
  not implemented.
- WebRCON remains a separate loopback-only Docker-published endpoint and is
  not controlled by HTTP bind-host settings.

## Validation

- All affected .NET solutions built successfully; Control (138), Versions
  (42), Plugins (24), and Snapshots (3) tests passed.
- Control, Versions, and Snapshots API-contract verification passed. Web
  lint, typecheck, test (180), and build passed, with the two existing React
  lint warnings and existing bundle-size warning still reported.
- An isolated Bootstrap migration preserved a customized Control URL, added
  each missing bind-host key exactly once, and made no change on its second
  run.
- Without rebuilding, all five services were verified on all interfaces in
  development configuration, then verified with four loopback-only backends
  and network-reachable Web in the documented future configuration. Vite
  proxy routes worked in both runs.
- A production VPS deployment and a live Ubuntu systemd restart were not run.

## Decisions

- Bind hosts are canonical IPv4 addresses. This keeps listener configuration
  explicit and prevents a topology value from changing URL syntax or ports.
- The existing complete URLs remain connection addresses and the sole source
  of service ports; no duplicate port setting was introduced.
