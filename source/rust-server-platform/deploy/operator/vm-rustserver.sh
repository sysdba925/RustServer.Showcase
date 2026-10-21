#!/usr/bin/env bash
# Remote operator helper. Credentials arrive only on stdin.
set -Eeuo pipefail

usage() {
    echo 'Usage: bash vm-rustserver.sh bootstrap Development|Production | clean | tunnel-info' >&2
    exit 2
}

# Read only the fixed Updater listener configuration (never dump credentials).
tunnel_port() {
    local env_file='/etc/rustserver/updater.env'
    [[ -f "$env_file" ]] || { echo '[TUNNEL] Updater is not configured.' >&2; return 1; }
    local bind port
    bind="$(sudo -n sed -n 's/^RUSTSERVER_UPDATER_BIND_ADDRESS=//p' "$env_file")"
    port="$(sudo -n sed -n 's/^RUSTSERVER_UPDATER_PORT=//p' "$env_file")"
    [[ "$port" =~ ^[0-9]{1,5}$ ]] && (( 10#$port >= 1 && 10#$port <= 65535 )) || {
        echo '[TUNNEL] Invalid Updater port.' >&2
        return 1
    }
    printf '%s\n' "$port"
}

case "${1:-}" in
    tunnel-info)
        [[ "$#" -eq 1 ]] || usage
        port="$(tunnel_port)"
        curl -fsS --connect-timeout 2 --max-time 5 "http://127.0.0.1:${port}/health" >/dev/null || {
            echo '[TUNNEL] Updater /health is not available.' >&2
            exit 1
        }
        printf 'TUNNEL_PORT=%s\n' "$port"
        ;;
    clean)
        [[ "$#" -eq 1 ]] || usage
        echo '[CLEAN] Remote wrapper started; launching cleanup.sh...'
        python3 -u -c "$(cat <<'PYTHON'
import os
import selectors
import subprocess
import sys

# A background descendant can keep stdout open after cleanup.sh exits.  Drain
# available output, but use the actual sudo process exit as the completion
# signal rather than waiting indefinitely for every inherited pipe to close.
process = subprocess.Popen(
    ["sudo", "-n", "bash", "-x", os.path.join(os.path.dirname(os.path.realpath(sys.argv[1])), "cleanup.sh")],
    stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
)
fd = process.stdout.fileno()
os.set_blocking(fd, False)
selector = selectors.DefaultSelector()
selector.register(fd, selectors.EVENT_READ)
while True:
    events = selector.select(0.2)
    if events:
        try:
            data = os.read(fd, 65536)
        except BlockingIOError:
            data = None
        if data:
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()
        elif data == b"":
            selector.unregister(fd)
    status = process.poll()
    if status is not None:
        while True:
            try:
                data = os.read(fd, 65536)
            except BlockingIOError:
                break
            if not data:
                break
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()
        break
selector.close()
process.stdout.close()
exit_code = status if status >= 0 else 128 - status
print(f"[CLEAN] cleanup.sh exited with code {exit_code}.", flush=True)
# This final framed result lets Windows finish even if the SSH client
# itself does not terminate after the remote operation has completed.
print(f"__RUST_CLEAN_RESULT__={exit_code}", flush=True)
sys.exit(exit_code)
PYTHON
)" "$0"
        ;;
    bootstrap)
        [[ "$#" -eq 2 && ( "$2" == Development || "$2" == Production ) ]] || usage
        finish_bootstrap() {
            local status=$?
            trap - EXIT
            printf '__RUST_BOOTSTRAP_RESULT__=%s\n' "$status"
            exit "$status"
        }
        trap finish_bootstrap EXIT
        # Ubuntu 24.04 includes Python 3; its stdlib PTY is used because
        # install.sh requires an interactive terminal for Google/Admin inputs.
        # Only the Python program is in argv; its stdin reads the SSH stream.
        echo '[BOOTSTRAP] Remote wrapper started; receiving credentials...'
        python3 -u -c "$(cat <<'PYTHON'
import errno
import json
import os
import pty
import re
import sys
import termios

fields = {
    "GitHub bootstrap token": "GitHub bootstrap token: ",
    "GitHub updater all services token": "GitHub updater token: ",
    "GitHub plugin & config token": "GitHub plugins token: ",
    "GOOGLE_CLIENT_ID": "Google Client ID: ",
    "GOOGLE_CLIENT_SECRET": "Google Client Secret: ",
    "ADMIN_USERNAME": "Admin username: ",
    "ADMIN_PASSWORD": "Admin password: ",
}

# Parse credentials streamed via stdin.
# Never print values and cap the input size to avoid unbounded memory usage.
payload = sys.stdin.buffer.read(131073)
if len(payload) > 131072:
    raise SystemExit("[BOOTSTRAP] Credentials input is too large.")
print("[BOOTSTRAP] Credentials received; validating...", flush=True)
try:
    secrets = json.loads(payload.decode("utf-8-sig"))
except (UnicodeDecodeError, ValueError):
    raise SystemExit("[BOOTSTRAP] Invalid credential data.")

if not isinstance(secrets, dict) or set(fields) != set(secrets) or any(not isinstance(v, str) or not v for v in secrets.values()):
    raise SystemExit("[BOOTSTRAP] Missing or invalid credentials.")

for name in list(fields)[:3]:
    if not re.fullmatch(r"[A-Za-z0-9_=-]+", secrets[name]):
        raise SystemExit(f"Invalid GitHub token characters for: {name}")
for name, value in secrets.items():
    if "\n" in value or "\r" in value or "\x00" in value or value.startswith("my_"):
        raise SystemExit(f"Invalid or example placeholder value for: {name}")
if len(secrets["ADMIN_PASSWORD"]) < 12:
    raise SystemExit("ADMIN_PASSWORD must be at least 12 characters.")

# Supply answers only when their exact prompt appears, since on an existing VM
# Installer intentionally skips credentials that are already provisioned.
prompts = {prompt: secrets[name] for name, prompt in fields.items()}
answered = set()

print("[BOOTSTRAP] Credentials validated; starting install.sh...", flush=True)
pid, master_fd = pty.fork()
if pid == 0:
    try:
        settings = termios.tcgetattr(0)
        settings[3] &= ~termios.ECHO  # Prevent visible input echo, including username/ID.
        termios.tcsetattr(0, termios.TCSANOW, settings)
    except termios.error:
        pass
    env = os.environ.copy()
    env["UPDATER_RELEASE_CHANNEL"] = sys.argv[2]
    # Keep the Updater listener on loopback for the SSH tunnel.
    env["RUSTSERVER_UPDATER_BIND_ADDRESS"] = "127.0.0.1"
    os.execve("/bin/bash", ["bash", os.path.join(os.path.dirname(os.path.realpath(sys.argv[1])), "install.sh")], env)

tail = ""
try:
    while True:
        try:
            chunk = os.read(master_fd, 65536)
        except OSError as e:
            if e.errno == errno.EIO:
                break
            raise
        if not chunk:
            break
        sys.stdout.buffer.write(chunk)
        sys.stdout.buffer.flush()
        tail = (tail + chunk.decode("utf-8", errors="replace"))[-1024:]
        for prompt, value in prompts.items():
            if prompt in tail and prompt not in answered:
                answered.add(prompt)
                os.write(master_fd, (value + "\n").encode("utf-8"))
                tail = ""
                break
finally:
    os.close(master_fd)

_, status = os.waitpid(pid, 0)
exit_code = os.waitstatus_to_exitcode(status)
if exit_code:
    print(f"[BOOTSTRAP] install.sh failed (exit {exit_code}).", file=sys.stderr, flush=True)
    sys.exit(exit_code if exit_code > 0 else 128 - exit_code)
print("[BOOTSTRAP] install.sh completed.", flush=True)
PYTHON
)" "$0" "$2"
        if [[ "$2" == Development ]]; then
            echo '[BOOTSTRAP] Applying test Auth setting...'
            sudo -n test -f /etc/rustserver/auth.env || {
                echo 'Auth config is missing; Bootstrap may not have completed.' >&2
                exit 1
            }
            sudo -n sed -i '/^Auth__AllowInsecureHttp=/d; $a Auth__AllowInsecureHttp=true' /etc/rustserver/auth.env
            if ! sudo -n grep -qx 'Auth__AllowInsecureHttp=true' /etc/rustserver/auth.env; then
                printf '%s\n' 'Auth__AllowInsecureHttp=true' | sudo -n tee -a /etc/rustserver/auth.env >/dev/null
            fi
            echo '[BOOTSTRAP] Test Auth setting applied.'
        fi
        ;;

    *) usage ;;
esac
