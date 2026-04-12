# GitHub Actions Runner Setup Guide

Step-by-step instructions for setting up a self-hosted GitHub Actions runner on the ChartHub.Server deployment host.

---

## Overview

The ChartHub.Server deployment pipeline requires a self-hosted GitHub Actions runner on the target host machine. The runner:
- Receives deployment jobs from GitHub Actions workflows
- Runs Docker commands locally (pull images, start containers, health checks)
- Must have labels: `self-hosted`, `linux`, `charthub-server-host`
- Typically runs as a systemd service

This guide assumes:
- **OS**: Linux (tested on Ubuntu 22.04+ and Debian 12+)
- **Host**: The machine where ChartHub.Server will be deployed
- **Docker**: Already installed and running

---

## Prerequisites

On the ChartHub.Server host machine:

- [ ] Root or `sudo` access
- [ ] Docker installed and running (`docker ps` returns active containers)
- [ ] Git installed (`git --version`)
- [ ] Internet access to download runner software
- [ ] A GitHub personal access token (PAT) with `admin:repo_hook`, `admin:org_hook`, `repo` scopes

### Generate GitHub Personal Access Token

1. Go to **GitHub** → **Settings** → **Developer settings** → **Personal access tokens** → **Tokens (classic)**
2. Click **Generate new token (classic)**
3. **Token name**: `charthub-server-runner`
4. **Expiration**: 90 days or custom (renewal required at expiry)
5. **Scopes**: Select:
   - ✅ `repo` (full control of private repositories)
   - ✅ `admin:repo_hook` (write access to hooks in all repositories)
   - ✅ `admin:org_hook` (write access to organization hooks)
6. Click **Generate token**
7. **Copy immediately** (GitHub shows it only once). Store in a password manager or temporary file.

---

## Installation Steps

### 1. SSH into the ChartHub.Server Host

```bash
ssh user@charthub-server-host
```

Replace `user` with your actual username and `charthub-server-host` with the server's IP or hostname.

### 2. Create a Runner Directory

GitHub recommends a dedicated directory for the runner:

```bash
mkdir -p /opt/actions-runner
cd /opt/actions-runner
```

If `/opt` is not writable, use `/home/<user>/actions-runner` instead.

### 3. Download the Runner Software

Determine your system architecture:

```bash
uname -m
```

- **x86_64**: Download `linux-x64`
- **arm64**: Download `linux-arm64`

Download the appropriate runner version:

```bash
# For x86_64 (most common)
wget https://github.com/actions/runner/releases/download/v2.321.0/actions-runner-linux-x64-2.321.0.tar.gz

# For arm64
# wget https://github.com/actions/runner/releases/download/v2.321.0/actions-runner-linux-arm64-2.321.0.tar.gz

# Extract
tar xzf actions-runner-linux-x64-2.321.0.tar.gz
```

**Note**: Check for the latest runner version: https://github.com/actions/runner/releases

### 4. Configure the Runner

Run the configuration script:

```bash
./config.sh --url https://github.com/YOUR_OWNER/YOUR_REPO \
  --token YOUR_PAT_TOKEN \
  --labels self-hosted,linux,charthub-server-host \
  --name charthub-server-runner \
  --work _work
```

**Replace**:
- `YOUR_OWNER` — Your GitHub username or organization
- `YOUR_REPO` — The repository name (e.g., `rhythmverse-client` subdirectory is `rhythmverseclient`)
- `YOUR_PAT_TOKEN` — The personal access token you generated earlier

**Example**:
```bash
./config.sh --url https://github.com/calahilstudios/rhythmverseclient \
  --token ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx \
  --labels self-hosted,linux,charthub-server-host \
  --name charthub-server-runner \
  --work _work
```

**Output** should show:
```
√ Connected to GitHub
√ Runner registration complete
Current runner version: 2.321.0
...
```

If you see an error, verify:
- PAT is correct and hasn't expired
- GitHub URL is correct (https://github.com/owner/repo)
- Network access to GitHub is available

### 5. Install and Start as a Systemd Service

Install the runner as a systemd service (so it auto-starts on reboot):

```bash
sudo ./svc.sh install
```

**Output**:
```
Creating service file
Service installation complete
```

Start the service:

```bash
sudo ./svc.sh start
```

Check status:

```bash
sudo ./svc.sh status
```

**Output** should show:
```
Active: active (running)
```

Enable auto-start on reboot:

```bash
sudo systemctl enable actions-runner.service
```

### 6. Verify Runner Registration in GitHub

1. Go to your GitHub repository
2. **Settings** → **Actions** → **Runners** (in the left sidebar)
3. You should see **`charthub-server-runner`** in the list with:
   - Status: ✅ **Idle** (green dot)
   - Labels: `self-hosted`, `linux`, `charthub-server-host`
   - Last activity: Just now

If not visible:
- Wait 10-15 seconds and refresh the page
- Check runner logs: `sudo journalctl -u actions-runner -f` (last 20 lines)
- Restart if needed: `sudo ./svc.sh restart`

---

## Verification

### Test 1: Check Runner Status

On the host machine:

```bash
sudo systemctl status actions-runner
```

Should show `Active: active (running)`.

### Test 2: Trigger a Workflow

1. Create a test tag on your repo:
   ```bash
   git tag v0.0.0-test
   git push origin v0.0.0-test
   ```

2. Go to **GitHub** → **Actions**
3. Trigger the `release.yml` workflow (it should run for the `v0.0.0-test` tag)
4. Monitor the workflow run
5. Look for a job with `runs-on: [self-hosted, linux, charthub-server-host]`
6. It should be assigned to your runner

### Test 3: Verify Docker Access

While a workflow runs (or SSH into host):

```bash
docker ps
```

Should list running containers without errors.

---

## Troubleshooting

### Runner Not Appearing in GitHub Settings

**Symptom**: The runner is not listed under **Settings** → **Actions** → **Runners**.

**Troubleshooting**:
1. Check registration logs:
   ```bash
   sudo journalctl -u actions-runner -n 50
   ```
2. Verify PAT is still valid (check GitHub Settings → Personal access tokens)
3. Ensure network connectivity: `ping github.com`
4. Re-run config script: `./config.sh` with correct parameters

### Runner Shows "Offline"

**Symptom**: Runner appears in GitHub but status is offline (gray dot).

**Troubleshooting**:
1. Check service status: `sudo systemctl status actions-runner`
2. If not running, start it: `sudo ./svc.sh start`
3. Check logs for errors: `sudo journalctl -u actions-runner -f`

### Workflow Jobs Hang or Get Queued

**Symptom**: Workflow jobs are queued but never run on the runner.

**Troubleshooting**:
1. Verify runner labels match workflow: `runs-on: [self-hosted, linux, charthub-server-host]`
2. Check runner doesn't have a different label configuration
3. Verify runner is in the correct repository (not organization-level only)
4. Look at workflow logs: **Actions** → **Workflow run** → job output

### Docker Commands Fail in Workflow

**Symptom**: Health checks or docker pull commands fail with permission errors.

**Troubleshooting**:
1. Ensure Docker daemon is running: `sudo systemctl status docker`
2. Add runner user to docker group:
   ```bash
   sudo usermod -aG docker runner
   ```
   (Then restart the runner: `sudo ./svc.sh restart`)
3. Check Docker can pull from GHCR: `docker login ghcr.io` (uses GITHUB_TOKEN in workflow)

### GHCR Image Pull Fails

**Symptom**: `docker pull ghcr.io/...` fails with "unauthorized".

**Troubleshooting**:
1. Verify `secrets.GITHUB_TOKEN` is available in the workflow (should be automatic)
2. Check Docker login step in workflow: it should run before pulling images
3. Verify GitHub Actions has permission to pull from GHCR (usually inherited)

---

## Maintenance

### Updating the Runner

GitHub periodically releases new runner versions. To update:

1. Stop the runner:
   ```bash
   sudo ./svc.sh stop
   ```

2. Download and extract the new version (see [Step 3](#3-download-the-runner-software))

3. Run config again (same parameters as original):
   ```bash
   ./config.sh --url ... --token ... --labels ... --name ...
   ```

4. Start the runner:
   ```bash
   sudo ./svc.sh start
   ```

### Monitoring

Check recent activity:

```bash
sudo journalctl -u actions-runner -n 100
```

Monitor a live deployment:

```bash
sudo journalctl -u actions-runner -f
```

---

## Uninstallation (If Needed)

To remove the runner:

```bash
sudo ./svc.sh stop
sudo ./svc.sh uninstall
```

Then delete the runner from GitHub:
1. **Settings** → **Actions** → **Runners**
2. Click the runner
3. **Remove**

---

## Next Steps

1. ✅ Runner is registered and running
2. ✅ Workflow jobs can run on `[self-hosted, linux, charthub-server-host]`
3. 🔄 Deploy ChartHub.Server using the release workflow:
   - Create a tag (`v1.0.0-rc.1` or similar)
   - Push to GitHub
   - Workflow builds Server image and awaits staging approval
   - Approve the deployment in GitHub UI
   - Server container starts and health checks pass

---

## Related Documentation

- [SECRETS.md](.github/SECRETS.md) — GitHub secrets configuration
- [ENVIRONMENTS.md](.github/ENVIRONMENTS.md) — GitHub environment setup
- [release.yml](.github/workflows/release.yml) — Main CI/CD workflow

---

## Support

For issues not covered here:
- Check runner logs: `sudo journalctl -u actions-runner -f`
- GitHub Actions Runner docs: https://docs.github.com/en/actions/hosting-your-own-runners
- Open an issue in the repository

