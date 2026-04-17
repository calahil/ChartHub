# Architecture

ChartHub follows strict MVVM architecture. This page summarizes the key boundaries. The authoritative document is [`.governance/architecture.md`](https://github.com/calahilstudios/charthub/blob/main/.governance/architecture.md) in the repository.

---

## MVVM Layer Boundaries

```
View → ViewModel → Service/Configuration → ViewModel → View
```

### View (`ChartHub/Views`, `ChartHub/Controls`)

- XAML rendering, binding declarations, visual states, presentation-only event handlers
- **Not allowed**: business logic, direct IO, calling service implementations

### ViewModel (`ChartHub/ViewModels`)

- UI state, command orchestration, mapping service results to presentation models
- **Not allowed**: direct IO (filesystem, HTTP, DB), parsing logic, View/Control dependencies

### Model (`ChartHub/Models`)

- Pure data contracts and value containers
- **Not allowed**: side effects, IO

### Services and Infrastructure (`ChartHub/Services`, `ChartHub/Configuration`)

- All external IO (filesystem, HTTP, DB, OS/platform integration, parsing)
- Provider integrations (RhythmVerse, Encore, sync APIs)
- **Not allowed**: UI rendering concerns, dependencies on View or Control namespaces

---

## Repository Structure

| Path | Purpose |
|---|---|
| `ChartHub/Views/` | Window and page views |
| `ChartHub/Controls/` | Reusable UI controls |
| `ChartHub/ViewModels/` | Presentation state and orchestration |
| `ChartHub/Models/` | Data models and DTO-like contracts |
| `ChartHub/Services/` | Business services, integration clients, parsers |
| `ChartHub/Configuration/` | Config models, metadata, migration, storage |
| `ChartHub/Utilities/` | Shared helpers (layer-boundary-safe) |
| `ChartHub/Android/` | Android-specific platform integration |
| `ChartHub.Tests/` | Unit and integration tests |
| `ChartHub.Server/` | Server API — library management and virtual input |
| `ChartHub.Hud/` | Emubox WM splash/status UI for ChartHub Server |
| `ChartHub.BackupApi/` | RhythmVerse mirror service |
| `.governance/` | Agent governance, architecture rules, contribution policy |

---

## Boundary Enforcement

Architecture boundaries are enforced by tests:

- `ArchitectureBoundariesTests.Services_And_Configuration_DoNotDependOn_ViewLayer`
- `ArchitectureBoundariesTests.ViewModels_DoNotDependOn_ViewsOrControls`

---

## Platform-Specific Code

All Android-specific code and usings must be guarded with `#if ANDROID`.

---

## Async Rules

- Use async end-to-end for IO paths.
- `.Result` and `.Wait()` are forbidden.
- Avoid `async void` except for UI event handlers.

---

## Contributing

See [Contributing](contributing.md) for the full contribution workflow and definition of done.
