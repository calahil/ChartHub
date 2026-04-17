# Advanced ChartHub Docs

This page keeps the technical material that was removed from the root README so the landing page can stay focused on end users.

Use this document if you are:

- Building ChartHub from source
- Running the Backup API locally
- Working on Android builds
- Integrating with the desktop sync API
- Contributing code to the repository

## Repository Layout

- [ChartHub](../ChartHub): main app project for desktop and optional Android targets
- [ChartHub.BackupApi](../ChartHub.BackupApi): backup and mirroring service for RhythmVerse catalog data
- [ChartHub.Server](../ChartHub.Server): server-hosted download and Clone Hero library API
- [ChartHub.Tests](../ChartHub.Tests): test suite for the main app
- [ChartHub.BackupApi.Tests](../ChartHub.BackupApi.Tests): test suite for the backup API
- [ChartHub.Server.Tests](../ChartHub.Server.Tests): test suite for server endpoints and services
- [ChartHub.sln](../ChartHub.sln): solution entry point

## Requirements

- .NET SDK 10.0
- For Android builds:
	- Android SDK installed at `$HOME/Android/Sdk`
	- An emulator or physical Android device

## Local Development Quick Start

From the repository root:

```bash
dotnet build ChartHub/ChartHub.csproj
dotnet run --project ChartHub/ChartHub.csproj
```

## Validation And Tests

Basic test entry point:

```bash
dotnet test ChartHub.Tests/ChartHub.Tests.csproj
```

Repository completion requirements and agent rules are documented in:

- [.governance/AGENTS.md](../.governance/AGENTS.md)
- [.github/copilot-instructions.md](../.github/copilot-instructions.md)

## Android Build And Install

Build the Android target:

```bash
dotnet build ChartHub/ChartHub.csproj -p:EnableAndroid=true -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

Install to an emulator or connected device:

```bash
dotnet build ChartHub/ChartHub.csproj -t:Install -p:EnableAndroid=true -f net10.0-android -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

The workspace also exposes build and run tasks for common desktop and Android flows.

## Configuration Notes

- Runtime defaults live in [ChartHub/appsettings.json](../ChartHub/appsettings.json)
- Local development secrets use the `UserSecretsId` defined in [ChartHub/ChartHub.csproj](../ChartHub/ChartHub.csproj)
- The current backend-compatible API authentication key name is `rhythmverseToken`

## Data Sources And Library Catalog

- ChartHub supports both RhythmVerse and Chorus Encore as search and download sources
- Downloads from both sources are routed through the same local destination flow
- Library source membership is tracked in `library-catalog.db` under the application config directory
- Source IDs are stored per provider, currently including `rhythmverse` and `encore`, to support in-library indicators across views

## Backup API And Self-Hosting

The Backup API mirrors RhythmVerse song metadata into a local database for read access, filtering, and download redirection.

Primary references:

- [docs/backup-api-self-hosting.md](self-hosting/backup-api.md)
- [ChartHub.BackupApi/README.docker.md](../ChartHub.BackupApi/README.docker.md)
- [ChartHub.BackupApi/Program.cs](../ChartHub.BackupApi/Program.cs)

### Docker Setup

The repository includes Docker Compose support for the Backup API and PostgreSQL.

1. Generate local env values:

```bash
./scripts/setup-local-secrets.sh
```

2. Set `RHYTHMVERSE_TOKEN` in `.env.local` (or export it before running the script).
3. Start the stack:

```bash
docker compose up -d --build
```

The script writes `.env.local` and never overwrites `.env`.

The Backup API is exposed on `http://127.0.0.1:5147`.

Cache directories are mounted as bind mounts:

- `/cache/downloads`
- `/cache/images`

Host paths are configured through:

- `BACKUP_DOWNLOADS_HOST_PATH`
- `BACKUP_IMAGES_HOST_PATH`

### Client Compatibility Routes

To match the ChartHub client contract, the Backup API exposes:

- `POST /api/all/songfiles/list`
- `POST /api/all/songfiles/search/live`

Supported form fields include:

- `instrument` as a repeatable field
- `author`
- `sort[0][sort_by]`
- `sort[0][sort_order]`
- `data_type`
- `text`
- `page`
- `records`

Compatibility semantics currently include:

- exact `AuthorId` matching for author filters
- instrument matching when any selected instrument maps to a non-null difficulty field
- broad sort allow-list handling with fallback ordering for unknown sort keys

### Backup Sync Model

- Sync runs as a full reconciliation sweep
- Each run starts from page 1 and continues until it reaches either an empty page or a short final page where `returned < records`
- Songs seen in the current run are marked with that run identifier
- Songs not seen in a completed run are soft-deleted
- Soft-deleted songs are restored automatically if they reappear upstream
- Incomplete runs do not soft-delete unseen songs

### Default Cadence

- `Sync.IntervalMinutes` defaults to `10080`, which is one week
- `Sync.RecordsPerPage` and `Sync.MaxPagesPerRun` are the main controls for shaping sweep size
- If `MaxPagesPerRun` is too low to reach a terminal page, the run remains incomplete by design

### Health Endpoint

The Backup API exposes `GET /api/rhythmverse/health/sync`.

Useful response fields include:

- `last_success_utc`
- `lag_seconds`
- `total_available`
- `last_record_updated_unix`
- `reconciliation_current_run_id`
- `reconciliation_started_utc`
- `reconciliation_completed_utc`
- `reconciliation_in_progress`
- `last_run_completed`

### Upstream Constraint

The upstream RhythmVerse endpoint currently ignores the `updatedSince` input used by the client code, so correctness depends on full-run reconciliation rather than incremental watermark filtering.

### Operational Notes

- Apply Backup API EF migrations before deploying reconciliation changes
- Public Backup API song and download lookups exclude soft-deleted rows by default
- Soft delete is intentional so removed upstream rows are hidden from normal reads while still retained in persistence

## ChartHub.Server And Self-Hosting

ChartHub.Server provides authenticated endpoints for:

- download job orchestration
- source URL resolution and staged installs
- Clone Hero library operations (list/get/delete/restore/install-from-staged)

Primary references:

- [ChartHub.Server/Program.cs](../ChartHub.Server/Program.cs)
- [ChartHub.Server/.env.example](../ChartHub.Server/.env.example)
- [ChartHub.Server/README.docker.md](../ChartHub.Server/README.docker.md)

### Docker Setup

From repository root:

1. Generate local env values:

```bash
./scripts/setup-local-secrets.sh
```

2. Set `RHYTHMVERSE_TOKEN` in `.env.local`.
3. If needed, apply local dotnet user-secrets values too:

```bash
./scripts/setup-local-secrets.sh --apply-user-secrets
```

4. Set `CHARTHUB_SERVER_JWT_SIGNING_KEY` manually only when you want to override the generated value.
5. Set `CHARTHUB_SERVER_ALLOWED_EMAIL_0` to the Google account email you use in the client auth flow.
6. Initialize bind mount folders:

```bash
./scripts/init-charthub-server-dev-paths.sh
```

7. Start the stack:

```bash
docker compose up -d --build
```

ChartHub.Server is exposed on `http://127.0.0.1:5180` by default.

### Health Check

```bash
curl -s http://127.0.0.1:5180/health
```

Expected response:

```json
{
	"status": "ok"
}
```

## Desktop And Android Sync API

The desktop sync API is the transport used between the desktop host and the Android companion.

Primary references:

- [docs/sync-api.md](sync-api.md)
- [openapi.yaml](../openapi.yaml)
- [docs/swagger-ui.html](swagger-ui.html)

### Runtime Model

- Desktop listens on `http://0.0.0.0:15123/`
- Companion-facing bootstrap URLs are resolved from LAN interfaces automatically
- If no routable LAN IPv4 address is available, ChartHub falls back to `http://127.0.0.1:15123`
- `GET /health` is unauthenticated and returns a simple status response
- `POST /api/pair/claim` is unauthenticated and exchanges a pair code for the sync token
- Other `/api/*` endpoints require either `X-ChartHub-Sync-Token` or bearer-token authentication

Configuration values are sourced from [ChartHub/appsettings.json](../ChartHub/appsettings.json), including:

- `Runtime.SyncApiAuthToken`
- `Runtime.SyncApiPairCode`
- `Runtime.SyncApiPairCodeTtlMinutes`
- `Runtime.AllowSyncApiStateOverride`

### Endpoint Summary

The main API surface includes:

1. `GET /api/version`
2. `POST /api/pair/claim`
3. `GET /api/ingestions`
4. `GET /api/ingestions/{id}`
5. `POST /api/ingestions`
6. `POST /api/ingestions/{id}/events`
7. `POST /api/ingestions/{id}/actions/retry`
8. `POST /api/ingestions/{id}/actions/install`
9. `POST /api/ingestions/{id}/actions/open-folder`

Use the OpenAPI document for the request and response schemas rather than copying the raw contract into multiple files.

## Architecture And Contribution Guidance

If you are changing code rather than using the app, start with:

- [.governance/architecture.md](../.governance/architecture.md)
- [.governance/AGENTS.md](../.governance/AGENTS.md)
- [.github/copilot-instructions.md](../.github/copilot-instructions.md)