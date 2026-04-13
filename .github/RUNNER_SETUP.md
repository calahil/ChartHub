# GitHub Actions Runner Setup Guide

This guide covers setting up the self-hosted runner used by ChartHub.Server production executable deployment.

## Runner purpose

The Server deploy job runs on:

- `self-hosted`
- `linux`
- `charthub-server-host`

The job deploys a Linux bundle, updates a systemd unit, restarts the service, and validates `/health`.

## Prerequisites on target host

- Linux host with sudo access
- GitHub Actions runner installed and registered with label `charthub-server-host`
- `unzip` installed
- `systemd` available

## Register runner (summary)

1. Download and configure GitHub runner on host.
2. Register labels: `self-hosted,linux,charthub-server-host`.
3. Install runner service and start it.
4. Verify it appears in GitHub Actions runners list.

## Host directory preparation

Deployment expects and will maintain these paths:

- `/srv/appdata/charthub/releases`
- `/srv/appdata/charthub/current`
- `/srv/appdata/charthub/config`
- `/srv/appdata/charthub/db`
- `/srv/appdata/charthub/logs`
- `/srv/appdata/charthub/data`
- `/srv/appdata/charthub/music`

The workflow creates missing directories automatically.

## systemd service

The workflow installs and manages:

- Unit file: `/etc/systemd/system/charthub-server.service`
- Environment file: `/srv/appdata/charthub/config/charthub-server.env`
- Executable path: `/srv/appdata/charthub/current/ChartHub.Server`

## Validation

After a stable release deploy:

1. `systemctl status charthub-server.service` is active.
2. `curl http://localhost:<port>/health` returns `200`.
3. `/srv/appdata/charthub/current` points to the expected version folder.

