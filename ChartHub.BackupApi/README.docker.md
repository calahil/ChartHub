# ChartHub.BackupApi Docker Notes

## Build and run with compose

From repository root:

```bash
docker compose up -d --build
```

Backup API host URL:

- http://127.0.0.1:5147

## Required environment variables

Set these in `.env` (see `.env.example`):

- `RHYTHMVERSE_TOKEN`
- `PSQL_USER`
- `PSQL_PASSWORD`
- `PSQL_DB`
- `INTERNAL`

Optional:

- `RHYTHMVERSE_BASE_URL` (default `https://rhythmverse.co/`)
- `BACKUP_SYNC_ENABLED`
- `BACKUP_SYNC_INITIAL_DELAY_MINUTES`
- `BACKUP_DOWNLOADS_MODE`
- `BACKUP_DOWNLOADS_EXTERNAL_REDIRECT_CACHE_HOURS`

## Cache mount points

The compose service mounts persistent Docker bind mounts at:

- `/cache/downloads`
- `/cache/images`

Host bind paths are set in `.env`:

- `BACKUP_DOWNLOADS_HOST_PATH`
- `BACKUP_IMAGES_HOST_PATH`

These paths are wired via:

- `Downloads__CacheDirectory=/cache/downloads`
- `ImageCache__CacheDirectory=/cache/images`

## Health check

The container health check uses:

- `GET /api/rhythmverse/health/sync`
