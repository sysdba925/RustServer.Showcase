# Rust local VM — clean test flow

This is the repeatable development flow for the local Ubuntu VM.

## 0. Repositories

```text
/home/ubuntu/repos/RustServer.Control
/home/ubuntu/repos/rust-server-platform
```

Rust server files live outside Git:

```text
/srv/rust/server
```

---

## 1. Commit and push changes from Windows

### RustServer.Control

```powershell
cd E:\Projects\rust\RustServer.Control
git status
git add .
git commit -m "feat(server-control): add compose-based Rust server lifecycle"
git push
```

### rust-server-platform

```powershell
cd E:\Projects\rust\rust-server-platform
git status
git add .
git commit -m "feat(rust-server): add compose runner"
git push
```

If there is nothing to commit, skip the commit command.

---

## 2. Pull both repositories on Ubuntu

```bash
cd ~/repos/RustServer.Control
git pull

cd ~/repos/rust-server-platform
git pull
```

---

## 3. Create or migrate persistent configuration

Rust Dedicated Server settings have one active location:

```text
/etc/rustserver/rust-server.env
```

Run Bootstrap to create it from `docker/rust-server/.env.example` or to migrate
an existing `docker/rust-server/.env` safely:

```bash
cd ~/repos/rust-server-platform
./scripts/bootstrap.sh
```

Bootstrap preserves a valid canonical file as authoritative. Once it exists,
Bootstrap does not compare or replace it based on the legacy file. The legacy
file is retained as a recoverable backup after a successful migration. Do not
create or use `docker/steamcmd/.env` or `docker/rust-server/.env` as an active
configuration source.

Check the canonical settings:

```bash
sudo cat /etc/rustserver/rust-server.env
```

For the local VM, `RUST_SERVER_PATH` normally remains:

```text
RUST_SERVER_PATH=/srv/rust/server
```

### RCON development secret

Regular Rust Docker configuration is stored in:

```text
/etc/rustserver/rust-server.env
```

The RCON password is stored separately in:

```text
/etc/rustserver/rcon-password
```

`rcon-password` contains only the raw password—no `KEY=value` prefix. Do not
put a real password in this runbook. Bootstrap creates it when absent, or
copies the legacy checkout password when this canonical file is absent. It
keeps an existing canonical file authoritative and does not compare or
overwrite it from the legacy file. Its permissions are restricted to the
workspace owner and root:

```bash
sudo stat --format '%U:%G %a %n' /etc/rustserver/rcon-password
```

Docker Compose mounts this file read-only as a secret. Both the Rust Docker
container and the RustServer.Control development launcher use this same file.

### Control configuration state

Control stores the desired configuration and the configuration effective for
the currently running Rust process in:

```text
/var/lib/rustserver/control/server-configuration.json
```

Step 85 of Development Bootstrap preserves an existing canonical file. If that
file is absent, it atomically copies an existing
`docker/rust-server/server-configuration.json` without changing its JSON and
retains the legacy file as a recoverable backup. It does not overwrite the
canonical file from legacy data. The Control service owns the canonical file
and atomically replaces it when saving configuration, so do not place it back
inside the checkout.

Check its owner and permissions after Bootstrap:

```bash
sudo stat --format '%U:%G %a %n' /var/lib/rustserver/control/server-configuration.json
```

---

### Installed Docker Compose runtime assets

Step 82 of Development Bootstrap installs the Compose definitions that Control
uses after Bootstrap:

```text
/opt/rustserver/runtime/
├── rust-server/compose.yml
├── rust-server/scripts/start-rust.sh
├── steamcmd/compose.yml
├── steamcmd/scripts/update-rust.sh
├── oxide/compose.yml
└── oxide/scripts/update-oxide.sh
```

They are root-managed runtime copies of the versioned source definitions. The
relative script mounts remain within each installed Compose directory. A
Bootstrap rerun updates an asset only when it differs; it never copies `.env`,
passwords, Control JSON, or `/srv/rust/server` data, and does not restart or
recreate a Rust container merely because it installed these files.

Control receives the absolute installed Compose paths through its systemd unit.
It continues to use `/etc/rustserver/rust-server.env` for Rust settings and
`/etc/rustserver/rcon-password` for the Compose secret. Confirm the installed
layout without starting containers:

```bash
sudo find /opt/rustserver/runtime -type f -printf '%m %u:%g %p\n' | sort
```

---

## 4. Check whether Rust server is already installed

```bash
if [ -x /srv/rust/server/RustDedicated ]; then
  echo "Rust server is INSTALLED"
else
  echo "Rust server is NOT installed"
fi
```

Optional size check:

```bash
du -sh /srv/rust/server
```

For a clean install test, the expected first result right now is:

```text
Rust server is INSTALLED
```

because SteamCMD already installed it earlier.

---

## 5. Clean the existing Rust installation

Use the development reset script:

```bash
cd ~/repos/rust-server-platform
chmod +x scripts/dev/reset-rust-server.sh
./scripts/dev/reset-rust-server.sh --yes
```

The script deletes the CONTENTS of:

```text
/srv/rust/server
```

but keeps the `/srv/rust/server` directory itself.

Verify that RustDedicated is gone:

```bash
if [ -x /srv/rust/server/RustDedicated ]; then
  echo "ERROR: RustDedicated still exists"
else
  echo "OK: clean server directory"
fi
```

Optional:

```bash
ls -la /srv/rust/server
```

---

## 6. Validate Compose configuration

SteamCMD:

```bash
docker compose --env-file /etc/rustserver/rust-server.env \
  -f /opt/rustserver/runtime/steamcmd/compose.yml config
```

Rust runner:

```bash
RUST_SERVER_RCON_PORT="$(awk -F= '$1 == "RUSTSERVER_RCON_URL" { value = $2 } END { sub(/^.*:/, "", value); print value }' /etc/rustserver/topology.env)" \
  docker compose --env-file /etc/rustserver/rust-server.env \
    -f /opt/rustserver/runtime/rust-server/compose.yml config
```

Both commands should finish without errors.

---

## 7. Start RustServer.Control

Open a terminal on the Ubuntu VM and keep it running:

```bash
~/repos/rust-server-platform/scripts/dev/run-control.sh
```

The script automatically obtains the RCON secret and uses the canonical
Control configuration state file. No manual password export is required.

Wait for:

```text
Now listening on the configured Control URL
```

---

## 8. Find the VM IP

In another Ubuntu terminal:

```bash
hostname -I
```

Use the first VM address, for example:

```text
192.168.x.x
```

---

## 9. Open Swagger from Windows

Open in the Windows browser:

```text
<RUSTSERVER_CONTROL_URL>/swagger
```

Example:

```text
http://<configured-host>:<configured-port>/swagger
```

No curl is needed for normal API testing.

---

## 10. Test clean installation through the API

In Swagger execute:

```text
POST /api/server/update
```

Expected compact response:

```json
{
  "succeeded": true,
  "exitCode": 0
}
```

Do not start the Rust server until update completes.

---

## 11. Verify that SteamCMD installed Rust again

On Ubuntu:

```bash
ls -la /srv/rust/server/RustDedicated
```

Or:

```bash
if [ -x /srv/rust/server/RustDedicated ]; then
  echo "OK: Rust server installed"
else
  echo "ERROR: Rust server was not installed"
fi
```

---

## 12. Verify the lifecycle through Swagger

The following sequence is verified end-to-end against the development VM and a
real Rust Dedicated Server:

```text
start → WebRCON command → graceful stop → start again → graceful restart → WebRCON command after restart
```

Run this concise checklist in Swagger:

1. Start RustServer.Control.
2. Open Swagger.
3. Execute `POST /api/server/start`; it returns `{"isRunning":true}`.
4. Wait for Rust bootstrap.
5. Execute `POST /api/server/command` with:

   ```json
   {
     "command": "status"
   }
   ```

   Expect HTTP 200 and a real WebRCON status response with the hostname,
   server version, map, and players.
6. Execute `POST /api/server/stop`.
7. Verify `{"isRunning":false}`. Stop sends `sv quit` through WebRCON; Rust
   shuts down normally and the Docker container exits with code 0.
8. Execute `POST /api/server/start`.
9. After Rust bootstrap, verify the WebRCON `status` command again.
10. Execute `POST /api/server/restart`; it performs a graceful shutdown and
    recreates/restarts the container, returning `{"isRunning":true}`.
11. Wait for Rust bootstrap.
12. Verify the WebRCON `status` command again.

`Docker container running` does **not** mean `Rust fully bootstrapped / WebRCON
ready`. `POST /api/server/start` reports that the container is running; it does
not wait for Rust readiness. Wait for bootstrap before sending WebRCON commands.

---

## 13. Inspect the Rust container when needed

List Compose state:

```bash
RUST_SERVER_RCON_PORT="$(awk -F= '$1 == "RUSTSERVER_RCON_URL" { value = $2 } END { sub(/^.*:/, "", value); print value }' /etc/rustserver/topology.env)" \
  docker compose --env-file /etc/rustserver/rust-server.env \
    -f /opt/rustserver/runtime/rust-server/compose.yml ps
```

Show Rust console output:

```bash
RUST_SERVER_RCON_PORT="$(awk -F= '$1 == "RUSTSERVER_RCON_URL" { value = $2 } END { sub(/^.*:/, "", value); print value }' /etc/rustserver/topology.env)" \
  docker compose --env-file /etc/rustserver/rust-server.env \
    -f /opt/rustserver/runtime/rust-server/compose.yml logs --tail=200 rust-server
```

Follow live output:

```bash
RUST_SERVER_RCON_PORT="$(awk -F= '$1 == "RUSTSERVER_RCON_URL" { value = $2 } END { sub(/^.*:/, "", value); print value }' /etc/rustserver/topology.env)" \
  docker compose --env-file /etc/rustserver/rust-server.env \
    -f /opt/rustserver/runtime/rust-server/compose.yml logs -f rust-server
```

Press `Ctrl+C` to stop following logs. This does NOT stop the Rust server.

---

## 14. Stop Control

In the terminal where `dotnet run` is running:

```text
Ctrl+C
```

This stops only RustServer.Control.

It does not delete `/srv/rust/server`.
