# GitHub Secrets Reference

Complete inventory of all GitHub Actions secrets required for ChartHub CI/CD pipelines (BackupApi + ChartHub.Server).

---

## Quick Links

- [BackupApi Secrets (Existing)](#backupapi-secrets-existing)
- [ChartHub.Server Secrets (New)](#charthubserver-secrets-new)
- [Setup Instructions](#setup-instructions)
- [Environment Variable Mappings](#environment-variable-mappings)

---

## BackupApi Secrets (Existing)

These secrets are **already configured** in GitHub and used by the current `release.yml` deployment pipeline.

**Scope**: Repository-level (global across all workflows and environments)

| Secret Name | Required | Type | Description | Example / Notes |
|-------------|----------|------|-------------|-----------------|
| `PSQL_CONTAINER_NAME` | ✅ Yes | String | PostgreSQL container name for docker-compose | `charthub-postgres` |
| `PSQL_PORT` | ✅ Yes | Integer | PostgreSQL port (host-exposed) | `5432` |
| `PSQL_USER` | ✅ Yes | String | PostgreSQL superuser login | `postgres` |
| `PSQL_PASSWORD` | ✅ Yes | String | PostgreSQL password (must be strong) | `<32+ char random string>` |
| `PSQL_DB` | ✅ Yes | String | PostgreSQL database name for BackupApi | `charthub` |
| `INTERNAL` | ✅ Yes | String | Docker internal network name | `charthub-internal` |
| `DB_VOLUME` | ✅ Yes | String | Docker volume name for PostgreSQL data persistence | `charthub-db-data` |
| `API_PORT` | ✅ Yes | Integer | BackupApi exposed port (host) | `5147` |
| `BACKUP_DOWNLOADS_HOST_PATH` | ✅ Yes | Path | Host path for BackupApi downloads cache | `/mnt/backups/downloads` or `/data/backups/downloads` |
| `BACKUP_IMAGES_HOST_PATH` | ✅ Yes | Path | Host path for BackupApi image cache | `/mnt/backups/images` or `/data/backups/images` |
| `RHYTHMVERSE_TOKEN` | ✅ Yes | String | API token for RhythmVerse syncing | (issued by RhythmVerse) |
| `BACKUP_SYNC_ENABLED` | ✅ Yes | Boolean (string) | Enable BackupApi RhythmVerse sync on startup | `true` or `false` |

**How Used**:
- `release.yml` env vars in `deploy-dev`, `deploy-staging`, `deploy-production` jobs
- Passed to docker-compose via `.env.deploy` file
- Maps to `docker-compose.yml` service environment variables

---

## ChartHub.Server Secrets (New)

These secrets **must be created** before Server deployments are enabled in the workflow.

### Repository-Level Secrets

**Scope**: Available to all workflows and all environments (apply once)

| Secret Name | Required | Type | Description | Example / Notes |
|-------------|----------|------|-------------|-----------------|
| `CHARTHUB_SERVER_JWT_SIGNING_KEY` | ✅ Yes | String (base64) | JWT signing key (32+ bytes, base64-encoded) | `<base64 of 32+ random bytes>` |
| `CHARTHUB_SERVER_JWT_ISSUER` | ✅ Yes | String (URL) | JWT issuer claim (must match config) | `https://charthub.example.com` or `charthub-server` |
| `CHARTHUB_SERVER_JWT_AUDIENCE` | ✅ Yes | String | JWT audience claim | `charthub-server` or `charthub-clients` |
| `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS` | ✅ Yes | String (comma-separated) | Allowed email addresses for auth (pipe/space to separate multiple) | `user@example.com` or `user1@example.com,user2@example.com` |
| `CHARTHUB_SERVER_JWT_ACCESS_TOKEN_MINUTES` | ⚠️ Opt | Integer | JWT token TTL in minutes | `60` (recommended) |
| `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES` | ✅ Yes | String (comma-separated) | Google OAuth2 client IDs allowed to authenticate | `<client-id-1>.apps.googleusercontent.com,<client-id-2>.apps.googleusercontent.com` |
| `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY` | ✅ Yes | String | Google Drive API key for file access | (issued by Google Cloud Console) |

**How Used**:
- Passed to ChartHub.Server containers as environment variables via docker-compose
- Maps to `appsettings.json` config sections: `Auth`, `GoogleAuth`, `GoogleDrive`

---

### Environment-Level Secrets

**Scope**: Specific to each GitHub Environment (set per environment: `server-staging`, `server-production`)

These are **per-environment overrides** for host-specific paths and ports. Store these in GitHub Environments, not the repository global secrets.

#### For `server-staging` Environment

| Secret Name | Required | Type | Description | Example |
|-------------|----------|------|-------------|---------|
| `CHARTHUB_SERVER_CONFIG_PATH` | ✅ Yes | Path | Host path for ChartHub.Server config directory | `/etc/charthub-server/config` or `/mnt/config/server` |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | ✅ Yes | Path | Host path for ChartHub data (charts, downloads) | `/data/staging/charthub` or `/mnt/charthub/staging` |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | ✅ Yes | Path | Host path for Clone Hero library root | `/data/staging/clonehero` or `/mnt/clonehero/staging` |
| `CHARTHUB_SERVER_CHARTHUB_PORT` | ✅ Yes | Integer | Exposed port for ChartHub.Server (host) | `5180` |
| `CHARTHUB_SERVER_SQLITE_DB_PATH` | ✅ Yes | Path | Host path for Server's SQLite database file | `/data/staging/charthub/server.db` (contained within `CHARTHUB_SERVER_CHARTHUB_ROOT`) |

#### For `server-production` Environment

| Secret Name | Required | Type | Description | Example |
|-------------|----------|------|-------------|---------|
| `CHARTHUB_SERVER_CONFIG_PATH` | ✅ Yes | Path | Host path for ChartHub.Server config directory | `/etc/charthub-server/config` or `/mnt/config/server-prod` |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | ✅ Yes | Path | Host path for ChartHub data (charts, downloads) | `/data/charthub` or `/mnt/charthub/prod` |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | ✅ Yes | Path | Host path for Clone Hero library root | `/data/clonehero` or `/mnt/clonehero/prod` |
| `CHARTHUB_SERVER_CHARTHUB_PORT` | ✅ Yes | Integer | Exposed port for ChartHub.Server (host) | `5180` |
| `CHARTHUB_SERVER_SQLITE_DB_PATH` | ✅ Yes | Path | Host path for Server's SQLite database file | `/data/charthub/server.db` (contained within `CHARTHUB_SERVER_CHARTHUB_ROOT`) |

**How Used**:
- Loaded by `deploy-server-staging` and `deploy-server-production` jobs
- Written to `.env.deploy` file
- Passed to docker-compose as environment variables
- Mounted as volumes on the container

---

## Setup Instructions

### BackupApi Secrets (Verify Existing)

1. Navigate to **GitHub Repo** → **Settings** → **Secrets and variables** → **Actions**
2. Verify that all 12 secrets listed in [BackupApi Secrets](#backupapi-secrets-existing) are present
3. If any are missing, create them with the appropriate values for your deployment

### ChartHub.Server Repository-Level Secrets (Create)

1. **Generate JWT Signing Key**:
   ```bash
   # Generate 32 random bytes and base64-encode
   openssl rand -base64 32
   # Output example: "Ab1Cd2Ef3Gh4Ij5Kl6Mn7Op8Qr9St0Uv1Wx2Yz3+=" (save this)
   ```

2. **Gather Google OAuth Credentials**:
   - From Google Cloud Console, retrieve OAuth2 client IDs and Drive API key
   - Save the client ID(s) and API key

3. **Create Secrets in GitHub**:
   - Go to **Repo Settings** → **Secrets and variables** → **Actions** → **New repository secret**
   - Add each repository-level secret from the table above:
     - `CHARTHUB_SERVER_JWT_SIGNING_KEY` ← paste base64 output from OpenSSL
     - `CHARTHUB_SERVER_JWT_ISSUER` ← e.g., `charthub-server` or your domain
     - `CHARTHUB_SERVER_JWT_AUDIENCE` ← e.g., `charthub-server`
     - `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS` ← comma-separated list of admin emails
     - `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES` ← comma-separated OAuth client IDs
     - `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY` ← paste the API key

### ChartHub.Server Environment-Level Secrets (Create)

1. **Create GitHub Environments** (see [.github/ENVIRONMENTS.md](.github/ENVIRONMENTS.md) for detailed steps):
   - `server-staging`
   - `server-production`

2. **For each environment, add environment-specific secrets**:
   - Go to **Repo Settings** → **Environments** → **[environment-name]** → **Add environment secret**
   - Add the 5 path/port secrets listed in the table above (with staging or prod-specific paths)

---

## Environment Variable Mappings

This section maps GitHub secrets to the actual environment variables and config keys used by ChartHub.Server.

### Docker Compose Environment Variables

When `release.yml` deployment jobs run, they convert GitHub secrets into environment variables in `.env.deploy`, which are then passed to docker-compose:

| GitHub Secret | Docker Env Var | appsettings.json Path | Example Value |
|---------------|----------------|----------------------|---------------|
| `CHARTHUB_SERVER_JWT_SIGNING_KEY` | `Auth__JwtSigningKey` | `Auth:JwtSigningKey` | `<base64>` |
| `CHARTHUB_SERVER_JWT_ISSUER` | `Auth__Issuer` | `Auth:Issuer` | `charthub-server` |
| `CHARTHUB_SERVER_JWT_AUDIENCE` | `Auth__Audience` | `Auth:Audience` | `charthub-server` |
| `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS` | `Auth__AllowedEmails__0` | `Auth:AllowedEmails:0` | `admin@example.com` |
| `CHARTHUB_SERVER_JWT_ACCESS_TOKEN_MINUTES` | `Auth__AccessTokenMinutes` | `Auth:AccessTokenMinutes` | `60` |
| `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES` | `GoogleAuth__AllowedAudiences__0` | `GoogleAuth:AllowedAudiences:0` | `<client-id>.apps.googleusercontent.com` |
| `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY` | `GoogleDrive__ApiKey` | `GoogleDrive:ApiKey` | `<api-key>` |
| `CHARTHUB_SERVER_CONFIG_PATH` | `ServerPaths__ConfigRoot` | `ServerPaths:ConfigRoot` | `/etc/charthub-server/config` |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | `ServerPaths__ChartHubRoot` | `ServerPaths:ChartHubRoot` | `/data/charthub` |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | `ServerPaths__CloneHeroRoot` | `ServerPaths:CloneHeroRoot` | `/data/clonehero` |
| `CHARTHUB_SERVER_SQLITE_DB_PATH` | `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | `Data Source=/data/charthub/server.db` |

### Volume Mounts in docker-compose

docker-compose binds host paths to container paths:

| GitHub Secret | Host Path (Secret Value) | Container Path | Purpose |
|---------------|--------------------------|-----------------|---------|
| `CHARTHUB_SERVER_CONFIG_PATH` | `/etc/charthub-server/config` | `/config` | Runtime app configuration |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | `/data/charthub` | `/charthub` | Chart charts, downloads, staging |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | `/data/clonehero` | `/clonehero` | Clone Hero library |
| `CHARTHUB_SERVER_SQLITE_DB_PATH` | `/data/charthub/server.db` | Container SQLite db location | App SQLite database |

---

## Validation Checklist

Before deploying, verify:

- [ ] All 12 **BackupApi secrets** are present in Repo Settings → Secrets
- [ ] All 7 **ChartHub.Server repository-level secrets** are present
- [ ] **GitHub Environments** (`backup-api-dev`, `backup-api-staging`, `backup-api-production`, `server-staging`, `server-production`) are created
- [ ] Environment-level secrets are set for `server-staging` and `server-production` (5 path/port secrets each)
- [ ] Host paths for Server exist or will be created on the runner host
- [ ] Google OAuth credentials are valid and accessible

---

## Troubleshooting

### Deployment Job Fails with "Secret Not Found"

- **Cause**: GitHub secret is not defined or has a typo
- **Fix**: Check the exact secret name in the workflow vs. the GitHub Settings
- **Prevention**: Run `grep secrets.CHARTHUB_SERVER release.yml | grep -o 'secrets\.[A-Z_]*' | sort -u` to audit all secret references

### Port Already in Use

- **Cause**: Host port (e.g., 5180) is already bound by another service
- **Fix**: Choose a different port in the environment-level `CHARTHUB_SERVER_CHARTHUB_PORT` secret

### Docker Compose Can't Mount Path

- **Cause**: Host path in secret doesn't exist or runner user lacks permissions
- **Fix**: Ensure paths exist and are writable by the user running the GitHub Actions runner

---

## Related Documentation

- [.github/ENVIRONMENTS.md](.github/ENVIRONMENTS.md) — Step-by-step GitHub Environment creation
- [.github/RUNNER_SETUP.md](.github/RUNNER_SETUP.md) — ChartHub.Server runner installation guide
- [release.yml](.github/workflows/release.yml) — Main CI/CD workflow using these secrets
- [docker-compose.server.yml](../../docker-compose.server.yml) — Server deployment compose file

