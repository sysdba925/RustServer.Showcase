#!/usr/bin/env bash
# Run a self-contained linux-x64 publish in a transient user unit; no installation.
set -eu
publish=$(realpath "${1:?Pass the directory of a self-contained linux-x64 API publish}")
scratch=$(mktemp -d)
unit="updater-console-test-$$"
cleanup() {
    systemctl --user stop "$unit.service" >/dev/null 2>&1 || true
    rm -rf -- "$scratch"
}
trap cleanup EXIT
cp -a "$publish" "$scratch/api"
chmod +x "$scratch/api/RustServer.Updater.Api"
cat > "$scratch/applications.ini" <<'EOF'
[Defaults]
RepositoryOwner=sysdba925
InstallRoot=/opt/rustserver
HealthScheme=http
HealthHost=127.0.0.1
HealthPath=/health

[Application:RustServer.Updater]
Repository=RustServer.Updater
Directory=updater
Service=rustserver-updater
Entrypoint=RustServer.Updater.Api.dll
HealthPort=8080
EOF
port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')
# No WorkingDirectory is supplied, matching the shipped unit.
systemd-run --user --quiet --collect --unit="$unit" \
    --setenv="ASPNETCORE_URLS=http://127.0.0.1:$port" \
    --setenv="DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1" \
    --setenv="ApplicationRegistry__StoragePath=$scratch/applications.ini" \
    --setenv="Operations__StoragePath=$scratch/operations" \
    --setenv="PackagePreparation__StoragePath=$scratch/staging" \
    --setenv="DeploymentExecutor__RequestStoragePath=$scratch/requests" \
    --setenv="DeploymentExecutor__TransactionStoragePath=$scratch/transactions" \
    "$scratch/api/RustServer.Updater.Api"
base="http://127.0.0.1:$port"
ready=false
for attempt in $(seq 1 100); do
    if curl --silent --fail "$base/emergency" -o "$scratch/index.html"; then ready=true; break; fi
    sleep 0.1
done
if [ "$ready" != true ]; then
    journalctl --user -u "$unit.service" --no-pager -n 50
    exit 1
fi
pid=$(systemctl --user show "$unit.service" --property MainPID --value)
cwd=$(readlink -f "/proc/$pid/cwd")
if [ "$cwd" = "$scratch/api" ]; then
    echo 'systemd unexpectedly started the API in its application directory' >&2
    exit 1
fi
cmp "$scratch/api/wwwroot/console/index.html" "$scratch/index.html"
for asset in console.css console.js rust.ico; do
    curl --silent --show-error --fail "$base/console/assets/$asset" -o "$scratch/$asset"
    cmp "$scratch/api/wwwroot/console/assets/$asset" "$scratch/$asset"
done
echo "Transient Linux systemd unit served the published HTML, CSS, JS and favicon from working directory $cwd."
