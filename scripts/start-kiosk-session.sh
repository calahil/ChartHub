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
# 4. Start ChartHub.Server for kiosk mode
# ---------------------------------------------------------------------------
KIOSK_ENV_FILE="/srv/appdata/charthub/active/kiosk.env"
SERVER_BINARY="/srv/appdata/charthub/active/current/ChartHub.Server"

if [[ -f "$KIOSK_ENV_FILE" ]]; then
    # shellcheck disable=SC1090
    source "$KIOSK_ENV_FILE"
fi

SERVER_BINARY="${CHARTHUB_SERVER:-$SERVER_BINARY}"

if [[ ! -x "$SERVER_BINARY" ]]; then
    log_line "server binary missing or not executable: $SERVER_BINARY"
    echo "ERROR: ChartHub.Server is missing or not executable: $SERVER_BINARY" >&2
    exit 1
fi

log_line "launching server: $SERVER_BINARY"
"$SERVER_BINARY" >> "$SERVER_LOG" 2>&1 &
SERVER_PID=$!
log_line "server pid=${SERVER_PID}"

# ---------------------------------------------------------------------------
# 5. Run Openbox as the window manager
# ---------------------------------------------------------------------------
openbox &
OPENBOX_PID=$!
log_line "openbox pid=${OPENBOX_PID}"

cleanup() {
    log_line "cleanup: stopping server pid=${SERVER_PID} openbox pid=${OPENBOX_PID}"
    kill "$SERVER_PID" "$OPENBOX_PID" 2>/dev/null || true
}

trap cleanup EXIT INT TERM

# If either process exits, end the session so LightDM can relaunch it.
wait -n "$SERVER_PID" "$OPENBOX_PID"
wait_status=$?

if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    log_line "server exited; ending kiosk session"
fi

if ! kill -0 "$OPENBOX_PID" 2>/dev/null; then
    log_line "openbox exited; ending kiosk session"
fi

log_line "kiosk session exiting with wait status ${wait_status}"
exit 0
