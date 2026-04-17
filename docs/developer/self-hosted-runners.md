# Self-Hosted Runners

This runner (`charthub-build`) handles all .NET builds, tests, and desktop publishing for ChartHub. It runs on the build server (i3-12100 / 62 GB RAM, Ubuntu).

---

## Prerequisites

### .NET SDK

```bash
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

```bash
sudo useradd --system --shell /usr/sbin/nologin --create-home github-runner
```

---

## Download and Configure

```bash
RUNNER_VERSION="2.322.0"   # Update to latest from https://github.com/actions/runner/releases

sudo -u github-runner bash <<'EOF'
cd /home/github-runner
mkdir actions-runner && cd actions-runner

curl -o actions-runner-linux-x64.tar.gz -L \
  "https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz"

tar xzf actions-runner-linux-x64.tar.gz
rm actions-runner-linux-x64.tar.gz
EOF
```

### Get a registration token

In GitHub: **Settings → Actions → Runners → New self-hosted runner**. Copy the token from the configure step (expires after 1 hour).

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

```bash
sudo bash -c 'cd /home/github-runner/actions-runner && ./svc.sh install github-runner'
sudo bash -c 'cd /home/github-runner/actions-runner && ./svc.sh start'
```

Verify:

```bash
sudo systemctl status actions.runner.calahil-ChartHub.charthub-build.service
sudo systemctl is-enabled actions.runner.calahil-ChartHub.charthub-build.service
# Should print: enabled
```

---

## NuGet Package Cache

The runner persists the NuGet global cache at `/home/github-runner/.nuget/packages`. `dotnet restore` uses this automatically — no additional configuration needed.

Optional pre-warm:

```bash
sudo -u github-runner bash -c 'cd /tmp && dotnet new console -n warmup --force && cd warmup && dotnet restore && cd .. && rm -rf warmup'
```

---

## Updating the Runner

```bash
sudo systemctl stop actions.runner.calahil-ChartHub.charthub-build.service

sudo -u github-runner bash <<'EOF'
cd /home/github-runner/actions-runner
NEW_VERSION="2.323.0"
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

---

## Security Notes

- The runner user has no login shell — cannot be used for interactive logins.
- Do not add the runner user to the `docker` group unless a job explicitly requires Docker.
- Regularly update the runner binary and re-register with a fresh token.
- Registration tokens are single-use and expire in 1 hour. Never commit them to source control.
