# GitHub Environments Setup Guide

This repository uses GitHub Environments for deployment approvals and environment-scoped secrets.

## Required environments

- `dev` (BackupApi dev)
- `staging` (BackupApi staging)
- `productions` (BackupApi production)
- `server-production` (ChartHub.Server stable executable deploy)

## Server environment model

ChartHub.Server is now deployed as a Linux bundled executable and is stable-only.

- No `server-dev` deployment flow
- No `server-staging` deployment flow
- Single target environment: `server-production`

## Configure `server-production`

1. Open repository settings -> Environments -> `server-production`.
2. Add required reviewers for production approval.
3. Add environment secret:
   - `CHARTHUB_SERVER_CHARTHUB_PORT` (optional, defaults to `5180`)

## Host paths used by server-production deploy

The workflow deploys into fixed host paths:

- `/srv/appdata/charthub/releases/{version}`
- `/srv/appdata/charthub/current` (symlink)
- `/srv/appdata/charthub/config`
- `/srv/appdata/charthub/db/charthub.db`
- `/srv/appdata/charthub/logs`
- `/srv/appdata/charthub/data`
- `/srv/appdata/charthub/music`

## Verification

After setup:

- `dev`, `staging`, and `productions` exist for BackupApi.
- `server-production` exists for ChartHub.Server.
- `server-production` has reviewer protection configured.
- `server-production` has `CHARTHUB_SERVER_CHARTHUB_PORT` if needed.

