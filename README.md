# ChartHub

ChartHub is a cross-platform Avalonia client for browsing and managing custom rhythm game charts, with desktop and Android targets.

## Repository layout

- `ChartHub/`: Main app project (`net10.0` desktop and optional `net10.0-android`)
- `ChartHub.BackupApi/`: Backup API for mirroring RhythmVerse catalog data into a local database
- `ChartHub.Tests/`: xUnit test suite
- `ChartHub.BackupApi.Tests/`: xUnit tests for the backup API
- `ChartHub.sln`: Solution entry point

## Requirements

- .NET SDK 10.0
- For Android builds:
	- Android SDK installed at `${HOME}/Android/Sdk`
	- Android emulator or physical Android device

## Quick start

From repository root:

```bash
dotnet build ChartHub/ChartHub.csproj
dotnet run --project ChartHub/ChartHub.csproj
```

## Run tests

```bash
dotnet test ChartHub.Tests/ChartHub.Tests.csproj
```

## Data Sources

- `RhythmVerse` and `Chorus Encore` are both available as search/download sources in the app.
- Downloads from either source can be routed through the same local destination flow.

## Library Catalog

- ChartHub stores source membership metadata in `library-catalog.db` under the app config directory.
- Source IDs are tracked per provider (`rhythmverse`, `encore`) to support `In Library` badges across views.

## Android build/install

Build Android target:

```bash
dotnet build ChartHub/ChartHub.csproj -p:EnableAndroid=true -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

Install to emulator/device:

```bash
dotnet build ChartHub/ChartHub.csproj -t:Install -p:EnableAndroid=true -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

You can also use the workspace tasks in `.vscode/tasks.json` for `build`, `run`, `build-android`, and emulator flows.

## Configuration

- Runtime defaults are in `ChartHub/appsettings.json`.
- Local developer secrets are loaded using user-secrets (`UserSecretsId` is set in `ChartHub/ChartHub.csproj`).
- Current API authentication key name is `rhythmverseToken` for backend compatibility.

## RhythmVerse Backup API

The Backup API mirrors RhythmVerse song metadata into a local database for read/query access and download redirection.

### Client compatibility routes

To match the ChartHub client contract, the Backup API exposes these form POST endpoints:

- `POST /api/all/songfiles/list`
- `POST /api/all/songfiles/search/live`

Supported form fields:

- `instrument` (repeatable)
- `author`
- `sort[0][sort_by]`
- `sort[0][sort_order]`
- `data_type`
- `text` (search route)
- `page`
- `records`

Current compatibility semantics:

- Author filtering is exact `AuthorId` match.
- Instrument filtering matches songs where any selected instrument maps to a non-null difficulty field.
- Sorting supports a broad allow-list and falls back to default ordering when an unknown sort key is provided.

### Sync model

- Sync is designed as a full reconciliation sweep.
- Each scheduled run starts from page 1 and continues until it reaches a terminal page:
	- an empty page, or
	- a short final page where `returned < records`
- Songs seen during the active run are stamped with the run identifier.
- After a completed run, songs not seen in that run are soft-deleted.
- If a previously soft-deleted song reappears in a later run, it is restored automatically.
- If a run stops early because of retry exhaustion, cancellation, or `MaxPagesPerRun`, reconciliation is left incomplete and no unseen songs are soft-deleted.

### Default cadence

- `Sync.IntervalMinutes` now defaults to `10080`, which is one week.
- `Sync.RecordsPerPage` and `Sync.MaxPagesPerRun` remain the main safety controls for sizing a full sweep.
- If `MaxPagesPerRun` is too low to reach the terminal page, the run will remain incomplete by design.

### Sync health endpoint

The Backup API exposes `GET /api/rhythmverse/health/sync`.

Response fields:

- `last_success_utc`: UTC timestamp of the last completed reconciliation run
- `lag_seconds`: age in seconds of `last_success_utc`
- `total_available`: last reported upstream total from the sync process
- `last_record_updated_unix`: highest upstream `record_updated` seen by the current sync implementation
- `reconciliation_current_run_id`: current or most recent reconciliation run id
- `reconciliation_started_utc`: when the current or most recent run started
- `reconciliation_completed_utc`: when the most recent completed run finished
- `reconciliation_in_progress`: `true` when a run has started and has not yet completed
- `last_run_completed`: `true` when the most recent run finished completely; `false` when in progress, interrupted, or no run has ever completed

### Current upstream constraint

- The upstream RhythmVerse endpoint currently ignores the `updatedSince` input used by the client code.
- Because of that, correctness depends on full-run reconciliation, not incremental watermark filtering.

### Operational notes

- Apply Backup API EF migrations before deploying reconciliation changes.
- Public Backup API song and download lookups exclude soft-deleted rows by default.
- Soft delete is intentional: this is a backup catalog, so rows removed upstream are hidden from normal reads but retained in persistence.

## Local Sync API (Desktop <-> Android)

Machine-readable contract: see `openapi.yaml` at repository root.
Interactive docs: open `docs/swagger-ui.html` in a browser or static host.

- Desktop listener is fixed to `http://0.0.0.0:15123/`.
- Companion-facing pair/bootstrap URLs are auto-resolved from LAN interfaces.
- If no routable LAN IPv4 is available, ChartHub falls back to loopback (`http://127.0.0.1:15123`).
- `GET /health` is unauthenticated and returns `{ "status": "ok" }`.
- `POST /api/pair/claim` is unauthenticated and exchanges a pair code for the sync token.
- All other `/api/*` endpoints require one of:
	- `X-ChartHub-Sync-Token: <token>`
	- `Authorization: Bearer <token>`
- Token source: `Runtime.SyncApiAuthToken` in `ChartHub/appsettings.json`.
- Pair code source: `Runtime.SyncApiPairCode` in `ChartHub/appsettings.json`.
- Pair codes are one-time use and expire based on `Runtime.SyncApiPairCodeTtlMinutes`.
- Successful pair claims update `Runtime.SyncApiLastPairedDeviceLabel`, `Runtime.SyncApiLastPairedAtUtc`, and append to `Runtime.SyncApiPairingHistoryJson` (last 10 entries).
- Optional override gate: `Runtime.AllowSyncApiStateOverride` (default `false`).

### Endpoints

1. `GET /api/version`
2. `POST /api/pair/claim`
3. `GET /api/ingestions?state=<state>&source=<source>&sort=Updated&desc=true|false&limit=1..500`
4. `GET /api/ingestions/{id}`
5. `POST /api/ingestions`
6. `POST /api/ingestions/{id}/events`
7. `POST /api/ingestions/{id}/actions/retry`
8. `POST /api/ingestions/{id}/actions/install`
9. `POST /api/ingestions/{id}/actions/open-folder`

### `GET /api/version` response body

```json
{
	"api": "ingestion-sync",
	"version": "1.0.0",
	"supports": {
		"ingestions": true,
		"events": true,
		"fromStateOverride": true,
		"metadata": true,
		"desktopLibraryStatus": true,
		"desktopState": true
	},
	"runtime": {
		"allowSyncApiStateOverride": false,
		"maxRequestBodyBytes": 65536,
		"bodyReadTimeoutMs": 1000,
		"mutationWaitTimeoutMs": 250,
		"slowRequestThresholdMs": 500,
		"telemetry": {
			"startedAtUtc": "2026-03-20T12:34:56.0000000+00:00",
			"requestsTotal": 12,
			"slowRequestsTotal": 1,
			"busyMutationRejectionsTotal": 0,
			"clientErrorsTotal": 2,
			"serverErrorsTotal": 0
		}
	}
}
```

### `POST /api/ingestions` request body

```json
{
	"source": "googledrive",
	"sourceId": "drive-file-id",
	"sourceLink": "https://drive.google.com/file/d/abc123/view",
	"downloadedLocation": "/path/to/file.zip",
	"sizeBytes": 12345,
	"contentHash": "sha256:...",
	"artist": "Tool",
	"title": "Sober",
	"charter": "Convour/clintilona/nunchuck/DenVaktare"
}
```

### `POST /api/ingestions` response body

```json
{
	"ingestionId": 42,
	"normalizedLink": "https://drive.google.com/file/d/abc123/view",
	"state": "Downloaded",
	"metadata": {
		"artist": "Tool",
		"title": "Sober",
		"charter": "Convour/clintilona/nunchuck/DenVaktare"
	}
}
```

### `GET /api/ingestions/{id}` response body

```json
{
	"item": {
		"IngestionId": 42,
		"Source": "googledrive",
		"SourceId": "drive-file-id",
		"SourceLink": "https://drive.google.com/file/d/abc123/view",
		"Artist": "Tool",
		"Title": "Sober",
		"Charter": "Convour/clintilona/nunchuck/DenVaktare",
		"DisplayName": "song.zip",
		"CurrentState": "Downloaded",
		"DownloadedLocation": "/path/to/file.zip",
		"InstalledLocation": null,
		"IsInDesktopLibrary": false,
		"DesktopLibraryPath": null,
		"UpdatedAtUtc": "2026-03-18T12:34:56.0000000+00:00",
		"Checked": false,
		"UpdatedText": "2026-03-18 12:34:56",
		"CanInstall": true
	}
}
```

### `POST /api/ingestions/{id}/events` request body

```json
{
	"fromState": "Downloaded",
	"toState": "Installed",
	"details": "Android install completed",
	"allowFromStateOverride": false
}
```

- `fromState` is optional. If provided, it must match the persisted ingestion state unless `allowFromStateOverride` is `true`.
- Even when `allowFromStateOverride=true` is sent by the client, the server only honors it when `Runtime.AllowSyncApiStateOverride=true`.

### `POST /api/pair/claim` request body

```json
{
	"pairCode": "PAIR-1234",
	"deviceLabel": "Pixel Companion"
}
```

### `POST /api/pair/claim` response body

```json
{
	"paired": true,
	"token": "<Runtime.SyncApiAuthToken>",
	"apiBaseUrl": "http://192.168.1.55:15123",
	"pairedAtUtc": "2026-03-20T14:22:31.0000000+00:00"
}
```

- Successful claim rotates the desktop pair code immediately.
- Successful claim also records the pairing event in the desktop Settings "Recent Pairings" panel.
- `apiBaseUrl` resolution order:
	- LAN IPv4 auto-resolution from the fixed all-interface listener
	- loopback fallback (`http://127.0.0.1:15123`)

### Error semantics

- `400`: invalid body, invalid JSON, or invalid state transition.
- `409`: `fromState` mismatch with persisted ingestion state when override is not allowed.
- `401`: missing/invalid sync token for `/api/*` routes.
- `401` on `/api/pair/claim`: invalid pair code.
- `410` on `/api/pair/claim`: pair code expired.
- `404`: ingestion id not found.
- `408`: request body upload/read timed out.
- `415`: `Content-Type` is not `application/json`.
- `413`: JSON request body exceeds 64 KiB.
- `503`: sync API mutation queue is busy; retry later.

### Action endpoint semantics

- `POST /api/ingestions/{id}/actions/retry`
	- Moves an ingestion back to `Queued` when state-machine rules allow it.
	- Returns `correlationId` (UUID) for client/desktop log correlation.
	- Returns `202` with `noop=true` if already `Queued`.
	- Returns `409` when retry is invalid for current state.
- `POST /api/ingestions/{id}/actions/install`
	- Uses the latest `Downloaded` asset path for the ingestion and runs desktop install flow.
	- Returns `correlationId` (UUID) for client/desktop log correlation.
	- Returns `202` with `installedDirectories` on success.
	- Returns `409` when no downloaded asset exists or file is missing.
- `POST /api/ingestions/{id}/actions/open-folder`
	- Opens the latest installed directory when available.
	- Falls back to the parent directory of the latest downloaded asset.
	- Returns `correlationId` (UUID) for client/desktop log correlation.
	- Returns `202` with `directory` on success.
	- Returns `409` when no usable folder path exists.

### Dev `curl` examples

Set token once for your shell session:

```bash
export CH_SYNC_TOKEN="<Runtime.SyncApiAuthToken>"
```

Optional (dev-only) override enablement in `ChartHub/appsettings.json`:

```json
{
	"Runtime": {
		"AllowSyncApiStateOverride": true
	}
}
```

Check API health:

```bash
curl -s http://127.0.0.1:15123/health | jq
```

Check API contract version/capabilities:

```bash
curl -s http://127.0.0.1:15123/api/version \
	-H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" | jq
```

Create/update a downloaded ingestion:

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
		"contentHash": "sha256:abc..."
	}' | jq
```

Query queue items by state/source:

```bash
curl -s "http://127.0.0.1:15123/api/ingestions?state=Downloaded&source=googledrive&sort=Updated&desc=true&limit=100" \
	-H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" | jq
```

Query one ingestion by id:

```bash
INGESTION_ID=42
curl -s "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}" \
	-H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" | jq
```

Post a state event for an ingestion id:

```bash
INGESTION_ID=42
curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/events" \
	-H "Content-Type: application/json" \
	-H "Authorization: Bearer ${CH_SYNC_TOKEN}" \
	-d '{
		"fromState": "Downloaded",
		"toState": "ResolvingSource",
		"details": "resume conversion/install flow",
		"allowFromStateOverride": false
	}' | jq
```

Retry an ingestion:

```bash
INGESTION_ID=42
curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/actions/retry" \
	-H "Content-Type: application/json" \
	-H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
	-d '{}' | jq
```

Install an ingestion's downloaded asset:

```bash
INGESTION_ID=42
curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/actions/install" \
	-H "Content-Type: application/json" \
	-H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
	-d '{}' | jq
```

Open an ingestion folder on desktop:

```bash
INGESTION_ID=42
curl -s -X POST "http://127.0.0.1:15123/api/ingestions/${INGESTION_ID}/actions/open-folder" \
	-H "Content-Type: application/json" \
	-H "X-ChartHub-Sync-Token: ${CH_SYNC_TOKEN}" \
	-d '{}' | jq
```

## Notes

- None

## License

See `LICENSE.txt`.
