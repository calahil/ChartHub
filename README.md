# ChartHub

Install and manage rhythm game song charts for Clone Hero.

[![Build](https://github.com/calahilstudios/charthub/actions/workflows/dotnet-guardrails.yml/badge.svg)](https://github.com/calahilstudios/charthub/actions/workflows/dotnet-guardrails.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)

**Full documentation: [docs.calahilstudios.com](https://docs.calahilstudios.com)**

---

## Components

| Component | Description |
|---|---|
| **ChartHub** | Desktop (Windows/Linux) and Android app — browse, download, and manage song libraries |
| **ChartHub.Server** | Server API — download orchestration, Clone Hero library management, virtual input (keyboard/gamepad/mouse) |
| **ChartHub.Hud** | Emubox WM splash/status UI for ChartHub Server |
| **ChartHub.BackupApi** | Optional self-hosted mirror for RhythmVerse song metadata |

---

## Quick Start

- [Install on Windows →](https://docs.calahilstudios.com/user-guide/install-windows/)
- [Install on Linux →](https://docs.calahilstudios.com/user-guide/install-linux/)
- [Install on Android →](https://docs.calahilstudios.com/user-guide/install-android/)
- [Self-host ChartHub Server →](https://docs.calahilstudios.com/self-hosting/server-setup/)

---

## Development

Requirements: .NET SDK 10.0

```bash
dotnet build ChartHub/ChartHub.csproj
dotnet run --project ChartHub/ChartHub.csproj --framework net10.0
```

Validation gates:

```bash
dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore
dotnet build ChartHub.sln --configuration Release --no-restore
dotnet test ChartHub.Tests/ChartHub.Tests.csproj --configuration Release --no-build
```

Contribution rules: [`.governance/AGENTS.md`](.governance/AGENTS.md) · Architecture: [`.governance/architecture.md`](.governance/architecture.md)

---

## License And Credits

- [LICENSE.txt](LICENSE.txt)
- [ChartHub/Credits.txt](ChartHub/Credits.txt)
