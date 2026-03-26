# ChartHub Sync API

This document covers the desktop-to-Android sync API used by ChartHub's companion flow.

For the machine-readable contract, use [openapi.yaml](../openapi.yaml). For an interactive view, open [docs/swagger-ui.html](swagger-ui.html).

## Overview

The sync API is hosted by desktop ChartHub and consumed by the Android companion.

Key behavior:

- Desktop listens on `http://0.0.0.0:15123/`
- Companion-facing URLs are auto-resolved from LAN interfaces when possible
- If no routable LAN IPv4 is available, the desktop falls back to loopback
- `/health` is unauthenticated
- `/api/pair/claim` is unauthenticated and exchanges a pair code for the sync token
- Other `/api/*` endpoints require either `X-ChartHub-Sync-Token` or `Authorization: Bearer <token>`

## Authentication Model

Runtime settings control the sync handshake:

- `Runtime.SyncApiAuthToken`
- `Runtime.SyncApiPairCode`
- `Runtime.SyncApiPairCodeTtlMinutes`
- `Runtime.AllowSyncApiStateOverride`

After a successful pair claim:

- the desktop rotates the pair code
- the desktop records pairing metadata and history
- the companion receives a token and desktop API base URL

## Core Endpoints

1. `GET /health`
2. `GET /api/version`
3. `POST /api/pair/claim`
4. `GET /api/ingestions`
5. `GET /api/ingestions/{id}`
6. `POST /api/ingestions`
7. `POST /api/ingestions/{id}/events`
8. `POST /api/ingestions/{id}/actions/retry`
9. `POST /api/ingestions/{id}/actions/install`
10. `POST /api/ingestions/{id}/actions/open-folder`

## Example Flows

### Health check

```bash
curl -s http://127.0.0.1:15123/health | jq
```

Expected shape:

```json
{
  "status": "ok"
}
```

### Claim a pair code

```bash
curl -s -X POST http://127.0.0.1:15123/api/pair/claim \
  -H "Content-Type: application/json" \
  -d '{
    "pairCode": "PAIR-1234",
    "deviceLabel": "Pixel Companion"
  }' | jq
```

Expected shape:

```json
{
  "paired": true,
  "token": "<Runtime.SyncApiAuthToken>",
  "apiBaseUrl": "http://192.168.1.55:15123",
  "pairedAtUtc": "2026-03-20T14:22:31.0000000+00:00"
}
```

### Check version and capabilities

Set the token once for your shell session:

```bash
export CH_SYNC_TOKEN="<Runtime.SyncApiAuthToken>"
```

Then query version and capability metadata:

```bash
curl -s http://127.0.0.1:15123/api/version \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" | jq
```

### Create or update an ingestion

```bash
curl -s -X POST http://127.0.0.1:15123/api/ingestions \
  -H "Content-Type: application/json" \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
  -d '{
    "source": "googledrive",
    "sourceId": "drive-file-id",
    "sourceLink": "https://drive.google.com/file/d/abc123/view",
    "downloadedLocation": "/storage/emulated/0/Download/song.zip",
    "sizeBytes": 12345,
    "contentHash": "sha256:abc123",
    "artist": "Tool",
    "title": "Sober",
    "charter": "Convour/clintilona/nunchuck/DenVaktare"
  }' | jq
```

### Query queue items

```bash
curl -s "http://127.0.0.1:15123/api/ingestions?state=Downloaded&source=googledrive&sort=Updated&desc=true&limit=100" \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" | jq
```

### Query a single ingestion

```bash
INGESTION_ID=42
curl -s "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}" \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" | jq
```

### Post a state event

```bash
INGESTION_ID=42
curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/events" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${CH_SYNC_TOKEN}" \
  -d '{
    "fromState": "Downloaded",
    "toState": "Installed",
    "details": "Android install completed",
    "allowFromStateOverride": false
  }' | jq
```

### Retry, install, or open-folder actions

```bash
INGESTION_ID=42

curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/actions/retry" \
  -H "Content-Type: application/json" \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
  -d '{}' | jq

curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/actions/install" \
  -H "Content-Type: application/json" \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
  -d '{}' | jq

curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/actions/open-folder" \
  -H "Content-Type: application/json" \
  -H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
  -d '{}' | jq
```

## Error Semantics

Common responses include:

- `400`: invalid body, invalid JSON, or invalid state transition
- `401`: missing or invalid sync token
- `401` on `/api/pair/claim`: invalid pair code
- `404`: ingestion not found
- `408`: request body read timed out
- `409`: invalid state transition or action for the current ingestion state
- `410`: pair code expired
- `413`: request body exceeds size limit
- `415`: unsupported `Content-Type`
- `503`: mutation queue is busy

## Operational Notes

- The sync API is intended for local network companion workflows, not public internet exposure
- The desktop app must remain running while pairing and syncing
- If LAN address resolution fails, the companion may only receive a loopback URL, which is not usable from another device
- Use [openapi.yaml](../openapi.yaml) as the source of truth for schemas and response shapes