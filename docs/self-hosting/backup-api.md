# Backup API Self-Hosting

The ChartHub Backup API mirrors RhythmVerse song metadata into a local database. It provides read access, search, image caching, and download redirection — acting as a resilient local proxy when the upstream RhythmVerse service is unstable.

---

## Vision and Long-Term Goal

!!! note "Work in progress"
    The Backup API is functional and self-hostable today. The long-term goal is a distributed mirror network where each self-hosted instance joins a load-balanced pool at `mirror.calahilstudios.com`, providing community redundancy for RhythmVerse data. The database schema will need a rework before that architecture is viable.

The key goals of that vision:

- Each operator runs their own instance and contributes to the pool.
- The load balancer routes clients to available mirrors.
- No single point of failure for song metadata access.

---

## Current Database Schema

The Backup API currently uses a single-tenant schema designed for local mirroring of one upstream source. The schema includes:

- `Songs` — mirrored song records with soft-delete support and run-tracking columns
- `SyncRuns` — reconciliation run metadata (start time, completion, run ID)

### Known Limitations (rework needed for distributed mirror)

| Limitation | Impact |
|---|---|
| Schema is single-source only | Cannot merge data from multiple upstream operators |
| No origin tracking per row | Cannot attribute which mirror a record came from |
| Soft-delete is local | No propagation protocol for deletes across instances |
| No row versioning | Cannot resolve conflicts between mirror instances |

Until the schema rework, each instance operates independently as a personal mirror — which is fully functional for the self-hosting use case today.

---

## Requirements

- Docker and Docker Compose
- A RhythmVerse API token
- An external Docker network and volume (see below)

---

## Compose Setup

The repository's `docker-compose.yml` defines two services:

- `db` — PostgreSQL
- `backup-api` — ASP.NET Backup API

### Create required Docker resources

```bash
docker network create your-internal-network
docker volume create rhythmverse
```

### Required environment variables

Set these in `.env`:

```env
RHYTHMVERSE_TOKEN=yourtoken
PSQL_USER=charthub
PSQL_PASSWORD=yourpassword
PSQL_DB=charthub_backup
INTERNAL=your-internal-network
```

Optional variables:

| Variable | Default | Purpose |
|---|---|---|
| `RHYTHMVERSE_BASE_URL` | `https://rhythmverse.co/` | Upstream base URL |
| `BACKUP_SYNC_ENABLED` | `true` | Enable background sync |
| `BACKUP_SYNC_INITIAL_DELAY_MINUTES` | `0` | Delay before first sync |
| `BACKUP_DOWNLOADS_MODE` | `redirect` | `redirect` or `proxy` |
| `BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS` | `48` | Download URL cache TTL |
| `BACKUP_DOWNLOADS_HOST_PATH` | `./ChartHub.BackupApi/cache/downloads` | Host path for download cache |
| `BACKUP_IMAGES_HOST_PATH` | `./ChartHub.BackupApi/cache/images` | Host path for image cache |

### Start the stack

```bash
docker compose up -d --build
```

The Backup API is exposed at `http://127.0.0.1:5147`.

---

## Sync Behavior

- Sync is a full reconciliation sweep — no incremental watermark (upstream ignores `updatedSince`).
- Each run starts from page 1 and continues until an empty page or a short final page (`returned < records`).
- Songs seen in a run are stamped with that run's ID.
- Songs not seen after a **completed** run are soft-deleted.
- Incomplete runs do not soft-delete unseen songs.
- Soft-deleted songs are restored automatically if they reappear upstream.

Default sync cadence: `Sync.IntervalMinutes` = `10080` (weekly).

---

## Health Check

```bash
curl -s http://127.0.0.1:5147/api/rhythmverse/health/sync | jq
```

Key fields in the response:

- `last_success_utc`
- `lag_seconds`
- `total_available`
- `reconciliation_in_progress`
- `last_run_completed`

---

## Database Migrations

Migrations are applied automatically on startup via EF Core. To generate a new migration manually:

```bash
dotnet ef migrations add YourMigrationName --project ChartHub.BackupApi
```

The design-time factory defaults to PostgreSQL. Set `CHART_HUB_DB_PROVIDER=sqlite` only for SQLite-specific migration generation.

---

## Client Compatibility

The Backup API exposes the same routes the ChartHub client expects:

- `POST /api/all/songfiles/list`
- `POST /api/all/songfiles/search/live`
