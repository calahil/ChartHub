# Getting Started

ChartHub lets you browse, download, and manage rhythm game song charts from RhythmVerse and Chorus Encore, then convert them for Clone Hero.

---

## What is ChartHub?

ChartHub is a multi-component system:

| Component | What it does |
|---|---|
| **Desktop app** | Browse, search, and download songs. Hosts the local sync API for Android. |
| **Android app** | Trigger downloads from your phone, remote-control the desktop, and use virtual input (gamepad, mouse, keyboard). |
| **ChartHub Server** | Optional server component for library management and virtual input. Typically runs on a dedicated machine or alongside an emulation setup. |
| **ChartHub Hud** | Optional Emubox WM splash/status screen that pairs with ChartHub Server. |
| **Backup API** | Optional self-hosted RhythmVerse mirror for resilient access when the upstream source is unreliable. |

---

## Typical Workflow

1. Install the **desktop app** on your Windows or Linux machine.
2. Log in with your RhythmVerse account.
3. Search for songs and download them — they are automatically staged and converted for Clone Hero.
4. Optionally install the **Android app** and pair it to your desktop for remote song queuing and input control.
5. Optionally run **ChartHub Server** on a dedicated machine to manage a shared library.

---

## Next Steps

- [Install on Windows →](install-windows.md)
- [Install on Linux →](install-linux.md)
- [Install on Android →](install-android.md)
- [Connect to ChartHub Server →](connect-to-server.md)
