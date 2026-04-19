# Architecture

This document is the authoritative reference for ChartHub's code structure, layer boundaries, service catalog, and subsystem design. Rules and contribution policy are in [`.governance/architecture.md`](.governance/architecture.md).

---

## Overview

ChartHub is a multi-target application (`net10.0` desktop + `net10.0-android`) built on Avalonia. It communicates with a user-deployed **ChartHub Server** instance and optionally a **ChartHub BackupApi** instance.

**Data flow (required, never violated):**

```
View → ViewModel → Service/Configuration → ViewModel → View
```

**Forbidden flows:**

- `View → Service` (direct)
- `View → Model mutation`
- `Service → View`

---

## MVVM Layer Responsibilities

### View (`ChartHub/Views`, `ChartHub/Controls`)

**Allowed:**
- XAML rendering, binding declarations, visual states
- Presentation-only event handlers

**Not allowed:**
- Business logic
- Direct IO (filesystem, HTTP, DB, parsing)
- Calling service implementations directly

### ViewModel (`ChartHub/ViewModels`)

**Allowed:**
- UI state and command orchestration
- Coordinating use cases through service interfaces
- Mapping service results into presentation models

**Not allowed:**
- Direct IO (filesystem, HTTP, DB, platform APIs)
- Parsing logic
- Dependencies on `ChartHub.Views` or `ChartHub.Controls` namespaces

### Model (`ChartHub/Models`)

**Allowed:**
- Pure data contracts and value containers

**Not allowed:**
- Side effects, IO

### Services and Infrastructure (`ChartHub/Services`, `ChartHub/Configuration`, `ChartHub/Android`)

**Allowed:**
- All external IO (filesystem, HTTP, DB, OS/platform integration, parsing)
- Provider integrations (RhythmVerse, Encore, Google Auth)
- Config persistence and migration

**Not allowed:**
- UI rendering concerns
- Dependencies on `ChartHub.Views` or `ChartHub.Controls` namespaces

---

## Repository Structure

| Path | Purpose |
|---|---|
| `ChartHub/Views/` | Window and page views |
| `ChartHub/Controls/` | Reusable UI controls |
| `ChartHub/ViewModels/` | Presentation state and command orchestration |
| `ChartHub/Models/` | Pure data contracts |
| `ChartHub/Services/` | Business services, integration clients, parsers, state machines |
| `ChartHub/Configuration/` | Config models, interfaces, stores, migration |
| `ChartHub/Utilities/` | Cross-cutting helpers (layer-boundary-safe) |
| `ChartHub/Android/` | Android-specific platform integration (`#if ANDROID`) |
| `ChartHub/Resources/` | Fonts, icons, images, splash assets |
| `ChartHub/Themes/` | Catppuccin Macchiato color theme (`Palette.axaml`, `Theme.axaml`) |
| `ChartHub/Localization/` | Culture configuration and `.resx` resource file |
| `ChartHub/Strings/` | Compiled string key constants and icon glyph references |
| `ChartHub.Tests/` | Unit and integration-style tests |
| `ChartHub.Server/` | Server API — library management, virtual input, runner coordination |
| `ChartHub.Hud/` | HUD UI (game console overlay) for ChartHub Server |
| `ChartHub.BackupApi/` | RhythmVerse mirror and proxy service |
| `ChartHub.Conversion/` | Chart conversion library (MIDI, audio, image, song.ini) |
| `ChartHub.TranscriptionRunner/` | AI drum transcription runner agent |
| `.governance/` | Agent governance, architecture rules, contribution policy |

---

## Services Catalog

All services live in `ChartHub/Services/`. Platform-specific variants are registered via DI (see [Dependency Injection](#dependency-injection)).

| Service | Interface | Purpose |
|---|---|---|
| `ChartHubServerApiClient` | `IChartHubServerApiClient` | HTTP client for all ChartHub Server API endpoints |
| `InputWebSocketService` | `IInputWebSocketService` | WebSocket relay for virtual controller/keyboard/touchpad input |
| `LibraryCatalogService` | — | SQLite-backed catalog of installed songs with source identity keys |
| `SongIngestionStateMachine` | — | Orchestrates the multi-stage download → extract → convert → install pipeline |
| `SongIngestionCatalogService` | — | Persists ingestion state, attempts, assets, and manifest files |
| `SongMetadataParserService` | — | Parses artist/title/charter metadata from chart and MIDI files |
| `SharedDownloadQueue` | — | Observable collection of active `DownloadFile` items for UI binding |
| `EncoreApiService` | — | Integration client for the Encore chart source |
| `LibraryIdentityService` | — (static) | Chart identity normalization and source key generation |
| `LibrarySourceNames` | — (static) | Source name constants (`RhythmVerse`, `Encore`) and validation helpers |
| `GoogleAuthTokenDataStore` | `IDataStore` | OAuth token persistence adapter |
| `DesktopGoogleAuthProvider` | `IGoogleAuthProvider` | Google OAuth — desktop implementation |
| `AndroidGoogleAuthProvider` | `IGoogleAuthProvider` | Google OAuth — Android implementation (`#if ANDROID`) |
| `StatusBarService` | `IStatusBarService` | Platform status bar / notification center integration |
| `AndroidOrientationService` | `IOrientationService` | Android device orientation sensor (`#if ANDROID`) |
| `NullOrientationService` | `IOrientationService` | Desktop no-op orientation stub |
| `AndroidVolumeHardwareButtonSource` | `IVolumeHardwareButtonSource` | Android hardware volume button events (`#if ANDROID`) |
| `NoOpVolumeHardwareButtonSource` | `IVolumeHardwareButtonSource` | Desktop no-op hardware button stub |

---

## ViewModels Catalog

All ViewModels live in `ChartHub/ViewModels/` and receive dependencies exclusively via constructor injection.

| ViewModel | Purpose | Key dependencies |
|---|---|---|
| `MainViewModel` | Root container; coordinates all child VMs and shared download queue | All child VMs, `SharedDownloadQueue` |
| `AppShellViewModel` | Root app shell state and navigation | — |
| `SplashViewModel` | Splash/initialization screen state | — |
| `DownloadViewModel` | Download queue, progress, cancel, retry | `AppGlobalSettings`, `IChartHubServerApiClient`, `SharedDownloadQueue`, `CloneHeroViewModel`, `IStatusBarService` |
| `CloneHeroViewModel` | Clone Hero library browsing and install management | `AppGlobalSettings`, `IChartHubServerApiClient` |
| `RhythmVerseViewModel` | RhythmVerse catalog browsing and ingestion | `IConfiguration`, `LibraryCatalogService`, `SharedDownloadQueue`, `ISettingsOrchestrator`, `IChartHubServerApiClient` |
| `EncoreViewModel` | Encore chart source search and results | `EncoreApiService` |
| `SettingsViewModel` | App settings/preferences UI | `ISettingsOrchestrator`, `IAppConfigStore` |
| `DesktopEntryViewModel` | Desktop shortcut launcher management | `AppGlobalSettings`, `IChartHubServerApiClient` |
| `VolumeViewModel` | Volume control and hardware button handling | `AppGlobalSettings`, `IChartHubServerApiClient`, `IVolumeHardwareButtonSource` |
| `VirtualControllerViewModel` | Virtual gamepad input relay | `AppGlobalSettings`, `IInputWebSocketService`, `IOrientationService` |
| `VirtualTouchPadViewModel` | Virtual touchpad gesture relay | `AppGlobalSettings`, `IInputWebSocketService`, `IOrientationService` |
| `VirtualKeyboardViewModel` | Virtual keyboard input relay | `AppGlobalSettings`, `IInputWebSocketService` |

---

## Views and Controls Catalog

### Views (`ChartHub/Views/`)

| View | Bound to |
|---|---|
| `AppShellView` | `AppShellViewModel` |
| `SplashView` | `SplashViewModel` |
| `MainView` | `MainViewModel` |
| `DownloadView` | `DownloadViewModel` |
| `CloneHeroView` | `CloneHeroViewModel` |
| `RhythmVerseView` | `RhythmVerseViewModel` |
| `EncoreView` | `EncoreViewModel` |
| `SettingsView` | `SettingsViewModel` |
| `DesktopEntryView` | `DesktopEntryViewModel` |
| `VolumeView` | `VolumeViewModel` |
| `VirtualControllerView` | `VirtualControllerViewModel` |
| `VirtualTouchPadView` | `VirtualTouchPadViewModel` |
| `VirtualKeyboardView` | `VirtualKeyboardViewModel` |
| `SharedDownloadCardsView` | Reusable download card component (no dedicated VM) |

### Controls (`ChartHub/Controls/`)

| Control | Purpose |
|---|---|
| `SongInfoControl` | Song metadata display card |
| `SongRatingControl` | Song difficulty/rating display |
| `StarRatingControl` | Interactive star rating widget |

---

## Song Ingestion Pipeline

Song ingestion is orchestrated by `SongIngestionStateMachine` and persisted by `SongIngestionCatalogService`.

**Stage sequence:**

```
Queued → Downloading → Downloaded → InstallQueued → Staging → Installing → Installed
                                                                          ↘ Failed
```

| Stage | Description |
|---|---|
| `Queued` | Job created, waiting for worker |
| `Downloading` | File being fetched from source URL |
| `Downloaded` | File on disk, awaiting install queue slot |
| `InstallQueued` | Queued for staging/conversion |
| `Staging` | Archive extracted, chart files prepared |
| `Installing` | Conversion complete, copying to Clone Hero library |
| `Installed` | Song is in the library, ready to play |
| `Failed` | Terminal failure — `error` field contains reason |

`SongIngestionCatalogService` records each attempt, associated assets, and the manifest file path. `LibraryCatalogService` holds the final installed song identity keyed by `(source, source_id)`.

Setting `drum_gen_requested: true` on job creation automatically enqueues an AI drum transcription job (via `ChartHub.TranscriptionRunner`) after the `Installed` stage.

---

## Configuration and Secrets

Configuration lives in `ChartHub/Configuration/`.

### Interfaces

| Interface | Purpose |
|---|---|
| `IAppConfigStore` | Read/write app configuration |
| `ISecretStore` | Secure secret storage (tokens, API keys) |
| `ISettingsOrchestrator` | Coordinates read/write with validation and migration |
| `IConfigValidator` | Validates config state on load |

### Stores

| Store | Platform | Backend |
|---|---|---|
| `JsonAppConfigStore` | Both | `appsettings.json` + user secrets |
| `EncryptedFileSecretStore` | Desktop | Encrypted file on disk |
| `AndroidKeystoreSecretStore` | Android (`#if ANDROID`) | Android Keystore |

**Config models:** `AppConfigRoot`, `RuntimeAppConfig`, `GoogleAuthConfig`, `RhythmVerseSource`, `EncoreUiStateConfig`

**Secret key constants:** `SecretKeys.cs` — names for OAuth tokens and API keys.

**Migration:** `SettingsMigrationService` runs on first boot after an upgrade to move legacy secrets to current format. Runs async on Android, synchronously-blocked on desktop.

---

## Dependency Injection

DI is configured in `ChartHub/Utilities/AppBootstrapper.cs`. All registrations are singletons unless noted.

### Configuration & Persistence

```
IConfiguration           ← appsettings.json + user secrets
IAppConfigStore          → JsonAppConfigStore
ISecretStore             → EncryptedFileSecretStore (desktop)
                         → AndroidKeystoreSecretStore (Android)
IConfigValidator         → DefaultConfigValidator
ISettingsOrchestrator    → SettingsOrchestrator
SettingsMigrationService
AppGlobalSettings        (singleton)
```

### Platform-Specific Services

```
#if ANDROID:
  IOrientationService          → AndroidOrientationService
  IVolumeHardwareButtonSource  → AndroidVolumeHardwareButtonSource
  IGoogleAuthProvider          → AndroidGoogleAuthProvider
#else:
  IOrientationService          → NullOrientationService
  IVolumeHardwareButtonSource  → NoOpVolumeHardwareButtonSource
  IGoogleAuthProvider          → DesktopGoogleAuthProvider
```

### Core Services

```
EncoreApiService             (singleton)
SharedDownloadQueue          (singleton)
LibraryCatalogService        (singleton, SQLite-backed)
IChartHubServerApiClient  → ChartHubServerApiClient (singleton)
IStatusBarService         → StatusBarService (singleton)
IInputWebSocketService    → InputWebSocketService (transient)
```

### ViewModels

```
DownloadViewModel
CloneHeroViewModel
DesktopEntryViewModel
VolumeViewModel
SettingsViewModel
RhythmVerseViewModel
EncoreViewModel
VirtualControllerViewModel
VirtualTouchPadViewModel
VirtualKeyboardViewModel
MainViewModel          ← composed from all child VMs above
```

---

## Platform-Specific Code

All Android-specific code and usings must be guarded with `#if ANDROID`.

**Android-only entry points:**

| File | Purpose |
|---|---|
| `ChartHub/Android/MainActivity.cs` | Android main activity for Avalonia host |
| `ChartHub/Android/AndroidApp.cs` | Android app lifecycle |
| `ChartHub/Android/GoogleSignInActivity.cs` | Google OAuth activity |
| `ChartHub/Android/AndroidVolumeHardwareButtonSource.cs` | Hardware volume key events |
| `ChartHub/Android/AndroidManifest.xml` | Permissions and activity declarations |

Desktop builds exclude the entire `Android/` folder at compile time.

---

## Build and Quality Gates

| Setting | Value |
|---|---|
| Target frameworks | `net10.0` (desktop), `net10.0-android` |
| .NET SDK | `10.0.100` (global.json, `latestFeature` roll-forward) |
| Nullable | `enable` — strict null checks enforced |
| `TreatWarningsAsErrors` | `true` — warnings block build |
| `AnalysisLevel` | `latest-recommended` |
| `EnforceCodeStyleInBuild` | `true` — code style violations are errors |
| Banned APIs | `BannedSymbols.txt` enforced via analyzer |
| `AllowUnsafeBlocks` | `true` (SkiaSharp graphics interop only) |

Run before merging any change:

```
dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore
dotnet build ChartHub.sln --configuration Release --no-restore
```

---

## Boundary Enforcement and Tests

Architecture boundaries are enforced by two tests in `ChartHub.Tests/ArchitectureBoundariesTests.cs`:

- `Services_And_Configuration_DoNotDependOn_ViewLayer` — scans all `.cs` files in `Services/` and `Configuration/`; fails if any file imports `ChartHub.Views` or `ChartHub.Controls`
- `ViewModels_DoNotDependOn_ViewsOrControls` — same scan over `ViewModels/`; fails if any file imports `ChartHub.Views` or `ChartHub.Controls`

Tests use trait `[Trait("Category", "Unit")]`. Additional integration-style tests are tagged `IntegrationLite`. All layers have test coverage — 28 test files covering ViewModels, services, config, parsing, state machines, and utilities.

---

## Async and Error Handling Rules

- Use async end-to-end for all IO paths.
- `.Result` and `.Wait()` are **forbidden**.
- `async void` is allowed only for UI event handlers.
- Handle failure paths explicitly with meaningful error context.
- Avoid catch-all `catch (Exception)` unless justified.

---

## Contributing

See [Contributing](contributing.md) for the full contribution workflow and definition of done. Rules enforced on agents are in [`.governance/AGENTS.md`](.governance/AGENTS.md).
