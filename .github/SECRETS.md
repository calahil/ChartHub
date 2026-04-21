# GitHub Secrets Reference

This document defines GitHub Actions secrets required for ChartHub release and deployment workflows.

## Tailscale (repository-level)

Required by all deploy jobs to reach machines on the private Tailscale network.

- `TAILSCALE_AUTHKEY` — reusable ephemeral auth key generated from the Tailscale admin console (ephemeral + reusable options enabled)

## BackupApi Secrets (existing, repository-level)

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
- `BACKUP_API_KEY`

## BackupApi SSH Secrets (environment-level)

Required for all three BackupApi environments (`dev`, `staging`, `production`).
These should be set as environment-level secrets on each GitHub Environment.
They provide SSH access from GitHub-hosted runners (via Tailscale) to the BackupApi host machine.

- `BACKUP_API_SSH_HOST` — Tailscale IP (100.x.x.x) of the BackupApi host machine
- `BACKUP_API_SSH_USER` — SSH user on the BackupApi host (must have Docker access)
- `BACKUP_API_SSH_PRIVATE_KEY` — PEM-encoded private key (ed25519 or RSA); corresponding public key must be in `~/.ssh/authorized_keys` on the host
- `BACKUP_API_SSH_PORT` — optional; SSH port, defaults to `22`

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

### SSH deployment secrets

Required for all three server environments (`server-dev`, `server-staging`, `server-production`).
These should be set as environment-level secrets on each GitHub Environment.

- `SERVER_DEPLOY_SSH_HOST` — hostname or IP of the server machine
- `SERVER_DEPLOY_SSH_USER` — SSH user (must have `sudo` rights for `systemctl` and `install` into `/etc/systemd/system/`)
- `SERVER_DEPLOY_SSH_PRIVATE_KEY` — PEM-encoded private key (ed25519 or RSA); the corresponding public key must be in `~/.ssh/authorized_keys` on the server
- `SERVER_DEPLOY_SSH_PORT` — optional; SSH port, defaults to `22`

### Environment-level secrets

| Environment | Triggered on |
|---|---|
| `server-dev` | `dev` channel |
| `server-staging` | `rc` or `stable` channel |
| `server-production` | `stable` channel only |

Each environment requires the SSH secrets above plus:

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

## Transcription Runner Secrets (Docker Compose deploy)

The runner communicates with ChartHub.Server over the local LAN — no Tailscale
is required for runtime traffic. Tailscale is still used by CI to SSH into the
runner machine for deployment.

### SSH deployment secrets (environment-level)

Required for all three runner environments (`runner-dev`, `runner-staging`, `runner-production`).

- `RUNNER_DEPLOY_SSH_HOST` — Tailscale IP of the runner machine
- `RUNNER_DEPLOY_SSH_USER` — SSH user on the runner machine (must have Docker access)
- `RUNNER_DEPLOY_SSH_PRIVATE_KEY` — PEM-encoded private key (ed25519 or RSA); corresponding public key must be in `~/.ssh/authorized_keys` on the runner machine
- `RUNNER_DEPLOY_SSH_PORT` — optional; SSH port, defaults to `22`

### Runner runtime secrets (environment-level)

- `CHARTHUB_RUNNER_SERVER_URL` — LAN URL of ChartHub.Server (e.g. `http://192.168.1.x:5180`)
- `CHARTHUB_RUNNER_MANAGEMENT_API_KEY` — Static secret for calling `POST /api/v1/runners/registration-tokens`. Never expires. Generate once with `openssl rand -hex 32` and set it in both this GitHub secret and `Runner__ManagementApiKey` in the server's env file.
- `CHARTHUB_RUNNER_NAME` — Human-readable name for this runner instance
- `CHARTHUB_RUNNER_CONCURRENCY` — optional; max concurrent jobs, defaults to `1`

### GitHub Environments

| Environment | Triggered on |
|---|---|
| `runner-dev` | `dev` channel |
| `runner-staging` | `rc` or `stable` channel |
| `runner-production` | `stable` channel only |

### Host paths (fixed)

- Config (config.json): `/srv/appdata/charthub-runner/config`
- Temp data: `/srv/appdata/charthub-runner/data`

Mounted into the container at `/config` and `/data` respectively. The container symlinks `/config` to `~/.charthub-runner` so the app works identically inside or outside Docker.

### First-time registration

Registration is idempotent — CI checks for `/srv/appdata/charthub-runner/config/config.json` before
attempting to register. If the file is absent, CI issues a one-time registration token from the server
and runs `register` inside a throwaway container.

#### How to obtain `CHARTHUB_RUNNER_MANAGEMENT_API_KEY`

Generate a random secret (at least 32 characters) and set it once:

```bash
openssl rand -hex 32
```

This value must be set in **two places**:

1. As the `CHARTHUB_RUNNER_MANAGEMENT_API_KEY` GitHub secret in each runner environment (`runner-dev`, `runner-staging`, `runner-production`).
2. As `Runner__ManagementApiKey` in the server's runtime env file (written by the `deploy-server-*` CI jobs via `CHARTHUB_RUNNER_MANAGEMENT_API_KEY` in each server environment).

The key never expires. CI passes it as `X-Runner-Api-Key: <key>` when calling
`POST /api/v1/runners/registration-tokens`. Once `config.json` exists on the runner machine,
the secret is no longer consulted by the deploy job.

> **Note:** Rotating the key requires updating both the server env and the GitHub secret simultaneously,
> then redeploying the server and re-registering any active runner machines.

## Validation checklist

- `TAILSCALE_AUTHKEY` exists as a repository secret.
- Both deploy target machines are joined to the Tailscale network.
- BackupApi secrets exist in repository settings.
- BackupApi SSH secrets (`BACKUP_API_SSH_HOST`, `BACKUP_API_SSH_USER`, `BACKUP_API_SSH_PRIVATE_KEY`) are set in each BackupApi environment (`dev`, `staging`, `production`).
- The BackupApi SSH public key is present in `authorized_keys` on the BackupApi host for the deploy user.
- Server repository secrets exist in repository settings.
- `server-dev`, `server-staging`, and `server-production` GitHub Environments exist.
- SSH secrets (`SERVER_DEPLOY_SSH_HOST`, `SERVER_DEPLOY_SSH_USER`, `SERVER_DEPLOY_SSH_PRIVATE_KEY`) are set in each server environment.
- The SSH public key is present in `authorized_keys` on the server for the deploy user.
- `CHARTHUB_SERVER_CHARTHUB_PORT` is set in each environment if not using default 5180.
- `runner-dev`, `runner-staging`, and `runner-production` GitHub Environments exist.
- SSH secrets (`RUNNER_DEPLOY_SSH_HOST`, `RUNNER_DEPLOY_SSH_USER`, `RUNNER_DEPLOY_SSH_PRIVATE_KEY`) are set in each runner environment.
- The runner SSH public key is present in `authorized_keys` on the runner machine for the deploy user.
- `CHARTHUB_RUNNER_SERVER_URL`, `CHARTHUB_RUNNER_MANAGEMENT_API_KEY`, and `CHARTHUB_RUNNER_NAME` are set in each runner environment.
- `CHARTHUB_RUNNER_MANAGEMENT_API_KEY` is also set in each **server** environment (`server-dev`, `server-staging`, `server-production`) so the server can write `Runner__ManagementApiKey` to its env file.

