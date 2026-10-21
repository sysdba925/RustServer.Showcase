#!/usr/bin/env bash
# Explicit Catalog provisioning. Run exactly one operation: host, database, or media.
set -Eeuo pipefail

readonly ASSET_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly CONFIG_DIRECTORY="/etc/rustserver/plugin-catalog"
readonly CONFIG_FILE="${CONFIG_DIRECTORY}/plugin-catalog.env"
readonly SERVICE_UNIT="/etc/systemd/system/rustserver-plugin-catalog.service"
readonly MEDIA_STORAGE_SYSTEMD_DROPIN_DIRECTORY="${SERVICE_UNIT}.d"
readonly MEDIA_STORAGE_SYSTEMD_DROPIN="${MEDIA_STORAGE_SYSTEMD_DROPIN_DIRECTORY}/media-storage.conf"
readonly PREPARED_ARTIFACTS_DIRECTORY="/opt/rustserver/plugin-catalog/prepared"
readonly PLATFORM_NGINX_CONFIG="/etc/nginx/conf.d/rustserver-web.conf"
readonly NGINX_APPLICATION_DIRECTORY="/etc/rustserver/nginx/applications.d"
readonly NGINX_CONFIG="${NGINX_APPLICATION_DIRECTORY}/rustserver-plugin-catalog.conf"
readonly LEGACY_NGINX_CONFIG="/etc/nginx/conf.d/rustserver-plugin-catalog.conf"

# The diagnostic consists only of fixed, owner-defined identifiers; never include
# command output, arguments or configuration values. The file is optional.
DIAGNOSTIC_STAGE=setup
DIAGNOSTIC_CATEGORY=unknown
DIAGNOSTIC_REASON=unexpected_failure
write_diagnostic() {
    local status=$?
    if (( status != 0 )) && [[ -n "${RUSTSERVER_PROVISIONING_DIAGNOSTIC:-}" ]]; then
        if [[ "${DIAGNOSTIC_REASON}" == postgresql_command_failed ]]; then
            case "${status}" in
                2) DIAGNOSTIC_REASON=postgresql_connection_failed ;;
                3) DIAGNOSTIC_REASON=postgresql_sql_failed ;;
            esac
        fi
        printf '{"version":1,"stage":"%s","category":"%s","reason":"%s"}\n' \
            "${DIAGNOSTIC_STAGE}" "${DIAGNOSTIC_CATEGORY}" "${DIAGNOSTIC_REASON}" \
            > "${RUSTSERVER_PROVISIONING_DIAGNOSTIC}" || true
    fi
}
trap write_diagnostic EXIT

fail() {
    DIAGNOSTIC_CATEGORY=validation
    case "$1" in
        "Database provider "*|"Media storage provider "*) DIAGNOSTIC_REASON=unsupported_provider ;;
        "Configuration key "*) DIAGNOSTIC_REASON=missing_configuration ;;
        "Required command "*) DIAGNOSTIC_REASON=required_command_missing ;;
        "Required regular file "*) DIAGNOSTIC_REASON=required_file_missing ;;
        "Database "*"partial Catalog schema"*) DIAGNOSTIC_REASON=incompatible_database_schema ;;
        "PostgreSQL peer authentication"*|"ConnectionStrings__PluginCatalog"*) DIAGNOSTIC_REASON=invalid_database_configuration ;;
        "Platform Nginx "*) DIAGNOSTIC_REASON=platform_nginx_configuration_invalid ;;
        "Nginx configuration test failed"*) DIAGNOSTIC_REASON=nginx_configuration_test_failed ;;
        "Service user "*"does not exist"*) DIAGNOSTIC_REASON=service_user_missing ;;
        "Service user "*"cannot write"*) DIAGNOSTIC_REASON=media_not_writable ;;
        "MediaStorage__RootPath "*) DIAGNOSTIC_REASON=media_sandbox_path_invalid ;;
        "Media root "*) DIAGNOSTIC_REASON=media_root_unsafe ;;
        "Existing "*) DIAGNOSTIC_REASON=existing_configuration_conflict ;;
        "Refusing to overwrite "*) DIAGNOSTIC_REASON=backup_conflict ;;
        *"must use a lowercase safe"*) DIAGNOSTIC_REASON=invalid_identifier ;;
        *"must be a normalized absolute path"*) DIAGNOSTIC_REASON=invalid_path ;;
        *"contains symlink component"*) DIAGNOSTIC_REASON=symlink_in_path ;;
        *"must be owned by root:root"*|*"must not be writable by group or others"*) DIAGNOSTIC_REASON=unsafe_file_permissions ;;
        *) DIAGNOSTIC_REASON=provisioning_validation_failed ;;
    esac
    printf 'Provisioning failed: %s\n' "$*" >&2
    exit 1
}

info() {
    printf '%s\n' "$*"
}

require_root() {
    [[ "${EUID}" -eq 0 ]] || fail 'Run this command as root.'
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command '$1' was not found."
}

require_regular_file() {
    [[ -f "$1" && ! -L "$1" ]] || fail "Required regular file '$1' is missing or is a symlink."
}

read_config_value() {
    local key="$1"
    local entry
    entry="$(grep -E "^${key}=" "${CONFIG_FILE}" | tail -n 1 || true)"
    [[ -n "${entry}" ]] || fail "Configuration key '${key}' is required in ${CONFIG_FILE}."
    printf '%s' "${entry#*=}"
}

validate_identifier() {
    local value="$1"
    local label="$2"
    [[ "${value}" =~ ^[a-z_][a-z0-9_-]{0,62}$ ]] || fail "${label} must use a lowercase safe Linux/PostgreSQL identifier."
}

validate_absolute_path() {
    local value="$1"
    local label="$2"
    [[ "${value}" == /* && "${value}" != *[[:space:]]* && "${value}" != *'//'* && \
        "${value}" != */./* && "${value}" != */../* && "${value}" != */. && "${value}" != */.. ]] || \
        fail "${label} must be a normalized absolute path without whitespace."
}

ensure_no_symlink_components() {
    local path="$1"
    local current=""
    local segment
    IFS='/' read -r -a segments <<< "${path}"
    for segment in "${segments[@]}"; do
        [[ -z "${segment}" ]] && continue
        current="${current}/${segment}"
        [[ ! -L "${current}" ]] || fail "Path '${path}' contains symlink component '${current}'."
    done
}

require_secure_root_owned_directory() {
    local path="$1"
    local label="$2"
    local ownership mode

    [[ -d "${path}" && ! -L "${path}" ]] || fail "${label} is missing or is not a safe directory: ${path}."
    ownership="$(stat -c '%u:%g' "${path}")"
    [[ "${ownership}" == '0:0' ]] || fail "${label} must be owned by root:root: ${path}."
    mode="$(stat -c '%a' "${path}")"
    [[ "${mode}" =~ ^[0-7]{3,4}$ ]] || fail "${label} has an invalid mode: ${path}."
    (( (8#${mode} & 8#022) == 0 )) || fail "${label} must not be writable by group or others: ${path}."
}

require_secure_root_owned_regular_file() {
    local path="$1"
    local label="$2"
    local ownership mode

    require_regular_file "${path}"
    ownership="$(stat -c '%u:%g' "${path}")"
    [[ "${ownership}" == '0:0' ]] || fail "${label} must be owned by root:root: ${path}."
    mode="$(stat -c '%a' "${path}")"
    [[ "${mode}" =~ ^[0-7]{3,4}$ ]] || fail "${label} has an invalid mode: ${path}."
    (( (8#${mode} & 8#022) == 0 )) || fail "${label} must not be writable by group or others: ${path}."
}

require_protected_artifact() {
    local artifact_directory

    [[ "${ASSET_ROOT##*/}" == 'provisioning' ]] || fail 'Provisioning assets must be in a provisioning directory.'
    artifact_directory="${ASSET_ROOT%/*}"
    [[ "${artifact_directory%/*}" == "${PREPARED_ARTIFACTS_DIRECTORY}" ]] || \
        fail "Run provisioning only from the root-owned prepared artifact under ${PREPARED_ARTIFACTS_DIRECTORY}/<release-tag>/."

    ensure_no_symlink_components "${PREPARED_ARTIFACTS_DIRECTORY}"
    ensure_no_symlink_components "${artifact_directory}"
    ensure_no_symlink_components "${ASSET_ROOT}"
    require_secure_root_owned_directory "${PREPARED_ARTIFACTS_DIRECTORY}" 'Prepared artifact root'
    require_secure_root_owned_directory "${artifact_directory}" 'Prepared artifact directory'
    require_secure_root_owned_directory "${ASSET_ROOT}" 'Prepared provisioning directory'
    require_secure_root_owned_regular_file "${ASSET_ROOT}/provision.sh" 'Provisioning entry point'
}

render_template_if_missing() {
    local template="$1"
    local destination="$2"
    local service_user="$3"
    local temporary
    require_secure_root_owned_regular_file "${template}" 'Provisioning template'
    temporary="$(mktemp)"
    sed "s/@SERVICE_USER@/${service_user}/g" "${template}" > "${temporary}"

    if [[ -e "${destination}" || -L "${destination}" ]]; then
        require_regular_file "${destination}"
        if cmp -s "${temporary}" "${destination}"; then
            rm -f "${temporary}"
            return
        fi

        rm -f "${temporary}"
        fail "Existing '${destination}' differs from the shipped template; it was preserved for manual review."
    fi

    install -o root -g root -m 0644 "${temporary}" "${destination}"
    rm -f "${temporary}"
}

require_platform_nginx_extension_point() {
    [[ -d "${NGINX_APPLICATION_DIRECTORY}" && ! -L "${NGINX_APPLICATION_DIRECTORY}" ]] || \
        fail "Platform Nginx application directory is missing or unsafe: ${NGINX_APPLICATION_DIRECTORY}. Run Platform Bootstrap first."
    [[ "$(stat -c '%U:%G:%a' "${NGINX_APPLICATION_DIRECTORY}")" == 'root:root:750' ]] || \
        fail "Platform Nginx application directory must be root:root mode 0750: ${NGINX_APPLICATION_DIRECTORY}."
    require_regular_file "${PLATFORM_NGINX_CONFIG}"
    grep -Fqx "    include ${NGINX_APPLICATION_DIRECTORY}/*.conf;" "${PLATFORM_NGINX_CONFIG}" || \
        fail "Platform Nginx configuration does not expose the required application include point: ${PLATFORM_NGINX_CONFIG}."
}

backup_known_legacy_file() {
    local source="$1"
    local label="$2"
    local backup

    backup="${CONFIG_DIRECTORY}/${label}.platform-bootstrap-$(date -u +%Y%m%dT%H%M%SZ)-$$.bak"
    [[ ! -e "${backup}" && ! -L "${backup}" ]] || fail "Refusing to overwrite legacy backup: ${backup}."
    install -o root -g root -m 0644 "${source}" "${backup}"
    rm -f -- "${source}"
    info "Preserved known legacy ${label} at ${backup}."
}

is_known_legacy_platform_unit() {
    cat <<'UNIT' | cmp -s - "${SERVICE_UNIT}"
[Unit]
Description=RustServer Plugin Catalog API
Wants=network-online.target
After=network-online.target postgresql.service

[Service]
Type=exec
User=rustserver-plugin-catalog
Group=rustserver-plugin-catalog
WorkingDirectory=/opt/rustserver/plugin-catalog/current
Environment=DOTNET_ENVIRONMENT=Production
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5116
Environment="ConnectionStrings__PluginCatalog=Host=/var/run/postgresql;Database=rustserver_plugin_catalog;Username=rustserver-plugin-catalog"
Environment=MediaStorage__Provider=Local
Environment=MediaStorage__RootPath=/var/lib/rustserver/plugin-catalog/media
ExecStart=/usr/bin/dotnet /opt/rustserver/plugin-catalog/current/RustServer.PluginCatalog.Api.dll
Restart=on-failure
RestartSec=5s
TimeoutStopSec=30s
UMask=0027
NoNewPrivileges=true
PrivateTmp=true
PrivateDevices=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/var/lib/rustserver/plugin-catalog

[Install]
WantedBy=multi-user.target
UNIT
}

transition_known_legacy_platform_unit() {
    [[ -e "${SERVICE_UNIT}" || -L "${SERVICE_UNIT}" ]] || return
    require_regular_file "${SERVICE_UNIT}"
    is_known_legacy_platform_unit || \
        fail "Existing '${SERVICE_UNIT}' is not the known Platform-managed Catalog unit; it was preserved for manual review."
    legacy_platform_configuration_is_preserved || \
        fail "Existing '${CONFIG_FILE}' does not preserve the known Platform Catalog database and media values; the unit was preserved for manual review."
    backup_known_legacy_file "${SERVICE_UNIT}" 'rustserver-plugin-catalog.service'
}

is_known_legacy_catalog_unit() {
    local service_user="$1"
    cat <<UNIT | cmp -s - "${SERVICE_UNIT}"
[Unit]
Description=RustServer Plugin Catalog API
After=network-online.target
Wants=network-online.target

[Service]
Type=exec
User=${service_user}
Group=${service_user}
WorkingDirectory=/opt/rustserver/plugin-catalog/current
EnvironmentFile=/etc/rustserver/plugin-catalog/plugin-catalog.env
ExecStart=/usr/bin/dotnet /opt/rustserver/plugin-catalog/current/RustServer.PluginCatalog.Api.dll
Restart=on-failure
RestartSec=5
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/var/lib/rustserver/plugin-catalog

[Install]
WantedBy=multi-user.target
UNIT
}

transition_known_legacy_catalog_unit() {
    local service_user="$1"

    [[ -e "${SERVICE_UNIT}" || -L "${SERVICE_UNIT}" ]] || return
    require_regular_file "${SERVICE_UNIT}"
    is_known_legacy_catalog_unit "${service_user}" || \
        fail "Existing '${SERVICE_UNIT}' differs from the Catalog-managed template; it was preserved for manual review."
    backup_known_legacy_file "${SERVICE_UNIT}" 'rustserver-plugin-catalog.service'
}

legacy_platform_configuration_is_preserved() {
    [[ "$(read_config_value 'Provisioning__ServiceUser')" == 'rustserver-plugin-catalog' ]] &&
        [[ "$(read_config_value 'Provisioning__PostgreSql__RoleName')" == 'rustserver-plugin-catalog' ]] &&
        [[ "$(read_config_value 'Provisioning__PostgreSql__DatabaseName')" == 'rustserver_plugin_catalog' ]] &&
        [[ "$(read_config_value 'ConnectionStrings__PluginCatalog')" == 'Host=/var/run/postgresql;Database=rustserver_plugin_catalog;Username=rustserver-plugin-catalog' ]] &&
        [[ "$(read_config_value 'MediaStorage__RootPath')" == '/var/lib/rustserver/plugin-catalog/media' ]]
}

is_known_legacy_catalog_nginx_configuration() {
    cat <<'NGINX' | cmp -s - "${LEGACY_NGINX_CONFIG}"
location = /plugin-catalog-api {
    return 404;
}

location ^~ /plugin-catalog-api/ {
    client_max_body_size 11m;
    auth_request /_rustserver/auth/authorize-admin;
    proxy_pass http://127.0.0.1:5116/;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
NGINX
}

transition_known_legacy_nginx_configuration() {
    [[ -e "${LEGACY_NGINX_CONFIG}" || -L "${LEGACY_NGINX_CONFIG}" ]] || return 0
    require_regular_file "${LEGACY_NGINX_CONFIG}"
    is_known_legacy_catalog_nginx_configuration || \
        fail "Existing '${LEGACY_NGINX_CONFIG}' is not the known Catalog snippet; it was preserved for manual review."
    backup_known_legacy_file "${LEGACY_NGINX_CONFIG}" 'rustserver-plugin-catalog.nginx.conf'
}

ensure_postgresql() {
    if ! command -v psql >/dev/null 2>&1; then
        require_command apt-get
        DEBIAN_FRONTEND=noninteractive apt-get update
        DEBIAN_FRONTEND=noninteractive apt-get install -y postgresql
    fi

    require_command systemctl
    systemctl enable --now postgresql
}

assert_supported_database_provider() {
    local provider="$1"
    if [[ "${provider,,}" != "postgresql" ]]; then
        fail "Database provider '${provider}' is unsupported. This artifact supports PostgreSql only; no provisioning was performed."
    fi
}

assert_supported_media_provider() {
    local provider="$1"
    if [[ "${provider,,}" != "local" ]]; then
        fail "Media storage provider '${provider}' is unsupported. This artifact supports Local only; no provisioning was performed."
    fi
}

host_provision() {
    local required_command
    for required_command in cat chown chmod cmp date getent grep install mktemp nginx rm sed stat systemctl useradd; do
        require_command "${required_command}"
    done
    install -d -o root -g root -m 0755 "${CONFIG_DIRECTORY}"
    if [[ ! -e "${CONFIG_FILE}" ]]; then
        require_secure_root_owned_regular_file "${ASSET_ROOT}/host/plugin-catalog.env" 'Host configuration template'
        install -o root -g root -m 0640 "${ASSET_ROOT}/host/plugin-catalog.env" "${CONFIG_FILE}"
        info "Created ${CONFIG_FILE}; review it before database or media provisioning."
    fi

    require_regular_file "${CONFIG_FILE}"
    local service_user listener_url
    service_user="$(read_config_value 'Provisioning__ServiceUser')"
    listener_url="$(read_config_value 'ASPNETCORE_URLS')"
    validate_identifier "${service_user}" 'Provisioning__ServiceUser'
    [[ "${listener_url}" == 'http://127.0.0.1:5116' ]] || \
        fail 'ASPNETCORE_URLS must remain the Catalog loopback listener http://127.0.0.1:5116.'

    if ! getent passwd "${service_user}" >/dev/null; then
        useradd --system --home-dir /nonexistent --shell /usr/sbin/nologin --no-create-home "${service_user}"
        info "Created service user ${service_user}."
    fi

    install -d -o root -g root -m 0755 /opt/rustserver/plugin-catalog/releases
    chown root:"${service_user}" "${CONFIG_FILE}"
    chmod 0640 "${CONFIG_FILE}"

    if [[ -e "${SERVICE_UNIT}" || -L "${SERVICE_UNIT}" ]]; then
        if is_known_legacy_platform_unit; then
            transition_known_legacy_platform_unit
        elif is_known_legacy_catalog_unit "${service_user}"; then
            transition_known_legacy_catalog_unit "${service_user}"
        fi
    fi
    render_template_if_missing \
        "${ASSET_ROOT}/host/rustserver-plugin-catalog.service" \
        "${SERVICE_UNIT}" \
        "${service_user}"
    systemctl daemon-reload

    require_platform_nginx_extension_point
    transition_known_legacy_nginx_configuration
    local nginx_config_created=false
    if [[ ! -e "${NGINX_CONFIG}" ]]; then
        nginx_config_created=true
    fi
    render_template_if_missing \
        "${ASSET_ROOT}/host/rustserver-plugin-catalog.nginx.conf" \
        "${NGINX_CONFIG}" \
        "${service_user}"
    if ! nginx -t; then
        if [[ "${nginx_config_created}" == true ]]; then
            rm -f "${NGINX_CONFIG}"
        fi
        fail 'Nginx configuration test failed; a newly created Catalog configuration was removed.'
    fi
    if systemctl is-active --quiet nginx; then
        systemctl reload nginx
    fi

    info 'Host provisioning completed. The API service was not enabled or started.'
}

database_provision() {
    require_regular_file "${CONFIG_FILE}"

    local provider connection_string service_user role_name database_name
    provider="$(read_config_value 'Database__Provider')"
    assert_supported_database_provider "${provider}"
    connection_string="$(read_config_value 'ConnectionStrings__PluginCatalog')"
    service_user="$(read_config_value 'Provisioning__ServiceUser')"
    role_name="$(read_config_value 'Provisioning__PostgreSql__RoleName')"
    database_name="$(read_config_value 'Provisioning__PostgreSql__DatabaseName')"
    validate_identifier "${service_user}" 'Provisioning__ServiceUser'
    validate_identifier "${role_name}" 'Provisioning__PostgreSql__RoleName'
    validate_identifier "${database_name}" 'Provisioning__PostgreSql__DatabaseName'
    [[ "${service_user}" == "${role_name}" ]] || fail 'PostgreSQL peer authentication requires the service user and PostgreSQL role to be identical.'
    [[ "${connection_string}" == *'Host=/var/run/postgresql'* && "${connection_string}" == *"Database=${database_name}"* && "${connection_string}" == *"Username=${role_name}"* ]] || fail 'ConnectionStrings__PluginCatalog must use the configured PostgreSQL database, peer role and /var/run/postgresql socket.'
    getent passwd "${service_user}" >/dev/null || fail "Service user '${service_user}' does not exist. Run explicit host provisioning first; database provisioning will not create it."

    DIAGNOSTIC_CATEGORY=external_command
    DIAGNOSTIC_REASON=postgresql_service_or_install_failed
    ensure_postgresql
    DIAGNOSTIC_CATEGORY=unknown
    DIAGNOSTIC_REASON=unexpected_failure
    require_command cat
    require_command runuser
    require_secure_root_owned_regular_file "${ASSET_ROOT}/database/postgresql/initial-schema-current.sql" 'Initial database schema SQL'
    require_secure_root_owned_regular_file "${ASSET_ROOT}/database/postgresql/validate-existing-schema.sql" 'Database validation SQL'

    DIAGNOSTIC_CATEGORY=external_command
    DIAGNOSTIC_REASON=postgresql_command_failed
    local role_exists database_exists
    role_exists="$(runuser -u postgres -- psql -X -At --dbname=postgres -c "SELECT 1 FROM pg_roles WHERE rolname = '${role_name}';")"
    if [[ "${role_exists}" != '1' ]]; then
        runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 --dbname=postgres \
            -c "CREATE ROLE \"${role_name}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;"
        info "Created PostgreSQL role ${role_name}."
    fi

    database_exists="$(runuser -u postgres -- psql -X -At --dbname=postgres -c "SELECT 1 FROM pg_database WHERE datname = '${database_name}';")"
    if [[ "${database_exists}" != '1' ]]; then
        runuser -u postgres -- psql -X -v ON_ERROR_STOP=1 --dbname=postgres \
            -c "CREATE DATABASE \"${database_name}\" OWNER \"${role_name}\";"
        info "Created PostgreSQL database ${database_name}."
    fi

    local expected_tables total_tables
    expected_tables="$(runuser -u "${service_user}" -- psql -X -At --dbname="${database_name}" -c "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' AND table_name IN ('plugins', 'plugin_sources', 'plugin_media');")"
    total_tables="$(runuser -u "${service_user}" -- psql -X -At --dbname="${database_name}" -c "SELECT count(*) FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema');")"

    if [[ "${expected_tables}" == '0' && "${total_tables}" == '0' ]]; then
        cat "${ASSET_ROOT}/database/postgresql/initial-schema-current.sql" |
            runuser -u "${service_user}" -- psql -X -v ON_ERROR_STOP=1 --dbname="${database_name}"
        info 'Created the current Catalog PostgreSQL schema in the new empty database.'
    elif [[ "${expected_tables}" == '3' ]]; then
        cat "${ASSET_ROOT}/database/postgresql/validate-existing-schema.sql" |
            runuser -u "${service_user}" -- psql -X -v ON_ERROR_STOP=1 --dbname="${database_name}"
        info 'Existing Catalog PostgreSQL schema is compatible; no migration was applied.'
    else
        fail "Database '${database_name}' is non-empty or has a partial Catalog schema; no changes were made."
    fi
}

validate_media_root_for_systemd() {
    local root_path="$1"

    case "${root_path}" in
        /|/boot|/boot/*|/dev|/dev/*|/etc|/etc/*|/home|/home/*|/proc|/proc/*|/root|/root/*|/run|/run/*|/sys|/sys/*|/usr|/usr/*)
            fail "MediaStorage__RootPath '${root_path}' is incompatible with the Catalog systemd filesystem sandbox. Use a persistent path outside protected system and home directories."
            ;;
    esac
}

is_catalog_managed_media_systemd_dropin() {
    local path="$1"
    local -a lines

    mapfile -t lines < "${path}"
    [[ "${#lines[@]}" -eq 3 ]] && \
        [[ "${lines[0]}" == '# Managed by RustServer.PluginCatalog media provisioning.' ]] && \
        [[ "${lines[1]}" == '[Service]' ]] && \
        [[ "${lines[2]}" =~ ^ReadWritePaths=/[^[:space:]]+$ ]]
}

configure_media_storage_systemd_access() {
    local root_path="$1"
    local temporary backup

    require_command cmp
    require_command date
    require_command install
    require_command mktemp
    require_command mv
    require_command systemctl
    install -d -o root -g root -m 0755 "${MEDIA_STORAGE_SYSTEMD_DROPIN_DIRECTORY}"
    temporary="$(mktemp "${MEDIA_STORAGE_SYSTEMD_DROPIN_DIRECTORY}/.media-storage.XXXXXX")"
    {
        printf '%s\n' '# Managed by RustServer.PluginCatalog media provisioning.'
        printf '%s\n' '[Service]'
        printf 'ReadWritePaths=%s\n' "${root_path}"
    } > "${temporary}"

    if [[ -e "${MEDIA_STORAGE_SYSTEMD_DROPIN}" || -L "${MEDIA_STORAGE_SYSTEMD_DROPIN}" ]]; then
        require_regular_file "${MEDIA_STORAGE_SYSTEMD_DROPIN}"
        if cmp -s "${temporary}" "${MEDIA_STORAGE_SYSTEMD_DROPIN}"; then
            rm -f "${temporary}"
            return
        fi

        is_catalog_managed_media_systemd_dropin "${MEDIA_STORAGE_SYSTEMD_DROPIN}" || {
            rm -f "${temporary}"
            fail "Existing '${MEDIA_STORAGE_SYSTEMD_DROPIN}' is not Catalog-managed; it was preserved for manual review."
        }
        backup="${CONFIG_DIRECTORY}/media-storage.conf-$(date -u +%Y%m%dT%H%M%SZ)-$$.bak"
        [[ ! -e "${backup}" && ! -L "${backup}" ]] || {
            rm -f "${temporary}"
            fail "Refusing to overwrite media-storage backup: ${backup}."
        }
        install -o root -g root -m 0644 "${MEDIA_STORAGE_SYSTEMD_DROPIN}" "${backup}"
        info "Preserved previous Catalog media sandbox configuration at ${backup}."
    fi

    chown root:root "${temporary}"
    chmod 0644 "${temporary}"
    mv -f "${temporary}" "${MEDIA_STORAGE_SYSTEMD_DROPIN}"
    systemctl daemon-reload
}

media_provision() {
    require_regular_file "${CONFIG_FILE}"

    local provider service_user root_path
    provider="$(read_config_value 'MediaStorage__Provider')"
    assert_supported_media_provider "${provider}"
    service_user="$(read_config_value 'Provisioning__ServiceUser')"
    root_path="$(read_config_value 'MediaStorage__RootPath')"
    validate_identifier "${service_user}" 'Provisioning__ServiceUser'
    validate_absolute_path "${root_path}" 'MediaStorage__RootPath'
    validate_media_root_for_systemd "${root_path}"
    getent passwd "${service_user}" >/dev/null || fail "Service user '${service_user}' does not exist. Run explicit host provisioning first; media provisioning will not create it."
    ensure_no_symlink_components "${root_path}"

    if [[ -e "${root_path}" ]]; then
        [[ -d "${root_path}" && ! -L "${root_path}" ]] || fail "Media root '${root_path}' exists but is not a safe directory."
    else
        mkdir -p "$(dirname -- "${root_path}")"
        install -d -o "${service_user}" -g "${service_user}" -m 0750 "${root_path}"
        info "Created Local media root ${root_path}."
    fi

    runuser -u "${service_user}" -- test -w "${root_path}" || fail "Service user '${service_user}' cannot write existing media root '${root_path}'."
    configure_media_storage_systemd_access "${root_path}"
    info 'Local media provisioning completed; existing media files were not changed.'
}

usage() {
    cat <<'USAGE'
Usage: sudo bash provisioning/provision.sh <install|host|database|media>

The install command orchestrates host, database and media on first activation.
Individual operations remain available for recovery. None activates/releases or starts
the Catalog API, and no operation invokes another provisioning operation.
USAGE
}

main() {
    [[ "$#" -eq 1 ]] || { usage; exit 2; }
    require_root
    require_command stat
    require_protected_artifact

    case "$1" in
        install)
            DIAGNOSTIC_STAGE=host
            host_provision
            DIAGNOSTIC_STAGE=database
            database_provision
            DIAGNOSTIC_STAGE=media
            DIAGNOSTIC_CATEGORY=unknown
            DIAGNOSTIC_REASON=unexpected_failure
            media_provision
            info 'Catalog initial installation dependencies are ready.'
            ;;
        host) DIAGNOSTIC_STAGE=host; host_provision ;;
        database) DIAGNOSTIC_STAGE=database; database_provision ;;
        media) DIAGNOSTIC_STAGE=media; media_provision ;;
        *) usage; exit 2 ;;
    esac
}

main "$@"
