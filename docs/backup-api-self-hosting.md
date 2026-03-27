# ChartHub Backup API Self-Hosting

This document covers the operational side of running the RhythmVerse Backup API locally.

For the container-specific notes that already existed in the repo, see [ChartHub.BackupApi/README.docker.md](../ChartHub.BackupApi/README.docker.md).

## What It Does

The Backup API mirrors RhythmVerse song metadata into a local database and provides read, search, image, and download redirection support for the ChartHub client.

It is useful when you want:

- a local mirror for RhythmVerse metadata
- download redirection through a local service
- persistent local caches for downloads and images
- more resilient local access when upstream behavior changes or becomes unstable

## Compose Topology

The repository's [docker-compose.yml](../docker-compose.yml) defines two services:

- `db`: PostgreSQL
- `backup-api`: the ASP.NET Backup API service

The compose setup also expects:

- an external Docker network named by `INTERNAL`
- an external Docker volume named `rhythmverse`

Example setup:

```bash
docker network create your-internal-network
docker volume create rhythmverse
```

Then set `INTERNAL=your-internal-network` in `.env`.

## Required Environment Variables

Set these in `.env`:

- `RHYTHMVERSE_TOKEN`
- `PSQL_USER`
- `PSQL_PASSWORD`
- `PSQL_DB`
- `INTERNAL`

Useful optional variables:

- `RHYTHMVERSE_BASE_URL`
- `BACKUP_SYNC_ENABLED`
- `BACKUP_SYNC_INITIAL_DELAY_MINUTES`
- `BACKUP_DOWNLOADS_MODE`
- `BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS`
- `BACKUP_DOWNLOADS_HOST_PATH`
- `BACKUP_IMAGES_HOST_PATH`

## How Compose Maps Settings

The compose service injects these application settings into the Backup API:

- `Database__Provider=postgresql`
- `Database__PostgreSqlConnectionString=Host=db;Port=5432;Database=${PSQL_DB};Username=${PSQL_USER};Password=${PSQL_PASSWORD}`
- `RhythmVerseSource__BaseUrl=${RHYTHMVERSE_BASE_URL:-https://rhythmverse.co/}`
- `RhythmVerseSource__Token=${RHYTHMVERSE_TOKEN:-CHANGE_ME}`
- `Sync__Enabled=${BACKUP_SYNC_ENABLED:-true}`
- `Sync__InitialDelayMinutes=${BACKUP_SYNC_INITIAL_DELAY_MINUTES:-0}`
- `Downloads__Mode=${BACKUP_DOWNLOADS_MODE:-redirect}`
- `Downloads__CacheDirectory=/cache/downloads`
- `Downloads__ExternalRedirectCacheHours=${BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS:-48}`
- `ImageCache__CacheDirectory=/cache/images`

## Starting The Stack

From the repository root:

```bash
docker compose up -d --build
```

The Backup API is exposed at:

- `http://127.0.0.1:5147`

## Cache Persistence

The compose file mounts persistent bind paths for:

- `/cache/downloads`
- `/cache/images`

These map to host paths configured by:

- `BACKUP_DOWNLOADS_HOST_PATH`
- `BACKUP_IMAGES_HOST_PATH`

If you do not set them, compose falls back to:

- `./ChartHub.BackupApi/cache/downloads`
- `./ChartHub.BackupApi/cache/images`

## Health And Verification

The container health check probes:

- `GET /api/rhythmverse/health/sync`

You can verify the API manually with:

```bash
curl -s http://127.0.0.1:5147/health | jq
curl -s http://127.0.0.1:5147/api/rhythmverse/health/sync | jq
```

## Database Provider And Migrations

At runtime, the Backup API supports both SQLite and PostgreSQL, but the compose configuration is wired for PostgreSQL.

Startup behavior:

- the app applies pending EF Core migrations automatically on startup
- this is performed through `dbContext.Database.Migrate()` in [ChartHub.BackupApi/Program.cs](../ChartHub.BackupApi/Program.cs)

For development tooling:

- the design-time factory defaults to PostgreSQL so migrations align with the normal runtime provider
- set `CHART_HUB_DB_PROVIDER=sqlite` if you need SQLite-specific migration generation behavior

Example migration generation command:

```bash
dotnet ef migrations add YourMigrationName --project ChartHub.BackupApi
```

## Backup Sync Behavior

The backup sync process is a full reconciliation sweep.

Important behavior:

- runs start from page 1
- runs continue until an empty page or short final page is reached
- seen songs are stamped with the active run identifier
- unseen songs are soft-deleted only after a completed run
- interrupted or incomplete runs do not soft-delete unseen songs

## Operational Caveats

- The upstream RhythmVerse endpoint currently ignores the `updatedSince` input used by the client logic
- Because of that, correctness depends on full reconciliation rather than incremental watermark sync
- Public Backup API reads exclude soft-deleted rows by default
- Soft delete is intentional so removed upstream rows remain retained in persistence but hidden from normal client reads

## Client Compatibility Routes

To match the ChartHub client contract, the Backup API exposes:

- `POST /api/all/songfiles/list`
- `POST /api/all/songfiles/search/live`

Supported form fields include:

- `instrument`
- `author`
- `sort[0][sort_by]`
- `sort[0][sort_order]`
- `data_type`
- `text`
- `page`
- `records`

## Recommended Operational Checklist

1. Create the external Docker network and volume
2. Populate `.env` with database and RhythmVerse credentials
3. Start the stack with `docker compose up -d --build`
4. Confirm `/health` and `/api/rhythmverse/health/sync` respond
5. Verify cache directories are writable and persisting data
6. Watch logs during the first sync to confirm reconciliation completes cleanly

## Rollback Procedure

To roll back the Backup API to a previous release, identify the last known-good version tag
(e.g. `1.2.3`) from the GitHub Releases page or from GHCR version history.

On the deployment host, for each environment stack you need to roll back:

```bash
# Set the previous image tag
PREVIOUS_TAG=1.2.3
ENV_NAME=dev   # or staging, prod
PROJECT_NAME=charthub-${ENV_NAME}

# Edit your .env file to point to the previous image
# BACKUP_API_IMAGE=ghcr.io/<owner>/charthub-backup-api:${PREVIOUS_TAG}

# Pull the previous image
docker compose --project-name "${PROJECT_NAME}" --env-file .env.deploy pull backup-api

# Redeploy
docker compose --project-name "${PROJECT_NAME}" --env-file .env.deploy up -d
```

After redeploying, verify the stack is healthy:

```bash
# Replace 5147 with your environment's API_PORT value
curl -s http://localhost:5147/health | jq
curl -s http://localhost:5147/api/rhythmverse/health/sync | jq
```

Confirm that the running container uses the expected image:

```bash
docker inspect charthub-dev-backup-api --format '{{ index .Config.Image }}'
```

The database volume is not affected by image rollbacks. EF Core migrations that ran against
the database during the broken release remain applied. If the previous image is incompatible
with the current database schema, a database restore from backup is required before rolling
back the image.