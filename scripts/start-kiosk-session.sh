#!/usr/bin/env bash
# start-kiosk-session.sh
# X session script launched by LightDM for the charthub-kiosk session.
# This script IS the desktop session — it replaces XFCE4/GNOME/etc.
#
# Layout:
#   1. Disable screensaver / DPMS (TV stays on)
#   2. Set display resolution (optional, configure KIOSK_RESOLUTION)
#   3. Start Openbox in the background (Unity/SDL2 games need a WM for
#      window mapping and fullscreen hints; Openbox has zero panel/compositor)
#   4. Exec ChartHub.Server as the foreground process
#      When the server exits the X session ends and LightDM can restart it.

set -euo pipefail

# ---------------------------------------------------------------------------
# Configuration — override via environment or edit below
# ---------------------------------------------------------------------------
# Path to the published ChartHub.Server binary
CHARTHUB_SERVER="${CHARTHUB_SERVER:-/opt/charthub/server/ChartHub.Server}"

# Optional display resolution (e.g. "1920x1080"). Leave empty to skip.
KIOSK_RESOLUTION="${KIOSK_RESOLUTION:-}"

# ---------------------------------------------------------------------------
# 1. Disable screensaver, blanking, and DPMS
# ---------------------------------------------------------------------------
xset s off
xset s noblank
xset -dpms

# ---------------------------------------------------------------------------
# 2. (Optional) force display resolution
# ---------------------------------------------------------------------------
if [[ -n "$KIOSK_RESOLUTION" ]]; then
    xrandr --size "$KIOSK_RESOLUTION" || true
fi

# ---------------------------------------------------------------------------
# 3. Start Openbox with no autostart, no config chrome, no compositor
# ---------------------------------------------------------------------------
# --no-config: skip autostart and menu generation
# &: background so we can exec the server
openbox --no-config &
OPENBOX_PID=$!

# Give Openbox a moment to become the WM
sleep 0.5

# Clean up Openbox when the session script exits
cleanup() {
    kill "$OPENBOX_PID" 2>/dev/null || true
}
trap cleanup EXIT

# ---------------------------------------------------------------------------
# 4. Exec ChartHub.Server (--no-launch-profile prevents launchSettings.json
#    from overriding ASPNETCORE_URLS and breaking health/SSE endpoints)
# ---------------------------------------------------------------------------
exec "$CHARTHUB_SERVER" --no-launch-profile
