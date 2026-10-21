# NetworkAccess

`NetworkAccess` controls only the host reachability of the single Rust Dedicated
Server container. It does not restart Rust and does not recreate the container.

- `GET /api/server/network-access` returns the persisted settings.
- `PUT /api/server/network-access` accepts `publicEnabled` and `maintenancePort`.
- The initial persisted value is `publicEnabled: true` and `maintenancePort: 50015`.
- The maintenance port is an independent host UDP entry point DNATed to the
  running container's game port (`28015/UDP`). It cannot be `28015` or `28017`.

The API returns success only after the privileged Helper has applied a
**fail-closed** firewall transaction, then persists the *desired* settings.
The saved `publicEnabled: true` is not an assertion that the public ports
are presently open. A maintenance port conflict returns HTTP 409; helper
unavailable, unsupported Docker firewall integration, and nftables errors
return 502 with a stable `code` and a safe, actionable `detail`.
Raw command output remains only in the Helper's journal.

## Lifecycle and rule model

**Bootstrap and each boot are closed by default.** The Helper's `ExecStartPre`
installs `inet rsc_network_access` input and forward guards that drop UDP
28015/28017 without needing Docker. The Updater Executor installs a dedicated
`docker.service.d/rustserver-network-access.conf` drop-in with a mandatory
`ExecStartPre` that installs the fail-closed nftables guard before Docker
starts (at boot or any later restart). If the guard fails, Docker startup
fails. Docker does not have a runtime `Requires=` dependency on the Helper:
updating the Helper does not stop Docker or Rust.

**Saving while Docker is stopped is supported.** The Helper configures the
closed host-only nftables table and responds successfully. Its persistent
settings are independent of Docker/container IP. When Docker later runs the
single Compose `rust-server` container, the Helper discovers its current
IPv4 and configures maintenance UDP DNAT to `<container-ip>:28015`. It does
not open the public ports until Rust is ready.

**Public ON is gated by readiness.** Control's periodic reconciliation
requires both `ServerStatus.IsReady` (WebRCON) and the final Startup Tracking
milestone. Until both are true, it passes an effective `Public OFF` request to
the Helper. Managed Start/Stop/Restart additionally applies the guard before
starting, stopping, or replacing the container. A new boot installs the guard
before Docker; unexpected external container lifecycle changes are noticed by
the 15-second reconciler.

The Helper uses a single atomic `nft -f -` transaction for its own
`inet rsc_network_access` table. It drops both locally addressed game UDP
traffic (input hook) and Docker-forwarded traffic (forward hook), including
existing connections by matching conntrack original destination. Maintenance
DNAT is retained when public traffic is disabled, because the maintenance
packet's *original* destination is its separate UDP port.

When Docker is ready, the Helper updates the dedicated
`ip filter RSC_NETWORK_ACCESS` chain reachable through `DOCKER-USER` to
allow maintenance forwarding. If Docker networking is inconsistent, the Helper
leaves the host guard active and returns a diagnostic error instead of opening
public access. The Helper never edits unrelated firewall rules.

## Required host integration

The API process intentionally has `NoNewPrivileges=true` and never invokes
`sudo`. The release contains this file under `network-access/`:

- `systemd/rustserver-network-access-helper.service`, the starter unit for the
  root helper process.
- `nftables/rustserver-network-access-guard.nft`, a fail-closed boot guard
  installed and executed only by that helper unit before Docker may publish the
  game ports.

The installation responsibilities are split as follows:

1. **Bootstrap** installs `nftables`, `iproute2` (`ss`), `kmod`, and the
   `nf_tables` kernel module, creates the unprivileged Control account and
   group, and provisions the reviewed `rustserver-control.service` unit.
   Bootstrap does **not** install or enable the release-owned Helper unit.
2. **Updater Executor (root)** validates the Helper unit and guard in the
   verified Control release, registers the stable
   `rustserver-network-access-helper.service` symlink under
   `/etc/systemd/system`, and runs `systemctl daemon-reload` after Control
   release activation. The normal Control systemd dependency starts the Helper
   first; only after Control readiness does Updater enable both units for boot.
   The Executor also installs Docker's fixed firewall-guard drop-in. On a
   failed first Control installation it removes both registrations; on update
   failure it restores the previous release and keeps the dependency intact.
3. **systemd** runs the Helper as root using the unit's narrowly scoped
   capabilities. Its `ExecStartPre` loads the release-owned fail-closed
   `nftables` guard before Docker starts; `ExecStopPost` restores it when
   the Helper stops. It creates the socket
   directory as `root:rustserver-control` with mode `0750`; the
   `UMask=0007` exposes the socket only to the dedicated group.
4. **Control API** remains non-root and does not install or launch privileged
   processes itself. It connects to the Helper socket at
   `/run/rustserver-network-access/network-access-helper.sock` to request
   bounded rule changes. The helper discovers a single running Compose
   `rust-server` container/IPv4 address, rejects conflicting ports and
   unsupported Docker `DOCKER-USER` firewall layouts, and fails closed.
   Control reconciles persisted settings at startup and every 15 seconds.

No Bootstrap script contains NetworkAccess rule logic. The Helper is part of
the Control release (not a separately versioned application); root-only
service installation belongs to the Updater Executor, and process startup
belongs to systemd.
