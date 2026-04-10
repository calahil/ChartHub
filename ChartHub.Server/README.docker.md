# ChartHub.Server Docker Notes

## Build and run with compose

From repository root:

```bash
./scripts/init-charthub-server-dev-paths.sh
docker compose up -d --build
```

ChartHub.Server host URL:

- http://127.0.0.1:5180

## Required environment variables

Set these in `.env` (see root `.env.example`):

- `CHARTHUB_SERVER_JWT_SIGNING_KEY`
- `CHARTHUB_SERVER_ALLOWED_EMAIL_0` (Google account email allowed to exchange ID tokens)
- `INTERNAL`

Recommended:

- `CHARTHUB_SERVER_JWT_ISSUER`
- `CHARTHUB_SERVER_JWT_AUDIENCE`
- `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY` (required for Google Drive file resolution)

## Bind mount paths

The compose service maps these host paths:

- `CHARTHUB_SERVER_CONFIG_PATH` -> `/config`
- `CHARTHUB_SERVER_CHARTHUB_PATH` -> `/charthub`
- `CHARTHUB_SERVER_CLONEHERO_PATH` -> `/clonehero`

These are wired to runtime path options:

- `ServerPaths__ConfigRoot=/config`
- `ServerPaths__ChartHubRoot=/charthub`
- `ServerPaths__DownloadsDir=/charthub/downloads`
- `ServerPaths__StagingDir=/charthub/staging`
- `ServerPaths__CloneHeroRoot=/clonehero`
- `ServerPaths__SqliteDbPath=/config/charthub-server.db`

## Health check

The container health check uses:

- `GET /health`
