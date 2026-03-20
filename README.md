# ChartHub

ChartHub is a cross-platform Avalonia client for browsing and managing custom rhythm game charts, with desktop and Android targets.

## Repository layout

- `ChartHub/`: Main app project (`net10.0` desktop and optional `net10.0-android`)
- `ChartHub.Tests/`: xUnit test suite
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

## Local Sync API (Desktop <-> Android)

- Desktop hosts a loopback API at `http://127.0.0.1:15123/`.
- `GET /health` is unauthenticated and returns `{ "status": "ok" }`.
- All `/api/*` endpoints require one of:
	- `X-ChartHub-Sync-Token: <token>`
	- `Authorization: Bearer <token>`
- Token source: `Runtime.SyncApiAuthToken` in `ChartHub/appsettings.json`.
- Optional override gate: `Runtime.AllowSyncApiStateOverride` (default `false`).

### Endpoints

1. `GET /api/version`
2. `GET /api/ingestions?state=<state>&source=<source>&sort=Updated&desc=true|false`
3. `GET /api/ingestions/{id}`
4. `POST /api/ingestions`
5. `POST /api/ingestions/{id}/events`
6. `POST /api/ingestions/{id}/actions/retry`
7. `POST /api/ingestions/{id}/actions/install`
8. `POST /api/ingestions/{id}/actions/open-folder`

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
		"desktopLibraryStatus": true
	},
	"runtime": {
		"allowSyncApiStateOverride": false
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

### Error semantics

- `400`: invalid body or invalid state transition.
- `409`: `fromState` mismatch with persisted ingestion state when override is not allowed.
- `401`: missing/invalid sync token for `/api/*` routes.
- `404`: ingestion id not found.

### Action endpoint semantics

- `POST /api/ingestions/{id}/actions/retry`
	- Moves an ingestion back to `Queued` when state-machine rules allow it.
	- Returns `202` with `noop=true` if already `Queued`.
	- Returns `409` when retry is invalid for current state.
- `POST /api/ingestions/{id}/actions/install`
	- Uses the latest `Downloaded` asset path for the ingestion and runs desktop install flow.
	- Returns `202` with `installedDirectories` on success.
	- Returns `409` when no downloaded asset exists or file is missing.
- `POST /api/ingestions/{id}/actions/open-folder`
	- Opens the latest installed directory when available.
	- Falls back to the parent directory of the latest downloaded asset.
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
curl -s "http://127.0.0.1:15123/api/ingestions?state=Downloaded&source=googledrive&sort=Updated&desc=true" \
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

- The app name has been migrated to ChartHub in project identity and package IDs.
- Some backend/API references still intentionally use RhythmVerse naming where tied to the remote service contract.

## License

See `LICENSE.txt`.
