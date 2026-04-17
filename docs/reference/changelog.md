## [Unreleased]

### Features

- Add agent task template for implementation work assignments

## [1.0.8-rc.1] - 2026-04-17

### Bug Fixes

- Update job dependencies in release workflow for staging deployment

### Documentation

- Update kiosk setup script comments with detailed hardware reference and review instructions

### Features

- Implement virtual controller and touchpad functionality with mouse speed adjustment
- Enhance song conversion logic to handle missing file objects and adjust pagination behavior
- Add unit tests for virtual controller, keyboard, and touchpad view models
- Update backup API configuration with Traefik labels and external network
- Update download links and mirror source handling in ApiClientService
- Update API URLs in tests and docker-compose for consistency
- Update ApiClientServiceTests to use dynamic MirrorBaseUrl for download links
- Add ports configuration for backup API service in docker-compose
- Update Traefik configuration for backup API service to include TLS and middlewares
- Update backup API service URLs in configuration for consistency
- Update backup API service URLs and Traefik configuration for consistency
- Update Traefik configuration for backup API service to use new host and enable TLS
- Implement status bar service and integrate into view models for improved user feedback
- Integrate Serilog for enhanced logging with PostgreSQL sink configuration
- Implement HUD status service and view model
- Enhance kiosk setup with environment file for dynamic ChartHub.Server path and systemd service management
- Enhance kiosk setup by protecting GPU and audio packages from auto-removal and ensuring AMD GPU stack installation
- Update kiosk setup script to protect and install correct AMD GPU packages, replacing firmware-amd-graphics with linux-firmware

### Refactoring

- Enhance pagination logic and add virtual controller documentation

## [1.0.7] - 2026-04-13

### Refactoring

- Add DISPLAY and WAYLAND_DISPLAY environment variables for ChartHub server

## [1.0.6] - 2026-04-13

### Bug Fixes

- Update PostgreSQL connection string to use container name variable
- Parameterize PostgreSQL connection string port in backup API configuration...derp
- Update PostgreSqlConnectionString to use default port instead of variable
- Update environment names in documentation and workflows for consistency
- Correct environment name from 'productions' to 'production' in release workflow
- Improve health check script to use effective API port and enhance error reporting
- Update ChartHub server service to use 'gamer' user and group for improved security

### Documentation

- Update localization policy and guidelines for user-facing strings

### Features

- Add publish-tag.sh for tagging and releasing commits
- Enhance publish-tag.sh to support tag deletion and improve user prompts
- Implement platform-specific code rules and enhance Android project structure
- Enhance Clone Hero library management and server interactions
- Update Android emulator configurations and enhance download commands
- Add server-dev environment and update deployment configurations

### Refactoring

- Consolidate quality checks into dotnet-quality.yml
- Remove unused ChartHub.Server configuration from docker-compose and setup script
- Update application resource disposal to use async methods with timeout handling
- Enhance volume management by integrating pactl support and fallback to amixer
- Improve volume retrieval logic by implementing best effort approach for pactl and amixer
- Enhance logging functionality and add unit tests for ServerFileLoggerProvider

### Test

- Add unit tests for DesktopEntryViewModel functionality
- Add tests for ExecuteCommand and KillCommand without server connection

## [1.0.0-rc.2] - 2026-03-28

### Bug Fixes

- Parameterize PostgreSQL port in docker-compose

## [1.0.0-rc.1] - 2026-03-28

### Bug Fixes

- Stabilize Avalonia image loading and Linux config/resource paths
- Ui tweaks
- Use Drive file ID as asset location to prevent duplicate ingestion entries on Android
- Do not create googledrive ingestions for files not started by the pipeline
- Enforce trusted ingestion sources across sync and library flows
- Add targeted tests to bring line coverage above 70% threshold
- Restore companion queue visibility and pair-code freshness
- Correct link path for test.json in project file
- Standardize environment variable names in configuration files

### Features

- Complete AvaloniaUI migration with .NET 10 support
- Successfully migrate UI to AvaloniaUI framework on .NET 10
- Align RhythmVerseClient for .NET 10 with Avalonia UI on Linux
- Port RhythmVerseClient to Avalonia and .NET 10 with DI improvements
- Integrate AsyncImageLoader and add development mock data support
- Redesign song browser, add Catppuccin theming, and harden media handling
- Add Android companion mode and multi-target build
- Migrate to Caskaydia Nerd fonts and improve desktop/companion song views
- Add auth-gated Google Drive flow and SVG icon support
- Overhaul auth, transfer pipeline, and cross-platform download UX
- Add settings architecture, hardened logging, and broad test coverage
- Encore service adde and integrated into the UI
- Enable concurrent downloads and fold reload into save
- Overhaul song identity, ingestion metadata, and Clone Hero library reconciliation
- Add app icons and new launch configs for tablet/physical devices
- Add desktop sync workflow and Clone Hero metadata reconciliation
- Enforce repo quality guardrails and push focused coverage above 70%
- QR-first companion pairing with LAN-aware desktop host
- Hard-remove desktop sync URL settings and fix desktop listener to all interfaces
- Stabilize Android sync/download pipeline and local push
- Stabilize android companion flows
- Add RhythmVerse mirror API with sync and tests
- Implement cursor management for sync operations and add RhythmVerse source configuration
- Implement image proxy service with caching and endpoints for assets
- Implement download proxy service with endpoints for file retrieval and caching
- Add Docker support for Backup API with environment configuration and health checks
- Add HEAD method support for download endpoints and implement related tests
- Update README and add advanced documentation for Backup API and Sync API
- Add GitHub Actions workflows for deployment and release processes
- Enhance release workflow with deployment stages for dev, staging, and production
- Implement UpdateSyncApiPairingState method for immediate state updates
- Update RefreshDesktopQrCommand test to use a predefined syncApiPairCode
- Add Sync API Preferred Base URL functionality

### Fix

- UseMockData setting not respected on Android - remove unconditional _isAndroid() override

### Refactoring

- Rename RhythmVerseModel and improve image/url safety
- Replace filter pane toggle with multi-pane mode, move watcher init, and add song detail controls
- Split song models and consolidate image/resource handling utilities
- Phase 1

### Cleanup

- Plan added

### Core

- Work to create a unified model contract for both sources

### Remove

- Removed the reconciliation service until I can figure out why ithe hashs arent matching

### Rename

- Massive renaming because of App name needing to be renamed.

### Rework

- Song ingestion rework begins.

### Ui

- RhythmVerse UI design work
- Fixes

