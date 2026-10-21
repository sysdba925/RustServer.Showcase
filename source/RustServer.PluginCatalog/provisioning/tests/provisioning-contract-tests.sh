#!/usr/bin/env bash
# Lightweight contract checks. They never invoke provisioning operations.
set -Eeuo pipefail

readonly TEST_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_DIRECTORY="$(cd -- "${TEST_DIRECTORY}/../.." && pwd)"
readonly PROVISIONER="${PROJECT_DIRECTORY}/provisioning/provision.sh"
readonly NGINX_TEMPLATE="${PROJECT_DIRECTORY}/provisioning/host/rustserver-plugin-catalog.nginx.conf"
readonly ENVIRONMENT_TEMPLATE="${PROJECT_DIRECTORY}/provisioning/host/plugin-catalog.env"
readonly SYSTEMD_TEMPLATE="${PROJECT_DIRECTORY}/provisioning/host/rustserver-plugin-catalog.service"

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

bash -n "${PROVISIONER}"

# A fresh host has no legacy snippet; under errexit this must be a successful no-op.
test_directory="$(mktemp -d)"
trap 'rm -rf -- "${test_directory}"' EXIT
LEGACY_NGINX_CONFIG="${test_directory}/missing.conf" bash -Eeuo pipefail -c '
    source <(sed -n "/^transition_known_legacy_nginx_configuration() {/,/^}/p" "$1")
    transition_known_legacy_nginx_configuration
' _ "${PROVISIONER}" || fail 'Missing legacy Nginx configuration must be a successful no-op.'

for required in \
    'readonly NGINX_APPLICATION_DIRECTORY="/etc/rustserver/nginx/applications.d"' \
    'readonly PREPARED_ARTIFACTS_DIRECTORY="/opt/rustserver/plugin-catalog/prepared"' \
    'require_protected_artifact' \
    'require_platform_nginx_extension_point' \
    'transition_known_legacy_platform_unit' \
    'transition_known_legacy_catalog_unit' \
    'transition_known_legacy_nginx_configuration' \
    'configure_media_storage_systemd_access' \
    'install)' \
    'host_provision' \
    'database_provision' \
    'media_provision' \
    "ASPNETCORE_URLS must remain the Catalog loopback listener http://127.0.0.1:5116."; do
    grep -Fq -- "${required}" "${PROVISIONER}" || fail "Provisioner is missing: ${required}"
done

if grep -Fq 'ensure_nginx' "${PROVISIONER}"; then
    fail 'Catalog host provisioning must use Platform Nginx infrastructure instead of installing it.'
fi

grep -Fq 'cat "${ASSET_ROOT}/database/postgresql/initial-schema-current.sql" |' "${PROVISIONER}" || \
    fail 'Catalog initial SQL must be opened by the privileged provisioner and passed through stdin.'
grep -Fq 'cat "${ASSET_ROOT}/database/postgresql/validate-existing-schema.sql" |' "${PROVISIONER}" || \
    fail 'Catalog validation SQL must be opened by the privileged provisioner and passed through stdin.'
if grep -Fq -- '-f "${ASSET_ROOT}/database/postgresql/' "${PROVISIONER}"; then
    fail 'The unprivileged psql process must not open SQL files from the artifact path.'
fi

if grep -Fq 'ReadWritePaths=/var/lib/rustserver/plugin-catalog' "${SYSTEMD_TEMPLATE}"; then
    fail 'The Catalog systemd unit must not hard-code the Local media root.'
fi
grep -Fq "printf 'ReadWritePaths=%s\\n' \"\${root_path}\"" "${PROVISIONER}" || \
    fail 'Media provisioning must derive systemd write access from MediaStorage__RootPath.'

grep -Fqx '    auth_request /_rustserver/auth/authorize-admin;' "${NGINX_TEMPLATE}" >/dev/null || \
    fail 'Catalog Nginx route must retain Admin authorization.'
grep -Fqx '    proxy_pass http://127.0.0.1:5116/;' "${NGINX_TEMPLATE}" >/dev/null || \
    fail 'Catalog Nginx route must proxy only to loopback.'
grep -Fqx 'ASPNETCORE_URLS=http://127.0.0.1:5116' "${ENVIRONMENT_TEMPLATE}" >/dev/null || \
    fail 'Catalog host environment must keep Kestrel on loopback.'
grep -Fqx 'Provisioning__ServiceUser=rustserver-plugin-catalog' "${ENVIRONMENT_TEMPLATE}" >/dev/null || \
    fail 'Catalog transition environment must preserve the legacy service identity.'

# Exercise a failing PostgreSQL command without invoking provisioning or a database.
diagnostic="${test_directory}/diagnostic.json"
if RUSTSERVER_PROVISIONING_DIAGNOSTIC="${diagnostic}" bash -Eeuo pipefail -c '
    source <(sed "$ d" "$1")
    require_regular_file() { :; }
    require_secure_root_owned_regular_file() { :; }
    require_command() { :; }
    ensure_postgresql() { :; }
    getent() { return 0; }
    read_config_value() {
        case "$1" in
            Database__Provider) printf PostgreSql ;;
            ConnectionStrings__PluginCatalog) printf "Host=/var/run/postgresql;Database=catalog;Username=catalog" ;;
            *) printf catalog ;;
        esac
    }
    runuser() { printf "secret from command\n" >&2; return 3; }
    DIAGNOSTIC_STAGE=database
    database_provision
' _ "${PROVISIONER}" 2>/dev/null; then
    fail 'A failed PostgreSQL command must fail provisioning.'
fi
grep -Fxq '{"version":1,"stage":"database","category":"external_command","reason":"postgresql_sql_failed"}' "${diagnostic}" || \
    fail 'PostgreSQL failure must emit a fixed safe database diagnostic.'

printf 'OK: Catalog provisioning contract tests passed\n'
