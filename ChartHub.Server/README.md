# ChartHub.Server

ASP.NET Core Minimal API that provides the server-side companion features for the ChartHub client.

## uinput Permissions (Linux)

The virtual controller, touchpad, and keyboard features use the Linux `uinput` kernel module to inject input events. The process running `ChartHub.Server` must have write access to `/dev/uinput`.

### Option 1 — Add the server user to the `input` group

```bash
sudo usermod -aG input <username>
```

Log out and back in (or reboot) for the group membership to take effect.

### Option 2 — udev rule (recommended for headless/service deployments)

Create `/etc/udev/rules.d/99-charthub-uinput.rules`:

```
SUBSYSTEM=="misc", KERNEL=="uinput", MODE="0660", GROUP="input"
```

Then reload rules:

```bash
sudo udevadm control --reload-rules
sudo udevadm trigger
```

Verify that `/dev/uinput` is accessible:

```bash
ls -la /dev/uinput
# Expected: crw-rw---- 1 root input ...
```

If `IsSupported` returns `false` at runtime, the device file is missing or the process lacks permission. The endpoint will return `503 Service Unavailable` in that case.
