#!/usr/bin/env bash
set -Eeuo pipefail

STEP_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly STEP_DIR
# shellcheck source=../lib/common.sh
source "$STEP_DIR/../lib/common.sh"

if ! bootstrap_mode_is install; then
  bootstrap_skip "Step is not required for Bootstrap mode: $(bootstrap_mode)"
  exit 0
fi

readonly NGINX_PACKAGE='nginx'
readonly NGINX_BINARY='/usr/sbin/nginx'
readonly NGINX_CONFIGURATION_DIRECTORY='/etc/nginx/conf.d'
readonly NGINX_CONFIGURATION_SOURCE="$STEP_DIR/../../config/rustserver-web.nginx.conf"
readonly NGINX_CONFIGURATION_PATH="$NGINX_CONFIGURATION_DIRECTORY/rustserver-web.conf"
readonly NGINX_APPLICATION_CONFIGURATION_DIRECTORY='/etc/rustserver/nginx/applications.d'
readonly NGINX_DEFAULT_SITE='/etc/nginx/sites-enabled/default'
readonly WEB_DIRECTORY='/opt/rustserver/web'
readonly WEB_RELEASES_DIRECTORY="$WEB_DIRECTORY/releases"
readonly LANDING_SOURCE="$STEP_DIR/../../config/landing/index.html"
readonly LANDING_DIRECTORY='/opt/rustserver/landing'

NGINX_CONFIGURATION_CHANGED=0
NGINX_PREVIOUS_CONFIGURATION=''
NGINX_HAD_PREVIOUS_CONFIGURATION=0
NGINX_DEFAULT_SITE_WAS_DISABLED=0

nginx_is_installed() {
  dpkg-query --show --showformat='${Status}' "$NGINX_PACKAGE" 2>/dev/null | grep -qx 'install ok installed'
}

install_nginx_if_missing() {
  if nginx_is_installed; then
    bootstrap_skip 'Nginx is already installed'
    return
  fi

  bootstrap_info 'Installing Nginx'
  run_as_root apt-get update || bootstrap_die 'Could not refresh APT package metadata for Nginx'
  run_as_root apt-get install --yes --no-install-recommends "$NGINX_PACKAGE" || \
    bootstrap_die 'Could not install Nginx'
  nginx_is_installed || bootstrap_die 'Nginx was not installed successfully'
}

disable_distribution_default_site() {
  if [[ ! -e "$NGINX_DEFAULT_SITE" && ! -L "$NGINX_DEFAULT_SITE" ]]; then
    return
  fi

  [[ -L "$NGINX_DEFAULT_SITE" ]] || \
    bootstrap_die "Refusing to replace a non-default Nginx site: $NGINX_DEFAULT_SITE"
  [[ "$(readlink -f -- "$NGINX_DEFAULT_SITE")" == '/etc/nginx/sites-available/default' ]] || \
    bootstrap_die "Refusing to replace an unrecognized Nginx site link: $NGINX_DEFAULT_SITE"

  run_as_root rm -- "$NGINX_DEFAULT_SITE" || \
    bootstrap_die "Could not disable the distribution default Nginx site: $NGINX_DEFAULT_SITE"
  NGINX_DEFAULT_SITE_WAS_DISABLED=1
  bootstrap_info 'Disabled the distribution default Nginx site'
}

write_candidate_main_configuration() {
  local candidate_server_configuration="$1"

  {
    printf '%s\n' 'worker_processes 1;'
    printf '%s\n' 'pid /tmp/rustserver-nginx-test.pid;'
    printf '%s\n' 'events { worker_connections 16; }'
    printf '%s\n' 'http {'
    printf '%s\n' '    include /etc/nginx/mime.types;'
    printf '    include %s;\n' "$candidate_server_configuration"
    printf '%s\n' '}'
  }
}

validate_candidate_configuration() {
  local temporary_directory
  local candidate_server_configuration
  local candidate_main_configuration

  temporary_directory="$(run_as_root mktemp -d)" || bootstrap_die 'Could not create temporary Nginx validation directory'
  candidate_server_configuration="$temporary_directory/rustserver-web.conf"
  candidate_main_configuration="$temporary_directory/nginx.conf"
  trap 'run_as_root rm -rf -- "$temporary_directory"' RETURN

  run_as_root install -m 0644 -- "$NGINX_CONFIGURATION_SOURCE" "$candidate_server_configuration" || \
    bootstrap_die 'Could not prepare temporary Nginx configuration'
  write_candidate_main_configuration "$candidate_server_configuration" | \
    run_as_root tee "$candidate_main_configuration" >/dev/null
  run_as_root "$NGINX_BINARY" -t -c "$candidate_main_configuration" || \
    bootstrap_die 'Candidate RustServer Nginx configuration is invalid'

  trap - RETURN
  run_as_root rm -rf -- "$temporary_directory"
}

install_configuration_if_changed() {
  local temporary_configuration

  run_as_root install -d -m 0755 -o root -g root -- "$NGINX_CONFIGURATION_DIRECTORY" || \
    bootstrap_die "Could not create Nginx configuration directory: $NGINX_CONFIGURATION_DIRECTORY"
  if run_as_root test -f "$NGINX_CONFIGURATION_PATH" && \
    run_as_root cmp -s "$NGINX_CONFIGURATION_SOURCE" "$NGINX_CONFIGURATION_PATH" && \
    [[ "$(run_as_root stat -c '%U:%G:%a' -- "$NGINX_CONFIGURATION_PATH")" == 'root:root:644' ]]; then
    bootstrap_skip "Nginx configuration is already current: $NGINX_CONFIGURATION_PATH"
    NGINX_CONFIGURATION_CHANGED=0
    return
  fi

  temporary_configuration="$(run_as_root mktemp --tmpdir="$NGINX_CONFIGURATION_DIRECTORY" '.rustserver-web.conf.tmp.XXXXXX')" || \
    bootstrap_die 'Could not create temporary Nginx configuration'
  if run_as_root test -f "$NGINX_CONFIGURATION_PATH"; then
    NGINX_PREVIOUS_CONFIGURATION="$(run_as_root mktemp --tmpdir="$NGINX_CONFIGURATION_DIRECTORY" '.rustserver-web.conf.previous.XXXXXX')" || {
      run_as_root rm -f -- "$temporary_configuration"
      bootstrap_die 'Could not create Nginx configuration rollback copy'
    }
    run_as_root cp -- "$NGINX_CONFIGURATION_PATH" "$NGINX_PREVIOUS_CONFIGURATION" || {
      run_as_root rm -f -- "$temporary_configuration" "$NGINX_PREVIOUS_CONFIGURATION"
      bootstrap_die 'Could not preserve the previous Nginx configuration'
    }
    NGINX_HAD_PREVIOUS_CONFIGURATION=1
  fi
  run_as_root install -m 0644 -o root -g root "$NGINX_CONFIGURATION_SOURCE" "$temporary_configuration" || {
    run_as_root rm -f -- "$temporary_configuration" "$NGINX_PREVIOUS_CONFIGURATION"
    bootstrap_die 'Could not prepare temporary Nginx configuration'
  }
  run_as_root mv -Tf -- "$temporary_configuration" "$NGINX_CONFIGURATION_PATH" || {
    run_as_root rm -f -- "$temporary_configuration" "$NGINX_PREVIOUS_CONFIGURATION"
    bootstrap_die 'Could not install Nginx configuration'
  }
  NGINX_CONFIGURATION_CHANGED=1
  bootstrap_info "Installed Nginx configuration: $NGINX_CONFIGURATION_PATH"
}

ensure_application_configuration_directory() {
  ensure_root_directory /etc/rustserver/nginx 0750 root root
  ensure_root_directory "$NGINX_APPLICATION_CONFIGURATION_DIRECTORY" 0750 root root
}

install_landing_page() {
  local directory="$1" source="$2" target="$1/index.html"

  ensure_root_directory "$directory" 0755 root root
  [[ "$(run_as_root stat -c '%U:%G:%a' -- "$directory")" == 'root:root:755' ]] || \
    bootstrap_die "Unsafe landing directory ownership or mode: $directory; review and set root:root 0755"
  if run_as_root test -e "$target" || run_as_root test -L "$target"; then
    if ! run_as_root test -f "$target" || run_as_root test -L "$target" || \
      [[ "$(run_as_root stat -c '%U:%G:%a' -- "$target")" != 'root:root:644' ]] || \
      ! run_as_root cmp -s -- "$source" "$target"; then
      bootstrap_die "Conflicting landing page: $target; review and preserve or remove it before rerunning Bootstrap"
    fi
    bootstrap_skip "Landing page is already current: $target"
    return
  fi
  run_as_root install -m 0644 -o root -g root -- "$source" "$target" || \
    bootstrap_die "Could not install landing page: $target"
  bootstrap_info "Installed landing page: $target"
}

restore_previous_configuration() {
  (( NGINX_CONFIGURATION_CHANGED )) || return

  if (( NGINX_HAD_PREVIOUS_CONFIGURATION )); then
    run_as_root mv -Tf -- "$NGINX_PREVIOUS_CONFIGURATION" "$NGINX_CONFIGURATION_PATH" || \
      bootstrap_die 'Could not restore the previous Nginx configuration'
  else
    run_as_root rm -f -- "$NGINX_CONFIGURATION_PATH" || \
      bootstrap_die 'Could not remove the invalid new Nginx configuration'
  fi
  NGINX_PREVIOUS_CONFIGURATION=''
  NGINX_CONFIGURATION_CHANGED=0
  run_as_root "$NGINX_BINARY" -t || bootstrap_die 'The previous Nginx configuration is invalid after rollback'
}

restore_distribution_default_site() {
  (( NGINX_DEFAULT_SITE_WAS_DISABLED )) || return

  run_as_root ln -s /etc/nginx/sites-available/default "$NGINX_DEFAULT_SITE" || \
    bootstrap_die 'Could not restore the distribution default Nginx site'
  NGINX_DEFAULT_SITE_WAS_DISABLED=0
}

verify_installed_configuration() {
  if ! run_as_root "$NGINX_BINARY" -t; then
    restore_previous_configuration
    restore_distribution_default_site
    bootstrap_die 'Installed Nginx configuration is invalid; the previous configuration was restored'
  fi

  if [[ -n "$NGINX_PREVIOUS_CONFIGURATION" ]]; then
    run_as_root rm -f -- "$NGINX_PREVIOUS_CONFIGURATION"
    NGINX_PREVIOUS_CONFIGURATION=''
  fi
}

start_or_reload_nginx() {
  if ! run_as_root systemctl is-active --quiet nginx.service; then
    run_as_root systemctl enable --now nginx.service || \
      bootstrap_die 'Could not start and enable Nginx'
    return
  fi

  run_as_root systemctl enable nginx.service || bootstrap_die 'Could not enable Nginx'
  if (( NGINX_CONFIGURATION_CHANGED )); then
    run_as_root systemctl reload nginx.service || bootstrap_die 'Could not reload Nginx configuration'
  fi
}

main() {
  ensure_sudo_access
  require_regular_file "$NGINX_CONFIGURATION_SOURCE"
  require_regular_file "$LANDING_SOURCE"
  require_command apt-get
  require_command cmp
  require_command dpkg-query
  require_command mktemp
  require_command readlink
  require_command systemctl

  ensure_root_directory "$WEB_DIRECTORY" 0755 root root
  ensure_root_directory "$WEB_RELEASES_DIRECTORY" 0755 root root
  install_landing_page "$LANDING_DIRECTORY" "$LANDING_SOURCE"
  ensure_application_configuration_directory
  install_nginx_if_missing
  [[ -x "$NGINX_BINARY" ]] || bootstrap_die "Nginx executable is unavailable: $NGINX_BINARY"
  validate_candidate_configuration
  disable_distribution_default_site
  install_configuration_if_changed
  verify_installed_configuration
  start_or_reload_nginx
  run_as_root systemctl is-active --quiet nginx.service || bootstrap_die 'Nginx did not become active'
  bootstrap_info 'Nginx is serving the landing page; Dashboard is unavailable until a valid Web release is activated'
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  main "$@"
fi
