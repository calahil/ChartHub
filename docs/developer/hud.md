# ChartHub Hud

ChartHub Hud is the window manager UI and status display for the ChartHub game console. It runs on a dedicated emulation or media PC as the user-facing interface of the **ChartHub Kiosk** session (Emubox WM) — a purpose-built session that replaces the standard Linux desktop with a minimal, full-screen rhythm game console UI.

---

## Purpose

In the kiosk session, ChartHub Server is the root process. It manages the Hud as a subprocess, providing users with a status display and replacing the desktop while no game is running. The Android app's Input suite (controller, touchpad, keyboard WebSocket connections) is the primary way users interact with the console. The Hud reflects that connection state at a glance.

A game/app launcher view is planned for a future iteration; the current scope is status display only.

---

## Architecture

ChartHub Hud is a separate Avalonia application (`ChartHub.Hud/`) that communicates with ChartHub Server over its local SSE API. It follows the same strict MVVM architecture as the main ChartHub client.

| Path | Purpose |
|---|---|
| `ChartHub.Hud/Views/` | Fullscreen Hud window |
| `ChartHub.Hud/ViewModels/` | Status state and server connectivity orchestration |
| `ChartHub.Hud/Services/` | ChartHub Server SSE status stream consumer |

---

## Layout

The Hud window is fullscreen with no decorations and a dark background.

- **Top strip** (64px, opaque) — spans the full width:
  - Left: the connected Android device's display name
  - Right: a green/red indicator dot (green = Input WebSocket open, red = none)
- **Main area** — the ChartHub logo centered in the remaining space below the strip

When no Android device is connected, the left side of the top strip is empty and the dot is red.

---

## Device Name

The Android app sends a display name when opening Presence and Input WebSocket connections via the `X-Device-Name` HTTP header. The name source is:

1. Android user-assigned device name, when available.
2. `Manufacturer Model` fallback when no user-assigned name is available.

ChartHub Server normalizes this value (trim, collapse whitespace, remove control chars, cap length), tracks it in `InputConnectionTracker`, and broadcasts it to the Hud via the SSE status stream.

Only one Input WebSocket connection is permitted at a time (globally across all three endpoints — controller, touchpad, keyboard). A second connection attempt while one is active is rejected immediately.

---

## Lifecycle

ChartHub Server spawns the Hud process on startup via `HudLifecycleService`. When an Android app launches a DesktopEntry, the server kills the Hud process to free all resources for the game. When that application exits or is killed, the server respawns the Hud.

This kill-and-respawn approach is intentional — the target hardware is resource-constrained.

> The kiosk session itself (Openbox X session, LightDM autologin, and deployment configuration) is documented in the operator setup guide.

---

## Running

The Hud is spawned automatically by ChartHub Server in a kiosk session. For manual testing:

```bash
dotnet run --project ChartHub.Hud/ChartHub.Hud.csproj
```

ChartHub Server must be running on the same host for the status stream to populate.
