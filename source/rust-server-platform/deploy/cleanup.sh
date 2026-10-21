#!/usr/bin/env bash
# Remove the RustServer deployment from an Ubuntu test machine.
# Keeps the VM, Ubuntu packages, SSH, .NET, Git and the Docker engine installed.
# WARNING: removes ALL Docker containers, images, volumes and build cache.
set -Eeuo pipefail
shopt -s nullglob
export SYSTEMD_PAGER=cat

if (( EUID != 0 )); then
  echo 'Run as root: sudo bash deploy/cleanup.sh' >&2
  exit 1
fi

stage='initialization'
log() { printf '[CLEANUP] %s\n' "$*"; }
fail() { echo "[CLEANUP] ERROR ($stage): $*" >&2; exit 1; }
trap 'status=$?; echo "[CLEANUP] ERROR ($stage): command failed at line $LINENO (exit $status)" >&2' ERR
log 'Starting cleanup.'

# Keep a retry marker until Nginx has accepted the changed configuration.
nginx_marker=/etc/nginx/.rustserver-cleanup-pending
nginx_restore_marker=/etc/nginx/.rustserver-cleanup-restore-default
nginx_files=(/etc/nginx/sites-enabled/*rustserver* /etc/nginx/sites-available/*rustserver* /etc/nginx/conf.d/*rustserver* /etc/nginx/conf.d/.rustserver*)
if ((${#nginx_files[@]})); then
  touch -- "$nginx_marker"
  if [[ -e /etc/nginx/conf.d/rustserver-web.conf && ! -e /etc/nginx/sites-enabled/default && ! -L /etc/nginx/sites-enabled/default && -f /etc/nginx/sites-available/default ]]; then
    touch -- "$nginx_restore_marker"
  fi
fi

stage='systemd services'
log 'Listing installed RustServer units...'
log "Listing installed units ($stage)..."
units_text=$(systemctl list-unit-files --all --no-legend --plain --no-pager)
mapfile -t units < <(awk '$1 ~ /^(rustserver|rust-server)/ { print $1 }' <<< "$units_text")
log "Found ${#units[@]} installed RustServer units."
for unit in "${units[@]}"; do
  log "Disabling and stopping $unit..."
  systemctl disable --now "$unit"
done
log "Listing loaded units ($stage)..."
loaded_text=$(systemctl list-units --all --no-legend --plain --no-pager)
mapfile -t loaded_units < <(awk '$1 ~ /^(rustserver|rust-server)/ && $3 == "active" { print $1 }' <<< "$loaded_text")
log "Found ${#loaded_units[@]} active RustServer units."
for unit in "${loaded_units[@]}"; do
  log "Stopping $unit..."
  systemctl stop "$unit"
done

stage='PostgreSQL'
log 'Discovering local PostgreSQL clusters...'
if command -v pg_lsclusters >/dev/null 2>&1; then
  clusters=$(pg_lsclusters --no-header)
  while read -r version cluster port status _; do
    [[ -n "$version" ]] || continue
    log "Processing PostgreSQL cluster $version/$cluster on port $port (status: $status)..."
    [[ "$status" == online ]] || fail "PostgreSQL cluster $version/$cluster is $status; start it and rerun cleanup"
    [[ "$port" =~ ^[0-9]+$ ]] || fail "Invalid PostgreSQL port for $version/$cluster"
    log "Removing Catalog database from $version/$cluster..."
    runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 -p "$port" -d postgres -c 'DROP DATABASE IF EXISTS rustserver_plugin_catalog;'
    log "Removing Catalog role from $version/$cluster..."
    runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 -p "$port" -d postgres -c 'DROP ROLE IF EXISTS "rustserver-plugin-catalog";'
    log "Verifying PostgreSQL cluster $version/$cluster..."
    remaining=$(runuser -u postgres -- psql -X -A -t -v ON_ERROR_STOP=1 -p "$port" -d postgres -c "SELECT (SELECT count(*) FROM pg_database WHERE datname = 'rustserver_plugin_catalog') + (SELECT count(*) FROM pg_roles WHERE rolname = 'rustserver-plugin-catalog');")
    [[ "$remaining" == 0 ]] || fail "Catalog PostgreSQL objects remain in $version/$cluster"
  done <<< "$clusters"
elif getent passwd postgres >/dev/null; then
  fail 'PostgreSQL cluster discovery is unavailable; install pg_lsclusters and rerun cleanup'
fi

stage='nftables'
log 'Inspecting NetworkAccess firewall rules...'
if command -v nft >/dev/null 2>&1; then
  ruleset=$(nft -a list ruleset)
  if [[ "$ruleset" =~ table[[:space:]]inet[[:space:]]rsc_network_access[[:space:]] ]]; then
    log "Removing inet rsc_network_access table..."
    nft delete table inet rsc_network_access
  fi
  if [[ "$ruleset" =~ chain[[:space:]]DOCKER-USER[[:space:]] ]]; then
    # Inspect only the ip/filter chain; never flush Docker or another firewall.
    if nft list chain ip filter DOCKER-USER >/dev/null 2>&1; then
      docker_rules=$(nft -a list chain ip filter DOCKER-USER)
      while IFS= read -r line; do
        if [[ "$line" =~ jump[[:space:]]RSC_NETWORK_ACCESS([[:space:]]|$) ]]; then
          [[ "$line" =~ \#[[:space:]]handle[[:space:]]([0-9]+) ]] || fail 'Cannot identify NetworkAccess jump handle'
          log "Removing DOCKER-USER jump (handle ${BASH_REMATCH[1]})..."
          nft delete rule ip filter DOCKER-USER handle "${BASH_REMATCH[1]}"
        fi
      done <<< "$docker_rules"
    fi
  fi
  if [[ "$ruleset" =~ chain[[:space:]]RSC_NETWORK_ACCESS[[:space:]] ]]; then
    log "Removing RSC_NETWORK_ACCESS chain..."
    nft flush chain ip filter RSC_NETWORK_ACCESS
    nft delete chain ip filter RSC_NETWORK_ACCESS
  fi
  ruleset=$(nft -a list ruleset)
  [[ ! "$ruleset" =~ table[[:space:]]inet[[:space:]]rsc_network_access[[:space:]] && ! "$ruleset" =~ chain[[:space:]]RSC_NETWORK_ACCESS[[:space:]] && ! "$ruleset" =~ jump[[:space:]]RSC_NETWORK_ACCESS([[:space:]]|$) ]] || fail 'NetworkAccess firewall objects remain'
elif [[ -e /etc/modules-load.d/rustserver-network-access.conf || -e /etc/systemd/system/docker.service.d/rustserver-network-access.conf ]]; then
  fail 'nft is unavailable; cannot verify NetworkAccess firewall cleanup'
fi

stage='host files'
log 'Removing systemd links and host integration files...'
find /etc/systemd/system -type l \( -name 'rustserver*' -o -name 'rust-server*' \) -delete
paths=(/etc/systemd/system/rustserver* /etc/systemd/system/rust-server* /run/systemd/system/rustserver* /run/systemd/system/rust-server*
  /etc/modules-load.d/rustserver-network-access.conf /etc/systemd/system/docker.service.d/rustserver-network-access.conf
  "${nginx_files[@]}" /etc/cron.d/rustserver* /etc/logrotate.d/rustserver* /etc/tmpfiles.d/rustserver* /etc/sysusers.d/rustserver* /etc/sudoers.d/rustserver*
  /usr/local/libexec/rustserver* /usr/local/bin/rustserver*
  /opt/rustserver /etc/rustserver /var/lib/rustserver /srv/rust
  /run/rustserver* /run/rust-server* /run/lock/rustserver* /run/lock/rust-server* /var/log/rustserver* /var/log/rust-server*
  /tmp/rustserver* /tmp/rust-server* /var/tmp/rustserver* /var/tmp/rust-server*)
for path in "${paths[@]}"; do
  [[ ! -e "$path" && ! -L "$path" ]] || log "Removing $path..."
done
rm -rf -- "${paths[@]}"

stage='Nginx'
if [[ -e "$nginx_marker" ]]; then
  log "Applying Nginx configuration changes..."
  restored_default=0
  if [[ -e "$nginx_restore_marker" && ! -e /etc/nginx/sites-enabled/default && ! -L /etc/nginx/sites-enabled/default ]]; then
    [[ -f /etc/nginx/sites-available/default ]] || fail 'The default Nginx site is unavailable'
    log "Restoring the default Nginx site..."
    ln -s /etc/nginx/sites-available/default /etc/nginx/sites-enabled/default
    restored_default=1
  fi
  command -v nginx >/dev/null 2>&1 || fail 'Nginx is unavailable; cannot validate its configuration'
  log "Validating Nginx configuration..."
  if ! nginx -t; then
    if (( restored_default )); then
      rm -- /etc/nginx/sites-enabled/default
    fi
    fail 'Nginx configuration is invalid; fix it and rerun cleanup'
  fi
  if systemctl is-active --quiet nginx.service; then
    log "Reloading Nginx..."
    systemctl reload nginx.service
  fi
  rm -f -- "$nginx_marker" "$nginx_restore_marker"
fi

stage='service accounts'
log 'Removing RustServer service accounts and groups...'
accounts=$(getent passwd)
while IFS=: read -r username _; do
  if [[ "$username" == rustserver* || "$username" == rust-server* ]]; then
    log "Removing user $username..."
    userdel "$username"
  fi
done <<< "$accounts"
groups=$(getent group)
while IFS=: read -r group _; do
  if [[ "$group" == rustserver* || "$group" == rust-server* ]] && getent group "$group" >/dev/null; then
    log "Removing group $group..."
    groupdel "$group"
  fi
done <<< "$groups"

stage='Docker'
log 'Removing ALL Docker workloads and persistent Docker volumes...'
if command -v docker >/dev/null 2>&1; then
  if ! docker info >/dev/null 2>&1; then
    echo '[CLEANUP] ERROR: Docker is installed but its daemon is unavailable; Docker data was NOT removed.' >&2
    exit 1
  fi
  log "Removing Docker containers..."
  docker ps -aq | xargs -r docker rm -f
  log "Removing Docker volumes..."
  docker volume ls -q | xargs -r docker volume rm -f
  log "Pruning Docker system data..."
  docker system prune -af --volumes
  log "Pruning Docker build cache..."
  docker builder prune -af
fi

stage='systemd reload'
log 'Reloading systemd configuration...'
systemctl daemon-reload
systemctl reset-failed

stage='verification'
log 'Verifying files, units, accounts and groups...'
for path in "${paths[@]}" "$nginx_marker" "$nginx_restore_marker"; do
  [[ ! -e "$path" && ! -L "$path" ]] || fail "$path remains"
done
log "Listing installed units ($stage)..."
units_text=$(systemctl list-unit-files --all --no-legend --plain --no-pager)
log "Listing loaded units ($stage)..."
loaded_text=$(systemctl list-units --all --no-legend --plain --no-pager)
if awk '$1 ~ /^(rustserver|rust-server)/ {found=1} END {exit !found}' <<< "$units_text" ||
   awk '$1 ~ /^(rustserver|rust-server)/ && $3 == "active" {found=1} END {exit !found}' <<< "$loaded_text"; then
  fail 'RustServer systemd units remain'
fi
accounts=$(getent passwd)
groups=$(getent group)
if awk -F: '$1 ~ /^(rustserver|rust-server)/ {found=1} END {exit !found}' <<< "$accounts" ||
   awk -F: '$1 ~ /^(rustserver|rust-server)/ {found=1} END {exit !found}' <<< "$groups"; then
  fail 'RustServer service accounts or groups remain'
fi

echo '[CLEANUP] DONE: RustServer removed. The Ubuntu VM and system packages are preserved.'
