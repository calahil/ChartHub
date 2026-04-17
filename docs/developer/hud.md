# ChartHub Hud

ChartHub Hud is the Emubox window manager splash screen and status UI that pairs with ChartHub Server.

---

## Purpose

ChartHub Hud is designed to run on a dedicated emulation or media PC using Emubox WM. It provides:

- A splash/startup screen while ChartHub Server initializes.
- A status display showing server readiness and connectivity.
- Integration with the Emubox window manager session lifecycle.

---

## Architecture

ChartHub Hud is a separate Avalonia application (`ChartHub.Hud/`) that communicates with ChartHub Server over its local API. It follows the same strict MVVM architecture as the main ChartHub client.

| Path | Purpose |
|---|---|
| `ChartHub.Hud/Views/` | Splash and status views |
| `ChartHub.Hud/ViewModels/` | Status state and server connectivity orchestration |
| `ChartHub.Hud/Services/` | ChartHub Server health polling |

---

## Running

ChartHub Hud is typically launched by the Emubox WM session startup script before ChartHub Server is fully ready, then dismissed automatically once the server reports healthy.

For manual testing:

```bash
dotnet run --project ChartHub.Hud/ChartHub.Hud.csproj
```

ChartHub Server must be running (or starting) on the same host for the status indicators to populate.
