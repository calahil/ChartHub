# ChartHub API Reference

This document is the authoritative source of truth for all HTTP, SSE, and WebSocket APIs exposed by the two ChartHub backend services.

| Service | Default base URL | Auth model |
|---|---|---|
| **ChartHub Server** | `http://localhost:5000` | Server-issued JWT (`Authorization: Bearer <token>`) |
| **ChartHub BackupApi** | `http://localhost:5147` | Shared API key (`X-Api-Key: <key>`) |

---

## Common Conventions

- All JSON field names are **snake_case** on the wire.
- All timestamps are **ISO 8601 UTC** strings (e.g. `2026-04-17T14:00:00Z`) unless noted as Unix seconds.
- Endpoints that require platform support (Linux uinput, PulseAudio) return **`501 Not Implemented`** when the feature is unavailable.
- SSE streams use the `text/event-stream` content type. Each event has an `event:` name line followed by a `data:` JSON line.
- Rate limits apply per client IP. Excess requests receive `429 Too Many Requests`.

---

## Error Models

### ChartHub Server — RFC 7807 Problem Details

Most error responses follow this shape:

```json
{
  "type": "string (problem type URI)",
  "title": "string",
  "status": 400,
  "detail": "string",
  "instance": "/api/v1/endpoint"
}
```

Some endpoints also use a short error key form:

```json
{ "error": "<machine_readable_key>" }
```

### ChartHub BackupApi — RFC 7807 Problem Details

```json
{
  "type": "about:blank",
  "title": "string",
  "status": 500,
  "detail": "string",
  "instance": "/api/endpoint"
}
```

---

---

# ChartHub Server

**Rate limits:** 120 req/min per IP globally; 10 req/min per IP on the auth endpoint.

---

## Health

```
GET /health
(no auth required)
```

**Response 200:**

```json
{ "status": "ok" }
```

---

## Authentication

### Exchange Google ID token for server JWT

```
POST /api/v1/auth/exchange
Content-Type: application/json
(no auth required)
```

**Request body:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `google_id_token` | string | yes | JWT from Google PKCE flow |

**Response 200:**

| Field | Type | Notes |
|---|---|---|
| `access_token` | string | Server-issued JWT |
| `expires_at_utc` | string | ISO 8601 UTC |

**Error responses:**

| Status | Error key | Meaning |
|---|---|---|
| `400` | `google_id_token_required` | Field missing or empty |
| `400` | `invalid_google_id_token` | Google rejected the token |
| `400` | `invalid_google_id_token_payload` | Token payload could not be parsed |
| `403` | — | Email not in server allowlist |
| `503` | — | Google validation service unreachable |

---

## Downloads (Ingestion Jobs)

All endpoints require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/downloads/jobs` | Create a new download job |
| `GET` | `/api/v1/downloads/jobs` | List all download jobs |
| `GET` | `/api/v1/downloads/jobs/{jobId}` | Get a specific job |
| `DELETE` | `/api/v1/downloads/jobs/{jobId}` | Delete a job |
| `POST` | `/api/v1/downloads/jobs/{jobId}/retry` | Retry a failed job |
| `POST` | `/api/v1/downloads/jobs/{jobId}/cancel` | Cancel a job |
| `POST` | `/api/v1/downloads/jobs/{jobId}/install` | Manually trigger install for a downloaded job |
| `GET` | `/api/v1/downloads/jobs/{jobId}/stream` | SSE stream for a specific job |
| `GET` | `/api/v1/downloads/jobs/stream` | SSE stream for all jobs |
| `GET` | `/api/v1/downloads/jobs/{jobId}/logs` | Get structured log entries for a job |

### Create Job — `POST /api/v1/downloads/jobs`

**Request body:**

| Field | Type | Required | Constraints | Notes |
|---|---|---|---|---|
| `source` | string | yes | | e.g. `"rhythmverse"` |
| `source_id` | string | yes | | Unique ID within the source |
| `display_name` | string | yes | ≤ 500 chars | Human-readable label |
| `source_url` | string | yes | ≤ 2048 chars | Download URL |
| `drum_gen_requested` | boolean | no | | If `true`, auto-queues AI drum transcription after install |

**Response 201:** `DownloadJobResponse` (see schema below)

### DownloadJobResponse

| Field | Type | Notes |
|---|---|---|
| `job_id` | string (GUID) | |
| `source` | string | |
| `source_id` | string | |
| `display_name` | string | |
| `source_url` | string | |
| `stage` | string | `Queued` \| `Downloading` \| `Downloaded` \| `InstallQueued` \| `Staging` \| `Installing` \| `Installed` \| `Failed` |
| `progress_percent` | number | 0–100 |
| `downloaded_path` | string \| null | Server-side filesystem path |
| `staged_path` | string \| null | |
| `installed_path` | string \| null | |
| `installed_relative_path` | string \| null | |
| `artist` | string \| null | |
| `title` | string \| null | |
| `charter` | string \| null | |
| `source_md5` | string \| null | |
| `source_chart_hash` | string \| null | |
| `error` | string \| null | Error message when `stage` is `Failed` |
| `file_type` | string \| null | |
| `drum_gen_requested` | boolean | |
| `created_at_utc` | string | ISO 8601 UTC |
| `updated_at_utc` | string | ISO 8601 UTC |

### JobLogEntryResponse

| Field | Type | Notes |
|---|---|---|
| `timestamp_utc` | string | ISO 8601 UTC |
| `level` | string | `Information` \| `Warning` \| `Error` |
| `event_id` | integer | |
| `category` | string \| null | |
| `message` | string | |
| `exception` | string \| null | Stack trace when present |

### SSE — Single Job Stream (`/jobs/{jobId}/stream`)

```
event: job
data: {"job_id":"...","stage":"...","progress_percent":50,"updated_at_utc":"..."}
```

### SSE — All Jobs Stream (`/jobs/stream`)

```
event: jobs
data: [<DownloadJobResponse>, ...]
```

**Common status codes for all Downloads endpoints:**

| Status | Meaning |
|---|---|
| `201` | Job created |
| `202` | Request accepted (retry, cancel, install) |
| `204` | Job deleted |
| `401` | Missing or invalid JWT |
| `404` | Job not found |
| `409` | Conflict (e.g. job already in terminal state) |

---

## Clone Hero Song Library

All endpoints require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/clonehero/songs` | List all songs |
| `GET` | `/api/v1/clonehero/songs/{songId}` | Get song details |
| `DELETE` | `/api/v1/clonehero/songs/{songId}` | Soft-delete a song |
| `POST` | `/api/v1/clonehero/songs/{songId}/restore` | Restore a soft-deleted song |
| `PATCH` | `/api/v1/clonehero/songs/{songId}/metadata` | Update song.ini metadata fields |

### CloneHeroSongResponse

| Field | Type | Notes |
|---|---|---|
| `song_id` | string | |
| `source` | string | |
| `source_id` | string | |
| `artist` | string | |
| `title` | string | |
| `charter` | string | |
| `source_md5` | string \| null | |
| `source_chart_hash` | string \| null | |
| `source_url` | string \| null | |
| `installed_path` | string \| null | Absolute filesystem path on server |
| `installed_relative_path` | string \| null | |
| `updated_at_utc` | string | ISO 8601 UTC |

All five endpoints return `CloneHeroSongResponse` on success.

### Patch Metadata — `PATCH /api/v1/clonehero/songs/{songId}/metadata`

All fields are optional. Only provided fields are written.

| Field | Type | Notes |
|---|---|---|
| `artist` | string \| null | |
| `title` | string \| null | |
| `charter` | string \| null | |
| `genre` | string \| null | |
| `year` | integer \| null | |
| `difficulty_band` | integer \| null | `-1` = unset, `0`–`6` mapped to Easy → Expert+ |

**Common status codes:**

| Status | Meaning |
|---|---|
| `200` | Success |
| `400` | Invalid patch body |
| `401` | Missing or invalid JWT |
| `404` | Song not found |

---

## Desktop Entries (App Launcher)

All endpoints require JWT auth. Returns `501` if not running on Linux.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/desktopentries` | List entries with runtime status |
| `POST` | `/api/v1/desktopentries/{entryId}/execute` | Launch application |
| `POST` | `/api/v1/desktopentries/{entryId}/kill` | Terminate application (SIGTERM) |
| `POST` | `/api/v1/desktopentries/refresh` | Refresh catalog and icon cache |
| `GET` | `/api/v1/desktopentries/stream` | SSE stream of entry status |
| `GET` | `/desktopentry-icons/{entryId}/{fileName}` | Static icon file (JWT auth required) |

### DesktopEntryItemResponse

| Field | Type | Notes |
|---|---|---|
| `entry_id` | string | |
| `name` | string | Display name |
| `status` | string | `NotRunning` \| `Starting` \| `Running` |
| `process_id` | integer \| null | |
| `icon_url` | string \| null | Points to `/desktopentry-icons/...` |

### DesktopEntryActionResponse (execute / kill)

| Field | Type | Notes |
|---|---|---|
| `entry_id` | string | |
| `status` | string | |
| `process_id` | integer \| null | |
| `message` | string | Human-readable result |

### SSE — Desktop Entry Stream (`/desktopentries/stream`)

```
event: desktopentries
data: {"updated_at_utc":"...","items":[<DesktopEntryItemResponse>, ...]}
```

**Common status codes:**

| Status | Meaning |
|---|---|
| `200` | Success |
| `401` | Missing or invalid JWT |
| `404` | Entry not found |
| `409` | Process already running / not running |
| `501` | Platform does not support desktop entries |

---

## Volume Control

All endpoints require JWT auth. Returns `501` if PulseAudio / platform volume is unavailable.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/volume` | Get master volume and per-session volumes |
| `POST` | `/api/v1/volume/master` | Set master volume |
| `POST` | `/api/v1/volume/sessions/{sessionId}` | Set per-application session volume |
| `GET` | `/api/v1/volume/stream` | SSE stream of volume snapshots |

### VolumeStateResponse

| Field | Type | Notes |
|---|---|---|
| `updated_at_utc` | string | ISO 8601 UTC |
| `master.value_percent` | integer | 0–100 |
| `master.is_muted` | boolean | |
| `sessions` | array | See session object below |
| `supports_per_application_sessions` | boolean | |
| `session_support_message` | string \| null | Reason when `supports_per_application_sessions` is false |

**Session object:**

| Field | Type | Notes |
|---|---|---|
| `session_id` | string | |
| `name` | string | |
| `process_id` | integer \| null | |
| `application_name` | string \| null | |
| `value_percent` | integer | 0–100 |
| `is_muted` | boolean | |

### SetVolumeRequest (master + session)

| Field | Type | Required | Constraints |
|---|---|---|---|
| `value_percent` | integer | yes | 0–100 (validated) |

### VolumeActionResponse

| Field | Type | Notes |
|---|---|---|
| `target_id` | string | |
| `target_kind` | string | `Master` \| `Session` |
| `name` | string | |
| `value_percent` | integer | |
| `is_muted` | boolean | |
| `message` | string | |

### SSE — Volume Stream (`/volume/stream`)

```
event: volume
data: {"updated_at_utc":"...","state":<VolumeStateResponse>}
```

**Common status codes:**

| Status | Meaning |
|---|---|
| `200` | Success |
| `400` | `value_percent` out of range |
| `401` | Missing or invalid JWT |
| `404` | Session not found |
| `501` | Platform does not support volume control |

---

## HUD Status (Loopback-only)

```
GET /api/v1/hud/status/stream
(no auth required — loopback only)
```

Only accepts connections from `127.0.0.1` or `::1`. Returns `403 Forbidden` for all other source IPs. Used exclusively by the ChartHub Hud process on the same machine as the server.

### SSE — HUD Status Stream

```
event: hud-status
data: {"connected_device_count": 2}
```

---

## Virtual Input — WebSocket (Android-exclusive client feature)

All three endpoints use a standard HTTP GET for the JWT-authenticated upgrade handshake, then switch to the WebSocket protocol. Requires Linux `uinput` access on the server host. Returns `503` if `uinput` is unavailable.

| Route | Purpose |
|---|---|
| `GET /api/v1/input/controller/ws` | Virtual gamepad (buttons + D-pad) |
| `GET /api/v1/input/touchpad/ws` | Virtual mouse (pointer + buttons) |
| `GET /api/v1/input/keyboard/ws` | Virtual keyboard (key codes + characters) |

**Status codes on handshake:** `101 Switching Protocols`, `400`, `401`, `503`

### Controller Messages (`/controller/ws`)

All messages are JSON with a `type` discriminator.

```jsonc
// Button press / release
{ "type": "btn", "button_id": "a|b|x|y|select|start", "pressed": true }

// D-pad position
{ "type": "dpad", "x": -1, "y": 0 }   // x, y each: -1 | 0 | 1
```

### Touchpad Messages (`/touchpad/ws`)

```jsonc
// Relative pointer movement
{ "type": "move", "dx": 5, "dy": -3 }

// Mouse button press / release
{ "type": "mousebtn", "side": "left|right", "pressed": true }
```

### Keyboard Messages (`/keyboard/ws`)

```jsonc
// Raw Linux input key code
{ "type": "key", "linux_key_code": 28, "pressed": true }

// IME single character
{ "type": "char", "char": "a" }
```

---

## Runner Management

All endpoints require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/runners/registration-tokens` | Issue a one-time registration token |
| `GET` | `/api/v1/runners` | List registered runners |
| `DELETE` | `/api/v1/runners/{runnerId}` | Remove a runner |

### Issue Registration Token — `POST /api/v1/runners/registration-tokens`

**Request body:**

| Field | Type | Required | Constraints | Notes |
|---|---|---|---|---|
| `ttl_minutes` | integer | no | 1–60 | Default 15. Token expires after this interval |

**Response 200 — RunnerRegistrationTokenResponse:**

| Field | Type | Notes |
|---|---|---|
| `token_id` | string | |
| `plain_token` | string | One-time-use secret — display once, not stored |
| `expires_at_utc` | string | ISO 8601 UTC |

### RunnerSummaryResponse

| Field | Type | Notes |
|---|---|---|
| `runner_id` | string | |
| `runner_name` | string | |
| `max_concurrency` | integer | |
| `registered_at_utc` | string | ISO 8601 UTC |
| `last_heartbeat_utc` | string \| null | ISO 8601 UTC |
| `last_active_job_count` | integer \| null | |
| `is_active` | boolean | |
| `is_online` | boolean | `true` when `is_active` and last heartbeat within 2 minutes |

**Common status codes:**

| Status | Meaning |
|---|---|
| `200` | Success |
| `204` | Runner deleted |
| `401` | Missing or invalid JWT |
| `404` | Runner not found |

---

## Transcription Jobs

All endpoints require JWT auth.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/transcription/scan` | Scan library and enqueue eligible jobs |
| `GET` | `/api/v1/transcription/jobs` | List jobs (filterable by `song_id`, `status`) |
| `GET` | `/api/v1/transcription/results` | List results (filterable by `song_id`) |
| `DELETE` | `/api/v1/transcription/jobs/{jobId}` | Delete a job |
| `POST` | `/api/v1/transcription/jobs/{songId}/retry` | Retry transcription for a song |
| `POST` | `/api/v1/transcription/results/{resultId}/approve` | Approve a transcription result |

### Scan — `POST /api/v1/transcription/scan`

**Response 200:**

```json
{ "enqueued_count": 3 }
```

### Retry — `POST /api/v1/transcription/jobs/{songId}/retry`

**Request body:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `aggressiveness` | string | yes | `Low` \| `Medium` \| `High` |

**Response 200 — TranscriptionJobResponse:**

| Field | Type | Notes |
|---|---|---|
| `job_id` | string | |
| `song_id` | string | |
| `song_folder_path` | string | Server-side path |
| `aggressiveness` | string | |
| `attempt_number` | integer | |

### TranscriptionJobSummaryResponse

| Field | Type | Notes |
|---|---|---|
| `job_id` | string | |
| `song_id` | string | |
| `aggressiveness` | string | `Low` \| `Medium` \| `High` |
| `status` | string | `Queued` \| `Claimed` \| `Processing` \| `Completed` \| `Failed` \| `Yielded` |
| `claimed_by_runner_id` | string \| null | |
| `created_at_utc` | string | ISO 8601 UTC |
| `claimed_at_utc` | string \| null | |
| `completed_at_utc` | string \| null | |
| `failure_reason` | string \| null | |
| `attempt_number` | integer | |

### TranscriptionResultResponse

| Field | Type | Notes |
|---|---|---|
| `result_id` | string | |
| `job_id` | string | |
| `song_id` | string | |
| `aggressiveness` | string | |
| `midi_file_path` | string | Server-side path |
| `completed_at_utc` | string | ISO 8601 UTC |
| `is_approved` | boolean | |
| `approved_at_utc` | string \| null | |

**Common status codes:**

| Status | Meaning |
|---|---|
| `200` | Success |
| `204` | Deleted or approved (no body) |
| `400` | Invalid aggressiveness value |
| `401` | Missing or invalid JWT |
| `404` | Job or result not found |

---

## Runner Protocol

Used by `ChartHub.TranscriptionRunner` agents. Authentication is distinct from the main JWT scheme.

**Auth header:** `Authorization: Runner {runnerId}:{secret}`

HMAC-SHA256 is validated server-side. All routes except `/register` require this header.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/runner/register` | Register a new runner using a registration token |
| `POST` | `/api/v1/runner/heartbeat` | Report liveness and active job count |
| `POST` | `/api/v1/runner/jobs/claim` | Claim the next available transcription job |
| `POST` | `/api/v1/runner/jobs/{jobId}/processing` | Signal job has started processing |
| `GET` | `/api/v1/runner/jobs/{jobId}/audio` | Download source audio (HMAC-signed URL) |
| `POST` | `/api/v1/runner/jobs/{jobId}/audio-url` | Get a short-lived HMAC-signed audio download URL |
| `POST` | `/api/v1/runner/jobs/{jobId}/complete` | Upload MIDI result (`multipart/form-data`) |
| `POST` | `/api/v1/runner/jobs/{jobId}/yield` | Return job to queue without failing |
| `POST` | `/api/v1/runner/jobs/{jobId}/fail` | Mark job as failed with a reason |

### Register — `POST /api/v1/runner/register`

**Request body:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `runner_name` | string | yes | |
| `registration_token` | string | yes | One-time token from `/runners/registration-tokens` |
| `secret` | string | yes | Runner's persistent secret (stored hashed server-side) |
| `max_concurrency` | integer | no | Default 1 |

**Response 200:**

```json
{ "runner_id": "string" }
```

### Heartbeat — `POST /api/v1/runner/heartbeat`

**Request body:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `active_job_count` | integer | yes | Current number of jobs being processed |

**Response 204 — No body.**

### Claim Job — `POST /api/v1/runner/jobs/claim`

- **Response 200:** `TranscriptionJobResponse` — a job was claimed.
- **Response 204:** No job available.

### Audio URL — `POST /api/v1/runner/jobs/{jobId}/audio-url`

**Response 200:**

| Field | Type | Notes |
|---|---|---|
| `url` | string | HMAC-signed URL pointing to the audio download endpoint |
| `expires_at_unix` | integer | Unix timestamp (seconds) |

### Audio Download — `GET /api/v1/runner/jobs/{jobId}/audio`

Query parameters are HMAC-signed by the server when generating the URL via `/audio-url`.

| Query param | Type | Notes |
|---|---|---|
| `sig` | string | Base64URL-encoded HMAC-SHA256 over `{jobId}:{expiresAtUnixSeconds}` using the server's `RunnerAudioSigningKey` (no padding) |
| `exp` | integer | Unix timestamp (seconds) — URL expires at this time |

**Responses:** `200` binary audio, `401` invalid/expired signature, `404` job not found, `410` audio file no longer available.

### Complete Job — `POST /api/v1/runner/jobs/{jobId}/complete`

**Content-Type:** `multipart/form-data`

| Part name | Notes |
|---|---|
| `midi` | Binary MIDI file (`filename="result.mid"`). Must start with magic bytes `4D 54 68 64`. |

**Response 204 — No body.**

**Status codes:** `204`, `400` (invalid MIDI), `403` (runner does not own job), `404`.

### Fail Job — `POST /api/v1/runner/jobs/{jobId}/fail`

**Request body:**

| Field | Type | Required | Notes |
|---|---|---|---|
| `reason` | string \| null | no | Human-readable failure reason |

**Response 204 — No body.**

---

---

# ChartHub BackupApi

Separate service. Proxies and caches RhythmVerse upstream data and assets.

**Rate limit:** 60 req/min per IP (production only).

**Auth:** All endpoints except `/health` and `/api/rhythmverse/health/sync` require the header:

```
X-Api-Key: <key>
```

The server compares keys with constant-time SHA-256 comparison. Missing or invalid key → `401 Unauthorized`.

**Security headers added to all responses:**

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`

---

## Health

| Method | Route | Auth | Purpose |
|---|---|---|---|
| `GET` | `/health` | None | Unauthenticated liveness check |
| `GET` | `/api/rhythmverse/health/sync` | None | Sync metadata for infra monitoring |

### `GET /health`

**Response 200:** `{ "status": "ok" }`

### `GET /api/rhythmverse/health/sync`

**Response 200:**

| Field | Type | Notes |
|---|---|---|
| `last_success_utc` | string \| null | ISO 8601 UTC |
| `lag_seconds` | number \| null | Seconds since last successful sync |
| `total_available` | string \| null | Total song count as numeric string |
| `last_record_updated_unix` | number \| null | Unix timestamp of most recently updated record |
| `reconciliation_current_run_id` | string \| null | |
| `reconciliation_started_utc` | string \| null | ISO 8601 UTC |
| `reconciliation_completed_utc` | string \| null | ISO 8601 UTC |
| `reconciliation_in_progress` | boolean | |
| `last_run_completed` | boolean | |

---

## RhythmVerse Song Proxy

### `GET /api/rhythmverse/songs/{songId}`

| Parameter | Type | Notes |
|---|---|---|
| `songId` | long | RhythmVerse numeric song ID |

**Responses:**

| Status | Meaning |
|---|---|
| `200` | Raw upstream song JSON (`Content-Type: application/json`) |
| `404` | Song not found or soft-deleted |
| `500` | Stored payload is invalid |

---

## RhythmVerse Download Proxy

### `GET /api/rhythmverse/download/{fileId}` (also `HEAD`)

| Parameter | Type | Notes |
|---|---|---|
| `fileId` | string | File identifier from upstream |

**Responses:**

| Status | Meaning |
|---|---|
| `302` | Redirect to upstream URL (`Location` header) |
| `400` | `{ "error": "Only redirect mode is currently implemented." }` |
| `404` | File not found |

---

## Compatibility Search / List

These endpoints implement a RhythmVerse-compatible query shape consumed by ChartHub clients.

Both use `Content-Type: application/x-www-form-urlencoded`.

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/all/songfiles/list` | Paginated song list with optional filters |
| `POST` | `/api/all/songfiles/search/live` | Same as list, plus free-text search |

### Request Form Fields

| Field | Type | Required | Constraints | Notes |
|---|---|---|---|---|
| `page` | integer | no | min 1, default 1 | |
| `records` | integer | no | 1–250, default 25 | Clamped server-side |
| `author` | string | no | | Matches `author_id` or `shortname` |
| `instrument` | string (repeatable) | no | | Multiple values use OR semantics |
| `sort[0][sort_by]` | string | no | | Sort field name |
| `sort[0][sort_order]` | string | no | `asc` \| `desc` | |
| `data_type` | string | no | | Legacy field — accepted but ignored |
| `text` | string | no | | **Search only.** Free-text query. Empty behaves like list |

### Response 200

```json
{
  "status": "success",
  "data": {
    "records": {
      "total_available": 1234,
      "total_filtered": 56,
      "returned": 25
    },
    "pagination": {
      "start": 1,
      "records": 25,
      "page": 1
    },
    "songs": [ /* raw upstream song objects */ ]
  }
}
```

---

## Schema Endpoints

| Method | Route | Auth | Response |
|---|---|---|---|
| `GET` | `/api/schemas/rhythmverse-song-list.json` | Required | JSON Schema document (`application/schema+json`) |
| `GET` | `/api/schemas/rhythmverse-song-list.openapi.json` | Required | OpenAPI components schema (`application/json`) |

---

## Image and Asset Proxies

All support `GET` and `HEAD`. Assets are cached to disk on first fetch.

| Route pattern | Cached formats | Cache directory |
|---|---|---|
| `/img/{**path}` | `.png` `.jpg` `.jpeg` `.webp` `.gif` | `./cache/images` |
| `/avatars/{**path}` | Same as above | `./cache/images` |
| `/assets/album_art/{**path}` | Same as above | `./cache/images` |
| `/download_file/{**path}` | `.zip` `.rar` `.7z` `.gz` `.tar` `.bz2` `.sng` `.mid` `.midi` `.chart` `.con` `.dta` `.mogg` | `./cache/downloads` |

**Responses:** `200` binary data (Content-Type inferred from extension), `404` not found or unsupported format.

---

## External Download Proxy

### `GET /downloads/external` (also `HEAD`)

| Query param | Type | Required | Notes |
|---|---|---|---|
| `sourceUrl` | string | yes | External URL to proxy |

**Features:**

- SSRF protection: private IP ranges are blocked. Returns `404` for blocked or invalid URLs.
- MediaFire HTML parsing: extracts direct download link from MediaFire pages automatically.
- Redirect caching: resolved redirect targets are cached for `Downloads:ExternalRedirectCacheHours` (default 48 hours).
- Supports HTTP range requests.

**Responses:** `200` binary file, `404` not found / blocked URL.
