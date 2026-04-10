# ChartHub

ChartHub is a cross-platform app for finding, downloading, and managing custom Clone Hero charts.

It gives you one place to:

- Browse charts from RhythmVerse and Encore
- Download charts to your local device
- Install charts into your Clone Hero library on desktop
- Manage your local library
- Pair Android with desktop for local sync

## Who It Is For

ChartHub is built for players who want a simpler way to build and maintain a Clone Hero chart library without juggling multiple sites, file managers, and ad-hoc install steps.

## What You Can Do

### Browse multiple chart sources

Use the built-in RhythmVerse and Encore tabs to search, sort, and filter songs.

### Queue downloads in one place

Downloads from different sources flow into the same Downloads view so you can track them consistently.

### Install songs into Clone Hero on desktop

On desktop, ChartHub can take downloaded files, prepare them, and place them into your Clone Hero songs folder.

### Review and clean up your library

Use the Clone Hero library view to inspect songs, refresh metadata, reconcile the library, or remove entries you no longer want.

### Pair Android with your desktop

Use the Sync tab to pair a companion device over your local network and push local files to the desktop app.

## Platform Support

### Desktop

Desktop is the full experience.

- Browse RhythmVerse and Encore
- Download files locally
- Install songs into Clone Hero
- View and manage your local library
- Host desktop sync for a companion device

### Android

Android is primarily the companion experience.

- Browse RhythmVerse and Encore
- Download files locally
- Pair with a desktop app over LAN
- Push local files to the desktop sync host

Desktop-only features such as Clone Hero library installation and management are not the main Android workflow.

## Installation

Install ChartHub from the latest packaged release for your platform.

- Desktop: install the latest desktop release package
- Android: install the latest Android release package

If you want to build from source, run the Backup API locally, or work with the sync API directly, see [docs/advanced.md](docs/advanced.md).

## Quick Start

### 1. Open Settings

On first launch, review the Settings tab and confirm your local paths.

On desktop, this usually means making sure ChartHub can write to:

- Your downloads or staging location
- Your Clone Hero songs folder

### 2. Choose a chart source

Use either:

- RhythmVerse for the main catalog flow
- Encore for community search with advanced filters

### 3. Search and filter

Browse the source tab you want and refine results with the built-in filters.

- RhythmVerse supports source-specific sorting and instrument filtering
- Encore supports broader advanced search fields such as instrument, difficulty, charter, year, and other metadata filters

### 4. Download a chart

When you download a chart, it appears in the Downloads view.

This is where you can:

- Watch transfer progress
- Review downloaded files
- Continue into installation on desktop

### 5. Install into Clone Hero on desktop

On desktop, use the Downloads tab to install supported downloads into your Clone Hero songs directory.

ChartHub handles the normal flow of:

- Reading the downloaded file
- Extracting or converting it when needed
- Writing the result into your Clone Hero library
- Refreshing the in-app library view

### 6. Manage your library

Use the Clone Hero tab on desktop to:

- Browse installed songs
- Re-scan library metadata
- Reconcile the library with what exists on disk
- Delete songs you no longer want

## App Flow

ChartHub is organized around a small number of main tabs:

### RhythmVerse

Browse the RhythmVerse catalog, apply filters, and queue downloads.

### Encore

Browse Encore with general or advanced filters and queue downloads.

### Downloads

See queued transfers, local downloaded files, and installation activity.

### Clone Hero

Desktop-only library management for songs already installed into your Clone Hero folder.

### Sync

Use desktop as a sync host or Android as a companion device.

### Settings

Manage paths, pairing settings, and account-related settings.

## Sync Between Desktop And Android

ChartHub supports a desktop-host and Android-companion flow over your local network.

### On desktop

Use the Sync tab to generate a pairing QR code for the companion device.

### On Android

Use the Sync tab to scan the desktop QR code and connect to the desktop host.

### Important sync requirements

- Both devices must be on the same local network
- The desktop app must stay open while pairing and syncing
- The desktop app must expose a LAN-reachable address

If the companion cannot reach the desktop host, pairing and queue refresh actions will fail until the connection is fixed.

## Cloud Account Linking

ChartHub can link to a cloud account for supported storage workflows.

If you do not need cloud-backed features, you can still use the local browse, download, install, and sync flows that do not depend on that account.

## Troubleshooting

### A download fails right after queueing

This usually means the source URL could not be resolved correctly by the selected provider or local redirect service.

Try:

- Re-running the download from the source tab
- Checking whether the source is temporarily unavailable
- Verifying any locally hosted backup or redirect service is running if you rely on one

### Android cannot pair with desktop

Check that:

- Both devices are on the same LAN
- The desktop app is open
- The desktop address is reachable from the Android device
- Your QR code or pair code has not expired

### ChartHub cannot write files

Make sure the configured download, staging, and Clone Hero directories exist and are writable by the app.

### A song downloads but does not install correctly

Some files may require conversion or may contain incomplete metadata. If installation fails, keep the original download and inspect the install log in the Downloads view before retrying.

## Advanced Docs

The root README is intentionally focused on end users.

For technical or self-hosting material, use:

- [docs/advanced.md](docs/advanced.md)
- [docs/backup-api-self-hosting.md](docs/backup-api-self-hosting.md)
- [docs/sync-api.md](docs/sync-api.md)
- [ChartHub.BackupApi/README.docker.md](ChartHub.BackupApi/README.docker.md)
- [ChartHub.Server/README.docker.md](ChartHub.Server/README.docker.md)
- [openapi.yaml](openapi.yaml)
- [docs/swagger-ui.html](docs/swagger-ui.html)
- [architecture.md](architecture.md)

## License And Credits

- [LICENSE.txt](LICENSE.txt)
- [ChartHub/Credits.txt](ChartHub/Credits.txt)
