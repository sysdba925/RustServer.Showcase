#!/usr/bin/env bash
set -Eeuo pipefail
helper="$(cd "$(dirname "$0")/.." && pwd)/vm-rustserver.sh"
bash -n "$helper"
output="$(printf 'invalid-test-payload' | bash "$helper" bootstrap Production 2>&1 || true)"
[[ "$output" == *'Invalid credential data.'* && "$output" == *'__RUST_BOOTSTRAP_RESULT__=1'* && "$output" != *'invalid-test-payload'* ]]
if bash "$helper" authorize-tunnel >/dev/null 2>&1; then exit 1; fi
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
cp "$helper" "$work/vm-rustserver.sh"
cat > "$work/install.sh" <<'INSTALL'
#!/usr/bin/env bash
set -eu
[[ "$UPDATER_RELEASE_CHANNEL" == "$EXPECTED_CHANNEL" ]]
[[ "$RUSTSERVER_UPDATER_BIND_ADDRESS" == 127.0.0.1 ]]
[[ "${FAIL_INSTALL:-0}" == 0 ]] || exit 7
for prompt in 'GitHub bootstrap token' 'GitHub updater token' 'GitHub plugins token' 'Google Client ID' 'Google Client Secret' 'Admin username' 'Admin password'; do
    printf '%s: ' "$prompt"
    IFS= read -r value
    [[ -n "$value" ]]
done
INSTALL
payload='{"GitHub bootstrap token":"testtoken1","GitHub updater all services token":"testtoken2","GitHub plugin & config token":"testtoken3","GOOGLE_CLIENT_ID":"testclientid","GOOGLE_CLIENT_SECRET":"testclientsecret","ADMIN_USERNAME":"testuser","ADMIN_PASSWORD":"testpassword123"}'
mkdir "$work/bin"
cat > "$work/bin/sudo" <<'SUDO'
#!/usr/bin/env bash
set -eu
[[ "$1" == -n ]]
shift
printf '%s\n' "$*" >> "$SUDO_LOG"
if [[ "$1" == test && "$2" == -f && "$3" == /etc/rustserver/auth.env ]]; then
    test -f "$AUTH_FILE"
elif [[ "$1" == sed && "$2" == -i && "$3" == '/^Auth__AllowInsecureHttp=/d; $a Auth__AllowInsecureHttp=true' && "$4" == /etc/rustserver/auth.env ]]; then
    [[ "${FAIL_SED:-0}" == 0 ]] || exit 1
    sed -i "$3" "$AUTH_FILE"
elif [[ "$1" == grep && "$2" == -qx && "$3" == 'Auth__AllowInsecureHttp=true' && "$4" == /etc/rustserver/auth.env ]]; then
    grep -qx "$3" "$AUTH_FILE"
elif [[ "$1" == tee && "$2" == -a && "$3" == /etc/rustserver/auth.env ]]; then
    tee -a "$AUTH_FILE"
else
    exit 1
fi
SUDO
chmod +x "$work/bin/sudo"
export PATH="$work/bin:$PATH" SUDO_LOG="$work/sudo.log" AUTH_FILE="$work/auth.env"
printf 'Other=value\nAuth__AllowInsecureHttp=false\nAuth__AllowInsecureHttp=false\n' > "$AUTH_FILE"
export EXPECTED_CHANNEL=Production
output="$(printf '%s' "$payload" | bash "$work/vm-rustserver.sh" bootstrap Production 2>&1)"
[[ "$output" == *'__RUST_BOOTSTRAP_RESULT__=0'* && "$output" != *'testpassword123'* && "$output" != *'testtoken1'* && "$output" != *'testclientid'* ]]
[[ ! -e "$SUDO_LOG" ]]
export EXPECTED_CHANNEL=Development
for run in 1 2; do
    output="$(printf '%s' "$payload" | bash "$work/vm-rustserver.sh" bootstrap Development 2>&1)"
    [[ "$output" == *'__RUST_BOOTSTRAP_RESULT__=0'* ]]
    [[ "$(cat "$AUTH_FILE")" == $'Other=value\nAuth__AllowInsecureHttp=true' ]]
done
rm "$SUDO_LOG"
output="$(printf '%s' "$payload" | FAIL_INSTALL=1 bash "$work/vm-rustserver.sh" bootstrap Development 2>&1 || true)"
[[ "$output" == *'__RUST_BOOTSTRAP_RESULT__=7'* && ! -e "$SUDO_LOG" ]]
output="$(printf '%s' "$payload" | FAIL_SED=1 bash "$work/vm-rustserver.sh" bootstrap Development 2>&1 || true)"
[[ "$output" == *'__RUST_BOOTSTRAP_RESULT__=1'* ]]
printf 'Operator credential and command smoke tests passed.\n'
