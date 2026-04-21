#!/usr/bin/env bash
# start-kiosk-session.sh
# X session script launched by LightDM for the charthub-kiosk session.
#
# ChartHub.Server is owned by systemd (charthub-server.service) and runs
# headlessly. It spawns the HUD as a child process once X is available.
# This script's only job is to:
#   1. Disable screensaver / DPMS (TV stays on)
#   2. Run Openbox as the window manager
# When Openbox exits, the X session ends and LightDM relaunches it.

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
# 3. Run Openbox as the window manager
#    exec replaces this shell — when Openbox exits LightDM relaunches the session
# ---------------------------------------------------------------------------
exec openbox
