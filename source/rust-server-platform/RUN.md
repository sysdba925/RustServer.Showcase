# Run

> **Configuration status:** Rust settings, the RCON secret, Control state, and
> Compose runtime assets use `/etc/rustserver/rust-server.env`,
> `/etc/rustserver/rcon-password`,
> `/var/lib/rustserver/control/server-configuration.json`, and
> `/opt/rustserver/runtime/`. Bootstrap preserves persistent values and
> installs the versioned Compose/script assets separately.

## First setup / after infrastructure changes

```bash
./scripts/bootstrap.sh
```

Bootstrap prepares the machine, builds and prepares the repositories, and
installs, enables, and starts all six services.

Service addresses are owned by `/etc/rustserver/topology.env`. Bootstrap creates
it from [`config/topology.env.example`](config/topology.env.example) only when
it is absent; edit that runtime file to change an address.

To use the configured values in interactive commands, load them with the same
safe parser used by Bootstrap:

```bash
source scripts/lib/topology.sh
load_topology_environment
```

## Services

```text
rustserver-control.service   -> $RUSTSERVER_CONTROL_URL
rustserver-versions.service  -> $RUSTSERVER_VERSIONS_URL
rustserver-plugins.service   -> $RUSTSERVER_PLUGINS_URL
rustserver-snapshots.service -> $RUSTSERVER_SNAPSHOTS_URL
rustserver-web.service       -> $RUSTSERVER_WEB_URL
rustserver-rcon-grpc.service -> $RUSTSERVER_RCON_GRPC_URL
```

`RustServer.Rcon` remains the NuGet library; `RustServer.Rcon.Grpc` is its
independent, localhost-only host. Swagger/OpenAPI uses
`$RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL`.

## Status

```bash
sudo systemctl status rustserver-control --no-pager
sudo systemctl status rustserver-versions --no-pager
sudo systemctl status rustserver-plugins --no-pager
sudo systemctl status rustserver-snapshots --no-pager
sudo systemctl status rustserver-web --no-pager
sudo systemctl status rustserver-rcon-grpc --no-pager
```

## Start

```bash
sudo systemctl start rustserver-control
sudo systemctl start rustserver-versions
sudo systemctl start rustserver-plugins
sudo systemctl start rustserver-snapshots
sudo systemctl start rustserver-web
sudo systemctl start rustserver-rcon-grpc
```

## Stop

```bash
sudo systemctl stop rustserver-control
sudo systemctl stop rustserver-versions
sudo systemctl stop rustserver-plugins
sudo systemctl stop rustserver-snapshots
sudo systemctl stop rustserver-web
sudo systemctl stop rustserver-rcon-grpc
```

## Restart

```bash
sudo systemctl restart rustserver-control
sudo systemctl restart rustserver-versions
sudo systemctl restart rustserver-plugins
sudo systemctl restart rustserver-snapshots
sudo systemctl restart rustserver-web
sudo systemctl restart rustserver-rcon-grpc
```

## Logs

```bash
journalctl -u rustserver-control --no-pager -n 50
journalctl -u rustserver-versions --no-pager -n 50
journalctl -u rustserver-plugins --no-pager -n 50
journalctl -u rustserver-snapshots --no-pager -n 50
journalctl -u rustserver-web --no-pager -n 50
journalctl -u rustserver-rcon-grpc --no-pager -n 50
```

## API / Web

```text
Control:           $RUSTSERVER_CONTROL_URL
Versions Swagger:  $RUSTSERVER_VERSIONS_URL/swagger
Versions API:      $RUSTSERVER_VERSIONS_URL/api/versions/rust
Plugins Swagger:   $RUSTSERVER_PLUGINS_URL/swagger
Plugins API:       $RUSTSERVER_PLUGINS_URL/api/environment
Snapshots Swagger: $RUSTSERVER_SNAPSHOTS_URL/swagger
Snapshots API:     $RUSTSERVER_SNAPSHOTS_URL/api/snapshots
Web:               $RUSTSERVER_WEB_URL
RCON gRPC:         $RUSTSERVER_RCON_GRPC_URL
RCON Swagger:      $RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL/swagger
RCON OpenAPI:      $RUSTSERVER_RCON_GRPC_DOCUMENTATION_URL/openapi.json
```

## Control Rust server

```bash
curl "$RUSTSERVER_CONTROL_URL/api/server/status"
curl "$RUSTSERVER_CONTROL_URL/api/server/configuration"
curl -X PUT "$RUSTSERVER_CONTROL_URL/api/server/configuration" \
  -H 'Content-Type: application/json' \
  -d '{"hostname":"Rust Server","description":"","maxPlayers":50,"tickRate":30,"saveInterval":600,"seed":0,"worldSize":3500}'
curl -X POST "$RUSTSERVER_CONTROL_URL/api/server/start"
curl -X POST "$RUSTSERVER_CONTROL_URL/api/server/stop"
curl -X POST "$RUSTSERVER_CONTROL_URL/api/server/restart"
curl -i -X POST "$RUSTSERVER_CONTROL_URL/api/server/update"
curl -i -X POST "$RUSTSERVER_CONTROL_URL/api/updates/oxide"
curl -i -X POST "$RUSTSERVER_CONTROL_URL/api/server/wipe" \
  -H 'Content-Type: application/json' \
  -d '{"world":"delete","blueprints":"keep"}'
curl "$RUSTSERVER_CONTROL_URL/api/operations/<operation-id>"
```

`/api/server/update` and `/api/updates/oxide` are asynchronous: each returns an operation ID. Use `/api/operations/<operation-id>` to check progress. Stop the server before starting either update. Rust is not started manually with Docker Compose; `RustServer.Control` owns its lifecycle through this API and uses the installed definitions under `/opt/rustserver/runtime/`.

Configuration is desired/effective: updating it does not restart Rust, and the
effective value changes only after Control successfully creates a new Rust
process. Wipe is also asynchronous and requires Rust to be stopped; its
explicit plan independently keeps or deletes world and blueprint state. It
does not create a snapshot, update software, or start/restart the server.

Control persists this desired/effective state at
`/var/lib/rustserver/control/server-configuration.json`. Development Bootstrap
preserves that canonical file when it exists; otherwise it migrates the legacy
`docker/rust-server/server-configuration.json` without overwriting canonical
data. The legacy file remains a recoverable backup.

## Changing topology

Edit `/etc/rustserver/topology.env`, then restart each affected service. For
example, after changing `RUSTSERVER_CONTROL_URL`, restart Control and every
consumer: Plugins, Snapshots, and Web. The .NET services read the file through
their systemd unit at process start; no rebuild is needed.

Each `RUSTSERVER_*_BIND_HOST` controls only its owning HTTP listener. To change
which service is reachable from the network, edit that key and restart only
the owning service. The temporary development defaults bind all five to
`0.0.0.0`. For Web-only public exposure, use `127.0.0.1` for the five backend
bind hosts and leave `RUSTSERVER_WEB_BIND_HOST=0.0.0.0`; WebRCON remains
loopback-only and is unaffected. RCON gRPC and its documentation endpoint have
no bind-host setting: both topology URLs must remain localhost addresses, and
the host starts independently of the Rust Dedicated Server.

For a direct command, use the same parser/exporter as Bootstrap:

```bash
./scripts/dev/with-topology.sh dotnet run --no-launch-profile --project ../repos/RustServer.Versions/src/RustServer.Versions.Api

# Published output uses the same wrapper.
./scripts/dev/with-topology.sh dotnet /opt/rustserver/RustServer.Versions.Api.dll
```

A prebuilt deployment needs no source repository or Bootstrap process at
runtime: use the same systemd service shape with
`EnvironmentFile=/etc/rustserver/topology.env`, point `WorkingDirectory` and
`ExecStart` at the published output, then restart the unit after a topology
change.
