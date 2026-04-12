# GitHub Environments Setup Guide

This guide walks through creating the GitHub Environments required for ChartHub CI/CD deployments.

---

## Overview

GitHub Environments provide:
- **Approval gates**: Require manual review before deployment
- **Environment-specific secrets**: Different passwords/paths per stage (dev vs. staging vs. production)
- **Deployment protection rules**: Limit which branches/tags can deploy
- **Audit trail**: Track who approved what and when

We need **6 environments**:
1. `dev` (BackupApi dev, no approval)
2. `staging` (BackupApi staging, requires approval)
3. `productions` (BackupApi production, requires approval)
4. `server-dev` (no approval)
5. `server-staging` (requires approval, once runner is ready)
6. `server-production` (requires approval, once runner is ready)

---

## Prerequisites

- You must have **Admin** or **Maintainer** access to the GitHub repository
- The repository must be using GitHub Actions (already enabled)

---

## Step-by-Step: Create Environments

### 1. Navigate to Repository Settings

1. Go to your repository on GitHub
2. Click **Settings** (top tab)
3. In the left sidebar, scroll down and click **Environments**

You should see a page titled "Environments" with an option to **"New environment"** (green button, top right).

---

### 2. Create `dev` (BackupApi)

1. Click **New environment**
2. **Name**: `dev`
3. Click **Configure environment**

**Configuration**:
- **Deployment branches and tags**: Leave as default (allow all)
- **Required reviewers**: ❌ **Do NOT check** (dev should auto-deploy without approval)
- **Prevent forking repositories**: Leave unchecked (not needed for this scenario)
- Click **Save protection rules** (if prompted)

Return to Environments page. You should see `dev` listed.

---

### 3. Create `staging` (BackupApi)

1. Click **New environment**
2. **Name**: `staging`
3. Click **Configure environment**

**Configuration**:
- **Deployment branches and tags**: Select **Selected branches and tags**
  - Enter: `refs/tags/v*` (any release tag)
  - This prevents accidental deployments from feature branches
- **Required reviewers**: ✅ **Check this box**
  - Add yourself or your team (select from list of repo admins/maintainers)
  - Can select multiple people
  - **Dismiss stale reviews**: Uncheck (keep approvals valid)
- **Prevent forking repositories**: Leave unchecked
- Click **Save protection rules**

Return to Environments page. You should see `staging` listed.

---

### 4. Create `productions` (BackupApi)

1. Click **New environment**
2. **Name**: `productions`
3. Click **Configure environment**

**Configuration**:
- **Deployment branches and tags**: Select **Selected branches and tags**
  - Enter: `refs/tags/v*` (only release tags)
- **Required reviewers**: ✅ **Check this box**
  - Add yourself or your team
  - Consider requiring **multiple reviewers** for production (toggle: **Require reviewers**)
- **Prevent forking repositories**: Leave unchecked
- Click **Save protection rules**

Return to Environments page.

---

### 5. Create `server-dev`

1. Click **New environment**
2. **Name**: `server-dev`
3. Click **Configure environment**

**Configuration**:
- **Deployment branches and tags**: Leave as default (allow all)
- **Required reviewers**: ❌ **Do NOT check** (dev should auto-deploy without approval)
- **Prevent forking repositories**: Leave unchecked
- Click **Save protection rules**

**Then add environment-specific secrets** (see [Add Environment Secrets](#add-environment-secrets)):
- `CHARTHUB_SERVER_CONFIG_PATH`
- `CHARTHUB_SERVER_CHARTHUB_ROOT`
- `CHARTHUB_SERVER_CLONEHERO_ROOT`
- `CHARTHUB_SERVER_CHARTHUB_PORT`

---

### 6. Create `server-staging`

1. Click **New environment**
2. **Name**: `server-staging`
3. Click **Configure environment**

**Configuration**:
- **Deployment branches and tags**: Select **Selected branches and tags**
  - Enter: `refs/tags/v*`
- **Required reviewers**: ✅ **Check this box**
  - Add yourself or your team
- **Prevent forking repositories**: Leave unchecked
- Click **Save protection rules**

**Then add environment-specific secrets** (see [Add Environment Secrets](#add-environment-secrets)):
- `CHARTHUB_SERVER_CONFIG_PATH`
- `CHARTHUB_SERVER_CHARTHUB_ROOT`
- `CHARTHUB_SERVER_CLONEHERO_ROOT`
- `CHARTHUB_SERVER_CHARTHUB_PORT`

---

### 7. Create `server-production`

1. Click **New environment**
2. **Name**: `server-production`
3. Click **Configure environment**

**Configuration**:
- **Deployment branches and tags**: Select **Selected branches and tags**
  - Enter: `refs/tags/v*`
- **Required reviewers**: ✅ **Check this box**
  - Add yourself or your team
  - Consider requiring **multiple reviewers** for production
- **Prevent forking repositories**: Leave unchecked
- Click **Save protection rules**

**Then add environment-specific secrets** (see [Add Environment Secrets](#add-environment-secrets)):
- `CHARTHUB_SERVER_CONFIG_PATH`
- `CHARTHUB_SERVER_CHARTHUB_ROOT`
- `CHARTHUB_SERVER_CLONEHERO_ROOT`
- `CHARTHUB_SERVER_CHARTHUB_PORT`

---

## Add Environment Secrets

Environment secrets are specific to a particular environment and override repository-level secrets when that environment is used.

### For `server-dev`:

1. Go to **Settings** → **Environments** → **server-dev**
2. Scroll to **Environment secrets**
3. Click **Add environment secret** (or **New secret** button)

Add these **4 secrets** (use dev-specific paths/port):

| Secret Name | Example Value |
|-------------|---------------|
| `CHARTHUB_SERVER_CONFIG_PATH` | `/etc/charthub-server/config-dev` |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | `/data/dev/charthub` |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | `/data/dev/clonehero` |
| `CHARTHUB_SERVER_CHARTHUB_PORT` | `5180` |

For each, click **Add secret**, enter the name and value, then click **Add secret**.

### For `server-staging`:

1. Go to **Settings** → **Environments** → **server-staging**
2. Scroll to **Environment secrets**
3. Click **Add environment secret** (or **New secret** button)

Add these **4 secrets** (use staging-specific paths/ports):

| Secret Name | Example Value |
|-------------|---------------|
| `CHARTHUB_SERVER_CONFIG_PATH` | `/etc/charthub-server/config-staging` |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | `/data/staging/charthub` |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | `/data/staging/clonehero` |
| `CHARTHUB_SERVER_CHARTHUB_PORT` | `5180` |

For each, click **Add secret**, enter the name and value, then click **Add secret**.

### For `server-production`:

1. Go to **Settings** → **Environments** → **server-production**
2. Scroll to **Environment secrets**
3. Click **Add environment secret**

Add the **same 4 secrets** (use production-specific paths/ports):

| Secret Name | Example Value |
|-------------|---------------|
| `CHARTHUB_SERVER_CONFIG_PATH` | `/etc/charthub-server/config` |
| `CHARTHUB_SERVER_CHARTHUB_ROOT` | `/data/charthub` |
| `CHARTHUB_SERVER_CLONEHERO_ROOT` | `/data/clonehero` |
| `CHARTHUB_SERVER_CHARTHUB_PORT` | `5180` |

---

## Verification

After creating all environments and secrets:

1. Go to **Settings** → **Environments**
2. You should see **6 environments**:
  - ✅ `dev`
  - ✅ `staging`
  - ✅ `productions`
  - ✅ `server-dev`
   - ✅ `server-staging`
   - ✅ `server-production`

3. Click each environment to verify:
   - Protection rules are configured (tag matching, reviewer requirements)
  - Environment secrets are populated (for `server-dev`, `server-staging`, and `server-production`)

4. Go to **Settings** → **Secrets and variables** → **Actions**
5. Verify **all 7 repository-level ChartHub.Server secrets** are present:
   - `CHARTHUB_SERVER_JWT_SIGNING_KEY`
   - `CHARTHUB_SERVER_JWT_ISSUER`
   - `CHARTHUB_SERVER_JWT_AUDIENCE`
   - `CHARTHUB_SERVER_JWT_ALLOWED_EMAILS`
   - `CHARTHUB_SERVER_GOOGLE_ALLOWED_AUDIENCES`
   - `CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY`
   - (+ all existing BackupApi secrets)

---

## Protection Rules Explained

### Deployment Branches and Tags

- **Selected branches and tags** with `refs/tags/v*` means:
  - Only tags matching the pattern `v*` (e.g., `v1.0.0`, `v1.0.0-rc.1`, `v1.0.0-dev`) can trigger deployments
  - Feature branches, `main`, `develop` are excluded
  - Prevents accidental manual deployments to staging/production

### Required Reviewers

When a workflow tries to deploy to an environment with required reviewers:
1. The workflow pauses at the environment step
2. GitHub notifies reviewers
3. Reviewers can **Approve** or **Reject** the deployment via GitHub UI
4. Once approved, the workflow resumes
5. If rejected, the deployment is canceled

---

## Related Documentation

- [SECRETS.md](.github/SECRETS.md) — Complete secrets reference and how to create them
- [RUNNER_SETUP.md](.github/RUNNER_SETUP.md) — How to set up the ChartHub.Server runner
- [release.yml](.github/workflows/release.yml) — The main deployment workflow that uses these environments

