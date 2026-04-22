#!/usr/bin/env bash
# start-kiosk-session.sh
# X session script launched by LightDM for the charthub-kiosk session.
#
# In kiosk mode the X session owns ChartHub.Server directly. This keeps boot
# and restart behavior aligned with the live display session that the server's
# HUD depends on. When either Openbox or ChartHub.Server exits, the session
# ends and LightDM relaunches it.

set -euo pipefail

# ---------------------------------------------------------------------------
# 1. Disable screensaver, blanking, and DPMS
# ---------------------------------------------------------------------------
xset s off || true
xset s noblank || true
xset -dpms || true

# ---------------------------------------------------------------------------
# 2. (Optional) force display resolution
# ---------------------------------------------------------------------------
KIOSK_RESOLUTION="${KIOSK_RESOLUTION:-}"
if [[ -n "$KIOSK_RESOLUTION" ]]; then
    xrandr --size "$KIOSK_RESOLUTION" || true
fi

# ---------------------------------------------------------------------------
# 3. Prepare persistent kiosk logs
# ---------------------------------------------------------------------------
LOG_DIR="/srv/appdata/charthub/active/logs"
SESSION_LOG="${LOG_DIR}/kiosk-session.log"
SERVER_LOG="${LOG_DIR}/charthub-server-session.log"

mkdir -p "$LOG_DIR"

timestamp() {
    date -Iseconds
}

log_line() {
    printf '[%s] %s\n' "$(timestamp)" "$1" >> "$SESSION_LOG"
}

log_line "kiosk session starting"

# ---------------------------------------------------------------------------
# 4. Run Openbox as the window manager first
# ---------------------------------------------------------------------------
openbox &
OPENBOX_PID=$!
log_line "openbox pid=${OPENBOX_PID}"

# ---------------------------------------------------------------------------
# 5. Start ChartHub.Server for kiosk mode
# ---------------------------------------------------------------------------
KIOSK_ENV_FILE="/srv/appdata/charthub/active/kiosk.env"
SERVER_ENV_FILE="/srv/appdata/charthub/active/config/charthub-server.env"
SERVER_BINARY="/srv/appdata/charthub/active/current/ChartHub.Server"
ACTIVE_ROOT="/srv/appdata/charthub/active"

if [[ -f "$KIOSK_ENV_FILE" ]]; then
    # shellcheck disable=SC1090
    source "$KIOSK_ENV_FILE"
fi

SERVER_BINARY="${CHARTHUB_SERVER:-$SERVER_BINARY}"

SERVER_PID=""

if [[ -f "$SERVER_ENV_FILE" ]]; then
    # Export all key=value pairs from the environment file for the server process.
    set -a
    # shellcheck disable=SC1090
    source "$SERVER_ENV_FILE"
    set +a
    log_line "loaded server env: $SERVER_ENV_FILE"
else
    log_line "server env file not found: $SERVER_ENV_FILE"
fi

# Ensure kiosk-safe defaults even if env file is missing/incomplete.
export ServerPaths__ConfigRoot="${ServerPaths__ConfigRoot:-${ACTIVE_ROOT}/config}"
export ServerPaths__ChartHubRoot="${ServerPaths__ChartHubRoot:-${ACTIVE_ROOT}/data}"
export ServerPaths__DownloadsDir="${ServerPaths__DownloadsDir:-${ACTIVE_ROOT}/data/downloads}"
export ServerPaths__StagingDir="${ServerPaths__StagingDir:-${ACTIVE_ROOT}/data/staging}"
export ServerPaths__CloneHeroRoot="${ServerPaths__CloneHeroRoot:-${ACTIVE_ROOT}/music}"
export ServerPaths__SqliteDbPath="${ServerPaths__SqliteDbPath:-${ACTIVE_ROOT}/db/charthub.db}"
export ServerLogging__LogDirectory="${ServerLogging__LogDirectory:-${ACTIVE_ROOT}/logs}"
log_line "effective config root: ${ServerPaths__ConfigRoot}"
log_line "effective sqlite path: ${ServerPaths__SqliteDbPath}"

if [[ ! -x "$SERVER_BINARY" ]]; then
    log_line "server binary missing or not executable: $SERVER_BINARY"
    # Keep the session alive so LightDM does not loop; operator can SSH in and
    # repair deployment paths without fighting relogin churn.
    wait "$OPENBOX_PID"
    exit 0
fi

start_server() {
    server_dir="$(dirname "$SERVER_BINARY")"
    log_line "launching server: $SERVER_BINARY"
    (
        cd "$server_dir"
        ./"$(basename "$SERVER_BINARY")"
    ) >> "$SERVER_LOG" 2>&1 &
    SERVER_PID=$!
    log_line "server pid=${SERVER_PID}"
}

cleanup() {
    log_line "cleanup: stopping server pid=${SERVER_PID:-none} openbox pid=${OPENBOX_PID}"
    if [[ -n "${SERVER_PID}" ]]; then
        kill "$SERVER_PID" 2>/dev/null || true
    fi
    kill "$OPENBOX_PID" 2>/dev/null || true
}

trap cleanup EXIT INT TERM

# Keep kiosk session stable: if server exits, log and restart it while
# Openbox is still alive. This avoids LightDM login-loop churn.
while kill -0 "$OPENBOX_PID" 2>/dev/null; do
    start_server
    wait "$SERVER_PID"
    server_status=$?
    log_line "server exited with status ${server_status}; restarting in 2s"
    sleep 2
done

log_line "openbox exited; ending kiosk session"
exit 0
