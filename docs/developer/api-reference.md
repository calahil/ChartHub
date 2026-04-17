# ChartHub Server API Reference

Both the Desktop and Android clients communicate with a user-deployed **ChartHub Server** instance over its HTTP/WebSocket API. All endpoints except the health check and auth exchange require a server-issued JWT (`Authorization: Bearer <token>`).

---

## Authentication

### Exchange Google ID token for server JWT

```
POST /api/v1/auth/exchange
Content-Type: application/json
(no auth required)
```

**Request body:**

```json
{ "googleIdToken": "<google_id_token_from_pkce_flow>" }
```

**Response (200 OK):**

```json
{
  "accessToken": "<server_jwt>",
  "expiresAtUtc": "2026-04-17T14:00:00Z"
}
```

**Error responses:**

| Status | Error key | Meaning |
|---|---|---|
| `400` | `google_id_token_required` | Token field missing or empty |
| `400` | `invalid_google_id_token` | Google rejected the token |
| `400` | `invalid_google_id_token_payload` | Token payload could not be parsed |
| `403` | — | Email not in server allowlist |
| `503` | — | Google validation service unreachable |

---

## Health

```
GET /health
(no auth required)
```

Returns `{ "status": "ok" }` when the server is ready.

---

## Downloads (Ingestion Jobs)

All endpoints under `/api/v1/downloads` require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/downloads/jobs` | Create a new download job |
| `GET` | `/api/v1/downloads/jobs` | List all download jobs |
| `GET` | `/api/v1/downloads/jobs/{jobId}` | Get a specific job |
| `POST` | `/api/v1/downloads/jobs/{jobId}/retry` | Retry a failed job |
| `POST` | `/api/v1/downloads/jobs/{jobId}/cancel` | Cancel a job |

---

## Clone Hero Song Library

All endpoints under `/api/v1/clonehero` require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/clonehero/songs` | List all songs in the library |
| `GET` | `/api/v1/clonehero/songs/{songId}` | Get song details |
| `DELETE` | `/api/v1/clonehero/songs/{songId}` | Soft-delete a song |
| `POST` | `/api/v1/clonehero/songs/{songId}/restore` | Restore a soft-deleted song |

---

## Volume Control

All endpoints under `/api/v1/volume` require JWT auth. Returns `501` if the platform does not support volume control.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/volume` | Get master volume and per-session volumes |
| `POST` | `/api/v1/volume/master` | Set master volume |
| `POST` | `/api/v1/volume/sessions/{sessionId}` | Set per-application session volume |
| `GET` | `/api/v1/volume/stream` | SSE stream of volume snapshots |

---

## App Launcher (Desktop Entries)

All endpoints under `/api/v1/desktopentries` require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/desktopentries` | List desktop entries and runtime status |
| `POST` | `/api/v1/desktopentries/{entryId}/execute` | Launch an application |
| `POST` | `/api/v1/desktopentries/{entryId}/kill` | Terminate a running application (SIGTERM) |
| `POST` | `/api/v1/desktopentries/refresh` | Refresh the desktop entry catalog and icon cache |

---

## Virtual Input (Android-exclusive client feature)

All endpoints under `/api/v1/input` require JWT auth. These are WebSocket endpoints — the HTTP handshake is authenticated, then the connection upgrades to WebSocket.

| Route | Transport | Purpose |
|---|---|---|
| `GET /api/v1/input/controller/ws` | WebSocket | Virtual gamepad (buttons + D-pad) |
| `GET /api/v1/input/touchpad/ws` | WebSocket | Virtual mouse (pointer + buttons) |
| `GET /api/v1/input/keyboard/ws` | WebSocket | Virtual keyboard (key codes + characters) |

Requires Linux `uinput` access on the server host. Returns `503` if `uinput` is unavailable.

---

## HUD Status (Internal — loopback only)

```
GET /api/v1/hud/status  (SSE, no auth, loopback-only)
```

Used exclusively by the ChartHub Hud process running on the same machine as the server. Not accessible from external clients — restricted to `127.0.0.1` by a loopback guard middleware.

---

## Error Model

All error responses follow the same shape:

```json
{ "error": "<machine_readable_key>" }
```

Common status codes:

| Status | Meaning |
|---|---|
| `401` | Missing or invalid JWT |
| `403` | Valid JWT, email not allowlisted |
| `404` | Resource not found |
| `409` | Conflict (e.g. process already running) |
| `501` | Feature not supported on this platform |
| `503` | Upstream service (Google, uinput) unavailable |
