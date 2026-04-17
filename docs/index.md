# ChartHub

ChartHub installs and manages rhythm game song charts from official sources and converts them reliably for Clone Hero.

---

## Components

| Component | Purpose |
|---|---|
| **ChartHub Desktop** | Windows/Linux app — browse, download, and manage song libraries |
| **ChartHub Android** | Android companion — remote control, song ingestion trigger, virtual input |
| **ChartHub Server** | Server-side library API, download orchestration, and virtual input (keyboard/gamepad/mouse) |
| **ChartHub Hud** | Emubox WM splash screen and status UI for ChartHub Server |
| **ChartHub Backup API** | Optional self-hosted mirror of RhythmVerse song metadata |

---

## AI-Assisted Development

ChartHub is developed with assistance from AI coding agents (GitHub Copilot). All agent-contributed code is reviewed by a human maintainer before merging.

---

## Quick Links

<div class="grid cards" markdown>

- :material-rocket-launch: **New? Start here**

    ---

    [Getting Started →](user-guide/getting-started.md)

- :material-desktop-classic: **Install the app**

    ---

    [Windows](user-guide/install-windows.md) · [Linux](user-guide/install-linux.md) · [Android](user-guide/install-android.md) · [Connect to Server](user-guide/connect-to-server.md)

- :material-server: **Self-host**

    ---

    [ChartHub Server](self-hosting/server-setup.md) · [Backup API](self-hosting/backup-api.md)

- :material-code-braces: **Contribute**

    ---

    [Architecture](developer/architecture.md) · [Contributing](developer/contributing.md)

</div>

---

## Song Sources

ChartHub supports:

- **RhythmVerse** — primary song catalog
- **Chorus Encore** — secondary song catalog

Downloads from both sources route through the same local destination flow and are tracked in the song library.
