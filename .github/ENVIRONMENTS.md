# GitHub Environments Setup Guide

This repository uses GitHub Environments for deployment approvals and environment-scoped secrets.

## Required environments

- `dev` (BackupApi dev)
- `staging` (BackupApi staging)
- `productions` (BackupApi production)
- `server-dev` (ChartHub.Server dev executable deploy)
- `server-staging` (ChartHub.Server staging executable deploy)
- `server-production` (ChartHub.Server production executable deploy)
:wqa
## Server environment model

ChartHub.Server deploys as a Linux bundled executable via SSH from `charthub-build`.

| Environment | Channel | Runner |
|---|---|---|
| `server-dev` | `dev` | `charthub-build` (SSH) |
| `server-staging` | `rc` or `stable` | `charthub-build` (SSH) |
| `server-production` | `stable` only | `charthub-build` (SSH) |

## Configure server environments

For each of `server-dev`, `server-staging`, and `server-production`:

1. Open repository settings -> Environments -> `<environment name>`.
2. Add required reviewers for production approval (`server-production` only).
3. Add environment secrets (see `SECRETS.md` for full list):
   - `SERVER_DEPLOY_SSH_HOST`
   - `SERVER_DEPLOY_SSH_USER`
   - `SERVER_DEPLOY_SSH_PRIVATE_KEY`
   - `SERVER_DEPLOY_SSH_PORT` (optional, defaults to `22`)
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
- `server-dev`, `server-staging`, and `server-production` exist for ChartHub.Server.
- `server-production` has reviewer protection configured.
- SSH secrets are present in each server environment.
- SSH public key is in `authorized_keys` on the server for the deploy user.
- `CHARTHUB_SERVER_CHARTHUB_PORT` is set in each server environment if not using default 5180.

