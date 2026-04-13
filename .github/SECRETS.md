# GitHub Secrets Reference

This document defines GitHub Actions secrets required for ChartHub release and deployment workflows.

## BackupApi Secrets (existing)

These are used by BackupApi Docker deployments in `release.yml`.

- `PSQL_CONTAINER_NAME`
- `PSQL_PORT`
- `PSQL_USER`
- `PSQL_PASSWORD`
- `PSQL_DB`
- `INTERNAL`
- `DB_VOLUME`
- `API_PORT`
- `BACKUP_DOWNLOADS_HOST_PATH`
- `BACKUP_IMAGES_HOST_PATH`
- `RHYTHMVERSE_TOKEN`
- `BACKUP_SYNC_ENABLED`

## ChartHub.Server Secrets (bundled executable deploy)

ChartHub.Server no longer deploys via Docker/GHCR. It deploys as a Linux self-contained bundle and runs under systemd.

### Repository-level secrets

These are required for Server runtime config and can be stored as repository secrets.

- `CHARTHUB_SERVER_JWT_SIGNING_KEY`
- `CHARTHUB_SERVER_JWT_ISSUER`
- `CHARTHUB_SERVER_JWT_AUDIENCE`
- `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS`
- `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES`
- `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY`

Formatting note:
- `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS` and `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES` can be either:
	- a single value, or
	- a comma-separated list.
- Do not add quotes around entries.
- Example: `client-id-desktop.apps.googleusercontent.com,client-id-android.apps.googleusercontent.com`

### Environment-level secrets (`server-production`)

Server deploy is stable-only and targets the `server-production` environment.

- `CHARTHUB_SERVER_CHARTHUB_PORT` (optional, defaults to `5180`)

## Runtime mapping used by deploy workflow

The Server deploy job writes `/srv/appdata/charthub/config/charthub-server.env` and maps secrets to app config keys:

- `CHARTHUB_SERVER_JWT_SIGNING_KEY` -> `Auth__JwtSigningKey`
- `CHARTHUB_SERVER_JWT_ISSUER` -> `Auth__Issuer`
- `CHARTHUB_SERVER_JWT_AUDIENCE` -> `Auth__Audience`
- `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS` -> `Auth__AllowedEmails__0`
- `Auth__AccessTokenMinutes` is set to `60` by deploy workflow
- `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES` -> `GoogleAuth__AllowedAudiences__0`
- `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY` -> `GoogleDrive__ApiKey`

## Fixed host paths for Server deploy

These paths are now fixed in workflow/systemd configuration:

- Install root: `/srv/appdata/charthub`
- Config root: `/srv/appdata/charthub/config`
- SQLite DB: `/srv/appdata/charthub/db/charthub.db`
- Logs: `/srv/appdata/charthub/logs`
- ChartHub data root: `/srv/appdata/charthub/data`
- Clone Hero root: `/srv/appdata/charthub/music`

## Validation checklist

- BackupApi secrets exist in repository settings.
- Server repository secrets exist in repository settings.
- `server-production` environment exists.
- `CHARTHUB_SERVER_CHARTHUB_PORT` is set in `server-production` if not using default 5180.

