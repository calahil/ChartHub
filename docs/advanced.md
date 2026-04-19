# Advanced Developer Reference

This is the authoritative developer reference for building, running, configuring, and contributing to all ChartHub subsystems.

---

## Repository Layout

| Project | Purpose |
|---|---|
| `ChartHub/` | Main client app — desktop (`net10.0`) and Android (`net10.0-android`) |
| `ChartHub.Server/` | Server API — download orchestration, Clone Hero library, virtual input, runner coordination |
| `ChartHub.BackupApi/` | RhythmVerse mirror and proxy service |
| `ChartHub.Conversion/` | Chart conversion library (CON/RB3CON → Clone Hero) |
| `ChartHub.TranscriptionRunner/` | AI drum transcription runner agent |
| `ChartHub.Hud/` | HUD overlay displayed while playing (reads from ChartHub.Server) |
| `ChartHub.Tests/` | Client app tests |
| `ChartHub.Server.Tests/` | Server API tests |
| `ChartHub.BackupApi.Tests/` | BackupApi tests |
| `ChartHub.Conversion.Tests/` | Conversion library tests |
| `ChartHub.sln` | Solution entry point |
| `scripts/` | Dev, deploy, and ops scripts |
| `.governance/` | Agent rules, architecture policy, contribution definition of done |

---

## Requirements

- **.NET SDK** `10.0.100` (enforced by `global.json`)
- **Android builds additionally require:**
  - Android SDK at `$HOME/Android/Sdk`
  - Java 21 (`/usr/lib/jvm/java-21-openjdk-amd64` by default in tasks)
  - AVD named `Medium_Phone_API_36` for the emulator tasks

---

## Build and Run

### Desktop client

```bash
dotnet build ChartHub/ChartHub.csproj -f net10.0
dotnet run --project ChartHub/ChartHub.csproj --framework net10.0
```

### Android — build

```bash
dotnet build ChartHub/ChartHub.csproj -f net10.0-android \
  -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk-amd64 \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

### Android — install to emulator (emulator-5554)

```bash
dotnet build ChartHub/ChartHub.csproj -t:Install \
  -p:EnableAndroid=true -f net10.0-android \
  -p:Device=emulator-5554 -p:RuntimeIdentifier=android-x64 \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

### Android — install to physical device

```bash
dotnet build ChartHub/ChartHub.csproj -t:Install \
  -p:EnableAndroid=true -f net10.0-android \
  -p:Device=<DEVICE_SERIAL> -p:RuntimeIdentifier=android-arm64 \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk
```

### ChartHub.Server

```bash
dotnet run --project ChartHub.Server/ChartHub.Server.csproj --no-launch-profile
```

> `--no-launch-profile` is required for deterministic port binding. `launchSettings.json` can override `ASPNETCORE_URLS` and break health and OpenAPI probes.

### ChartHub.BackupApi

```bash
dotnet run --project ChartHub.BackupApi/ChartHub.BackupApi.csproj --no-launch-profile
```

VS Code tasks cover all common desktop, Android, and server flows — see `.vscode/tasks.json`.

---

## Testing

Run each test project individually:

```bash
dotnet test ChartHub.Tests/ChartHub.Tests.csproj
dotnet test ChartHub.Server.Tests/ChartHub.Server.Tests.csproj
dotnet test ChartHub.BackupApi.Tests/ChartHub.BackupApi.Tests.csproj
dotnet test ChartHub.Conversion.Tests/ChartHub.Conversion.Tests.csproj
```

Full validation before merging (required by `.governance/AGENTS.md`):

```bash
dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore
dotnet build ChartHub.sln --configuration Release --no-restore
dotnet test ChartHub.Tests/ChartHub.Tests.csproj
dotnet test ChartHub.Server.Tests/ChartHub.Server.Tests.csproj
dotnet test ChartHub.BackupApi.Tests/ChartHub.BackupApi.Tests.csproj
dotnet test ChartHub.Conversion.Tests/ChartHub.Conversion.Tests.csproj
```

Test categories in use: `Unit`, `IntegrationLite`.

---

## ChartHub Client — Configuration

**appsettings.json** ([ChartHub/appsettings.json](../ChartHub/appsettings.json)):

| Key | Default | Purpose |
|---|---|---|
| `UseMockData` | `true` | Use mock API responses instead of live server (flip to `false` for real server) |
| `GoogleDrive.android_client_id` | `""` | Google OAuth client ID for Android |

**User secrets** (dev only):

```bash
dotnet user-secrets set "<key>" "<value>" --project ChartHub/ChartHub.csproj
# UserSecretsId: b0e751a2-ccc2-493f-96d7-3a78ffe23a8b
```

**Library catalog:** Installed song identities are tracked in `library-catalog.db` under the application config directory (platform-specific app data path at runtime). Source IDs are keyed by provider — `rhythmverse` and `encore`.

---

## ChartHub.Server — Self-Hosting

### Key configuration

**Auth (`Auth:*`):**

| Key | Default | Notes |
|---|---|---|
| `JwtSigningKey` | — | **Required.** Minimum 32 characters |
| `Issuer` | `charthub-server` | |
| `Audience` | `charthub-clients` | |
| `AccessTokenMinutes` | `60` | |
| `AllowedEmails` | `[]` | **Required.** Allowlist of Google accounts |

**GoogleAuth (`GoogleAuth:*`):**

| Key | Notes |
|---|---|
| `AllowedAudiences` | Google OAuth client IDs that the server will accept tokens from |

**Server paths (`ServerPaths:*`):**

| Key | Default (container) | Notes |
|---|---|---|
| `ConfigRoot` | `/config` | Config and SQLite DB directory |
| `ChartHubRoot` | `/charthub` | Working directory for downloads and staging |
| `DownloadsDir` | `/charthub/downloads` | |
| `StagingDir` | `/charthub/staging` | |
| `CloneHeroRoot` | `/clonehero` | Clone Hero songs library root |
| `SqliteDbPath` | `/config/charthub-server.db` | SQLite database file |
| `RunnerAudioSigningKey` | `change-me-runner-audio-key` | **Required for transcription.** HMAC key for signing runner audio download URLs |

**Desktop entries (`DesktopEntry:*`):**

| Key | Default | Notes |
|---|---|---|
| `Enabled` | `true` | |
| `CatalogDirectory` | `/usr/share/applications` | Where to scan for `.desktop` files |
| `IconCacheDirectory` | `cache/desktop-entry-icons` | |
| `SseIntervalSeconds` | `2` | |

**HUD (`Hud:*`):**

| Key | Default | Notes |
|---|---|---|
| `ExecutablePath` | `""` | Path to `ChartHub.Hud` binary |
| `ServerPort` | `5000` | Port the server listens on |

**Downloads (`Downloads:*`):**

| Key | Default | Notes |
|---|---|---|
| `CompletedJobRetentionDays` | `7` | Days to keep completed jobs before cleanup |

### Local dev setup

Create the dev directory structure:

```bash
./scripts/init-charthub-server-dev-paths.sh
```

This creates `dev-data/config/`, `dev-data/charthub/`, and `dev-data/clonehero/` under the repository root, matching the `appsettings.Development.json` paths.

### Environment variables (container / production)

From [ChartHub.Server/.env.example](../ChartHub.Server/.env.example):

```bash
CHARTHUB_SERVER_ENVIRONMENT=Production
CHARTHUB_SERVER_PORT=5180
CHARTHUB_SERVER_JWT_SIGNING_KEY=<32+ char secret>
CHARTHUB_SERVER_JWT_ISSUER=charthub-server
CHARTHUB_SERVER_JWT_AUDIENCE=charthub-clients
CHARTHUB_SERVER_GOOGLE_DRIVE_API_KEY=
CHARTHUB_SERVER_CONFIG_PATH=./dev-data/config
CHARTHUB_SERVER_CHARTHUB_PATH=./dev-data/charthub
CHARTHUB_SERVER_CLONEHERO_PATH=./dev-data/clonehero
```

User secrets for local dev:

```bash
# UserSecretsId: a4e27a88-521b-4eea-941a-fe09b3aff5cb
dotnet user-secrets set "Auth:JwtSigningKey" "<key>" --project ChartHub.Server/ChartHub.Server.csproj
dotnet user-secrets set "Auth:AllowedEmails:0" "you@example.com" --project ChartHub.Server/ChartHub.Server.csproj
```

---

## ChartHub.BackupApi — Self-Hosting

The BackupApi mirrors RhythmVerse song metadata into a local database (PostgreSQL or SQLite) and serves it to ChartHub clients.

### Key configuration

**Auth (`ApiKey:*`):**

| Key | Default | Notes |
|---|---|---|
| `Key` | `""` | **Required.** Shared API key for all client requests |

**Database (`Database:*`):**

| Key | Default | Notes |
|---|---|---|
| `Provider` | `postgresql` | `postgresql` or `sqlite` |
| `PostgreSqlConnectionString` | — | Used when `Provider` is `postgresql` |
| `SqliteConnectionString` | `Data Source=charthub-backup.db` | Used when `Provider` is `sqlite` |

**Sync (`Sync:*`):**

| Key | Default | Notes |
|---|---|---|
| `Enabled` | `true` | |
| `IntervalMinutes` | `10080` | 7 days between full sweeps |
| `RecordsPerPage` | `100` | |
| `MaxPagesPerRun` | `5000` | Cap on pages per run; incomplete runs do not soft-delete |
| `InitialDelayMinutes` | `0` | Delay before first sync after startup |

**Downloads (`Downloads:*`):**

| Key | Default | Notes |
|---|---|---|
| `Mode` | `redirect` | Currently only `redirect` is implemented |
| `CacheDirectory` | `./cache/downloads` | |
| `ExternalRedirectCacheHours` | `48` | How long to cache resolved redirect URLs |

**ImageCache (`ImageCache:*`):**

| Key | Default | Notes |
|---|---|---|
| `CacheDirectory` | `./cache/images` | |

User secrets for local dev:

```bash
# UserSecretsId: c272e404-7bd9-4951-9cdd-22ba952f0d31
dotnet user-secrets set "ApiKey:Key" "<key>" --project ChartHub.BackupApi/ChartHub.BackupApi.csproj
dotnet user-secrets set "RhythmVerseSource:Token" "<token>" --project ChartHub.BackupApi/ChartHub.BackupApi.csproj
```

### Docker Compose setup

```bash
# 1. Generate local env values (writes .env.local, never overwrites .env)
./scripts/setup-local-secrets.sh

# 2. Set RHYTHMVERSE_TOKEN in .env.local if not already populated

# 3. Start the stack
docker compose up -d --build
```

The BackupApi is exposed at `http://127.0.0.1:5147`. Cache directories are mounted as bind volumes; host paths are configured via `BACKUP_DOWNLOADS_HOST_PATH` and `BACKUP_IMAGES_HOST_PATH`.

### Sync model

- Each run is a full reconciliation sweep starting from page 1
- A run completes when it receives either an empty page or a partial page (`returned < records`)
- Songs seen in a completed run are retained; songs not seen are soft-deleted
- Soft-deleted songs are automatically restored if they reappear in a later run
- An incomplete run (hit `MaxPagesPerRun` before reaching a terminal page) does not soft-delete anything
- The upstream RhythmVerse endpoint ignores `updatedSince` — correctness depends on full sweeps, not incremental watermarks

### Client compatibility routes

The BackupApi exposes the RhythmVerse-compatible query shape consumed by ChartHub clients:

- `POST /api/all/songfiles/list`
- `POST /api/all/songfiles/search/live`

Supported form fields: `page`, `records`, `text`, `author`, `instrument` (repeatable, OR semantics), `sort[0][sort_by]`, `sort[0][sort_order]`, `data_type` (legacy, accepted but ignored).

### Health endpoint

`GET /api/rhythmverse/health/sync` (no auth required) — useful fields:

| Field | Notes |
|---|---|
| `last_success_utc` | UTC timestamp of last completed sweep |
| `lag_seconds` | Seconds since last success |
| `total_available` | Total song count in database |
| `reconciliation_in_progress` | `true` if a sweep is currently running |
| `last_run_completed` | `false` if the last run hit `MaxPagesPerRun` before finishing |

### Operational notes

- Apply EF migrations before deploying reconciliation changes
- Soft-deleted songs are excluded from all public queries by default
- Incomplete runs leave soft-delete state unchanged — safe to limit `MaxPagesPerRun` for large catalogs

---

## ChartHub.Conversion — Chart Conversion Library

`ChartHub.Conversion` converts Rock Band CON packages into Clone Hero song folders.

**Supported input formats:** `.con`, `.rb3con`

**Entry point:**

```csharp
IConversionService.ConvertAsync(sourcePath, outputRoot, cancellationToken)
```

**Internal components:**

| Component | File | Purpose |
|---|---|---|
| STFS reader | `Stfs/StfsReader.cs` | Reads the STFS block chain format used by CON packages |
| DTA parser | `Dta/DtaParser.cs` | Parses `songs.dta` metadata (artist, title, charter, instruments) |
| MIDI converter | `Midi/RbMidiConverter.cs` | Converts Rock Band MIDI track layout → Clone Hero format |
| Drum merger | `Midi/DrumMidiMerger.cs` | Merges Rock Band drum lanes into Clone Hero drum tracks |
| Audio extractor | `Audio/MoggExtractor.cs` | Extracts and splits MOGG audio streams |
| Image decoder | `Image/PngXboxDecoder.cs` | Converts Xbox DXT1/DXT5 textures → standard PNG |
| song.ini generator | `SongIni/SongIniGenerator.cs` | Writes Clone Hero `song.ini` metadata file |

See [ChartHub.Conversion/ConversionService.cs](../ChartHub.Conversion/ConversionService.cs) for the full pipeline.

---

## ChartHub.TranscriptionRunner — Runner Agent

`ChartHub.TranscriptionRunner` is a standalone worker that registers with a ChartHub.Server instance, claims drum transcription jobs, runs AI inference, and submits MIDI results.

### Registration (one-time)

First, issue a registration token from the server (requires JWT auth):

```
POST /api/v1/runners/registration-tokens
```

Then register the runner:

```bash
dotnet run --project ChartHub.TranscriptionRunner/ChartHub.TranscriptionRunner.csproj -- \
  register \
  --server https://<charthub-server-host> \
  --token <registration-token> \
  --name <runner-name> \
  --concurrency 1
```

This writes `~/.charthub-runner/config.json` containing the `runner_id` and hashed secret.

### Running

```bash
dotnet run --project ChartHub.TranscriptionRunner/ChartHub.TranscriptionRunner.csproj -- run
# or with explicit config path:
dotnet run --project ChartHub.TranscriptionRunner/ChartHub.TranscriptionRunner.csproj -- run \
  --config /path/to/config.json
```

### Worker protocol

```
register → periodic heartbeat (POST /api/v1/runner/heartbeat)
         → claim job (POST /api/v1/runner/jobs/claim)
         → signal processing (POST .../processing)
         → fetch audio (GET .../audio via HMAC-signed URL)
         → run inference
         → upload MIDI result (POST .../complete, multipart/form-data)
         → on error: yield (returns to queue) or fail (terminal)
```

Auth header for all runner requests: `Authorization: Runner {runnerId}:{secret}`

See [API Reference — Runner Protocol](developer/api-reference.md#runner-protocol) for full endpoint and message schemas.

---

## Scripts

| Script | Purpose |
|---|---|
| `scripts/init-charthub-server-dev-paths.sh` | Creates `dev-data/config/`, `dev-data/charthub/`, `dev-data/clonehero/` for local server dev |
| `scripts/setup-local-secrets.sh` | Populates `.env.local` from Infisical or existing `.env`; never overwrites `.env` |
| `scripts/publish-tag.sh` | Validates repo state and tags a release commit |
| `scripts/sync-github-secrets.sh` | Pushes secrets from Infisical (or `.env`) to GitHub repository environments via `gh` CLI |
| `scripts/kiosk-setup.sh` | Configures Lubuntu 24.04 as a ChartHub.Server kiosk (removes XFCE4, installs Openbox, sets up X session) |
| `scripts/start-kiosk-session.sh` | LightDM X session entry point for kiosk mode; starts Openbox and launches ChartHub.Server |

### Docker Setup

From repository root:

1. Generate local env values:

```bash
./scripts/setup-local-secrets.sh
```

2. Set `RHYTHMVERSE_TOKEN` in `.env.local`.
3. If needed, apply local dotnet user-secrets values too:

```bash
./scripts/setup-local-secrets.sh --apply-user-secrets
```

4. Set `CHARTHUB_SERVER_JWT_SIGNING_KEY` manually only when you want to override the generated value.
5. Set `CHARTHUB_SERVER_ALLOWED_EMAIL_0` to the Google account email you use in the client auth flow.
6. Initialize bind mount folders:

```bash
./scripts/init-charthub-server-dev-paths.sh
```

7. Start the stack:

```bash
docker compose up -d --build
```

ChartHub.Server is exposed on `http://127.0.0.1:5180` by default.

### Health Check

```bash
curl -s http://127.0.0.1:5180/health
```

Expected response:

```json
{
	"status": "ok"
}
```

## Architecture And Contribution Guidance

If you are changing code rather than using the app, start with:

- [.governance/architecture.md](../.governance/architecture.md)
- [.governance/AGENTS.md](../.governance/AGENTS.md)
- [.github/copilot-instructions.md](../.github/copilot-instructions.md)