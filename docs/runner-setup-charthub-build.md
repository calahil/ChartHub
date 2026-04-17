# GitHub Actions Runner Setup: `charthub-build`

This runner handles all .NET builds, tests, and desktop publishing for ChartHub.
It runs on the second server (i3-12100 / 62 GB RAM, Ubuntu).

---

## Prerequisites

Install the following on the server before registering the runner.

### .NET SDK

```bash
# Import Microsoft package signing key
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

Verify:
```bash
dotnet --version
# Should output 10.x.x
```

### Other tools

```bash
sudo apt-get install -y ripgrep zip curl git
```

---

## Create a Dedicated Runner User

Running as a dedicated non-root user with no login shell is the recommended security posture.

```bash
sudo useradd --system --shell /usr/sbin/nologin --create-home github-runner
```

---

## Download and Configure the Runner

Do this as the `github-runner` user (or with `sudo -u github-runner`).

```bash
RUNNER_VERSION="2.322.0"   # Update to latest from https://github.com/actions/runner/releases

sudo -u github-runner bash <<'EOF'
cd /home/github-runner
mkdir actions-runner && cd actions-runner

curl -o actions-runner-linux-x64.tar.gz -L \
  "https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz"

# Verify checksum (get current value from the release page)
# sha256sum -c <<< "<expected_sha256>  actions-runner-linux-x64.tar.gz"

tar xzf actions-runner-linux-x64.tar.gz
rm actions-runner-linux-x64.tar.gz
EOF
```

### Get a registration token

In GitHub: Settings → Actions → Runners → New self-hosted runner.
Copy the token from the configure step. It expires after 1 hour.

### Register

```bash
sudo -u github-runner bash <<'EOF'
cd /home/github-runner/actions-runner

./config.sh \
  --url https://github.com/calahil/ChartHub \
  --token <YOUR_REGISTRATION_TOKEN> \
  --name charthub-build \
  --labels self-hosted,linux,charthub-build \
  --work _work \
  --unattended
EOF
```

---

## Install as a systemd Service

`svc.sh install` must run as root from the runner directory. The `github-runner` argument tells it which user to run the service as.

```bash
sudo bash -c 'cd /home/github-runner/actions-runner && ./svc.sh install github-runner'
sudo bash -c 'cd /home/github-runner/actions-runner && ./svc.sh start'
```

Verify it is running:
```bash
sudo systemctl status actions.runner.calahil-ChartHub.charthub-build.service
```

Enable auto-start on reboot (the svc.sh install step handles this, but confirm):
```bash
sudo systemctl is-enabled actions.runner.calahil-ChartHub.charthub-build.service
# Should print: enabled
```

---

## NuGet Package Cache

The runner persists the NuGet global cache between runs, eliminating Actions cache quota usage for packages.
The default cache location is `/home/github-runner/.nuget/packages`.

No additional configuration is needed — `dotnet restore` uses this path automatically.

To pre-warm on first use (optional):
```bash
sudo -u github-runner bash -c 'cd /tmp && dotnet new console -n warmup --force && cd warmup && dotnet restore && cd .. && rm -rf warmup'
```

---

## Security Notes

- The runner user has no login shell — it cannot be used for interactive logins.
- Do not add the runner user to the `docker` group unless a job explicitly requires Docker (none currently do on this runner).
- Regularly update the runner binary: download the new release tarball and re-run `config.sh --url ... --token <new_token>` to re-register, then restart the service.
- Runner registration tokens are single-use and expire in 1 hour. Do not commit them to source control.

---

## Updating the Runner

```bash
# Stop service
sudo systemctl stop actions.runner.calahil-ChartHub.charthub-build.service

# Download new version and extract over existing directory
sudo -u github-runner bash <<'EOF'
cd /home/github-runner/actions-runner
NEW_VERSION="2.323.0"   # Set to target version
curl -o runner.tar.gz -L \
  "https://github.com/actions/runner/releases/download/v${NEW_VERSION}/actions-runner-linux-x64-${NEW_VERSION}.tar.gz"
tar xzf runner.tar.gz
rm runner.tar.gz
EOF

sudo systemctl start actions.runner.calahil-ChartHub.charthub-build.service
```

---

## Removal

```bash
sudo bash -c 'cd /home/github-runner/actions-runner && ./svc.sh stop'
sudo bash -c 'cd /home/github-runner/actions-runner && ./svc.sh uninstall'

sudo -u github-runner bash -c 'cd /home/github-runner/actions-runner && ./config.sh remove --token <REMOVE_TOKEN>'
```
