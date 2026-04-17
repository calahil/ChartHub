# ChartHub Server Setup

ChartHub Server is an ASP.NET Core Minimal API that provides server-side library management, download orchestration, and virtual input (keyboard, gamepad, mouse) for the Android companion.

---

## Requirements

- Linux host (x64) with `uinput` kernel module available
- Docker and Docker Compose
- Outbound internet access for RhythmVerse API calls
- A Google account email for client authentication

---

## uinput Permissions

The virtual controller, touchpad, and keyboard features use the Linux `uinput` kernel module to inject input events. The process must have write access to `/dev/uinput`.

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

Reload rules:

```bash
sudo udevadm control --reload-rules
sudo udevadm trigger
```

Verify access:

```bash
ls -la /dev/uinput
# Expected: crw-rw---- 1 root input ...
```

If `IsSupported` returns `false` at runtime, the device file is missing or the process lacks permission. The affected endpoint will return `503 Service Unavailable`.

---

## Docker Setup

From the repository root:

1. Generate local env values:

```bash
./scripts/setup-local-secrets.sh
```

2. Set your RhythmVerse token in `.env.local`:

```env
RHYTHMVERSE_TOKEN=yourtoken
```

3. Optionally apply dotnet user-secrets:

```bash
./scripts/setup-local-secrets.sh --apply-user-secrets
```

4. Set the JWT signing key if you want to override the generated value:

```env
CHARTHUB_SERVER_JWT_SIGNING_KEY=your-key
```

5. Set the Google account used for client auth:

```env
CHARTHUB_SERVER_ALLOWED_EMAIL_0=you@gmail.com
```

6. Initialize bind mount folders:

```bash
./scripts/init-charthub-server-dev-paths.sh
```

7. Start the stack:

```bash
docker compose up -d --build
```

ChartHub Server is exposed at `http://127.0.0.1:5180` by default.

---

## Health Check

```bash
curl -s http://127.0.0.1:5180/health
```

Expected:

```json
{ "status": "ok" }
```
