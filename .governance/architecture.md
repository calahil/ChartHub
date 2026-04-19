# Architecture

This document defines the **rules** contributors must follow. The authoritative **reference** (full service catalog, ViewModel list, DI graph, ingestion pipeline, build settings) is in [`docs/developer/architecture.md`](../docs/developer/architecture.md).

## Goals

- Keep UI concerns separate from domain and infrastructure concerns.
- Keep behavior predictable across desktop and Android targets.
- Preserve long-term maintainability by enforcing strict MVVM boundaries.

## High-Level Design

ChartHub follows a strict MVVM architecture with supporting service and configuration layers.

Required directional flow:

`View -> ViewModel -> Service/Configuration -> ViewModel -> View`

Disallowed directional flow:

- `View -> Service`
- `View -> direct Model mutation`
- `Service -> View`

## Layer Responsibilities

### View (`ChartHub/Views`, `ChartHub/Controls`)

Allowed:

- XAML and UI rendering logic.
- Binding declarations, visual states, and presentation-only event handling.

Not allowed:

- Business logic.
- Direct file system, network, database, or parser calls.
- Calling service implementations directly.

### ViewModel (`ChartHub/ViewModels`)

Allowed:

- UI state and command orchestration.
- Coordinating use cases through service interfaces.
- Mapping service results into presentation models.

Not allowed:

- Direct IO (filesystem, HTTP, DB, platform APIs).
- Parsing logic implementation.
- Direct dependencies on `ChartHub.Views` or `ChartHub.Controls` namespaces.

### Model (`ChartHub/Models`)

Allowed:

- Pure data contracts and value/state containers.

Not allowed:

- Side effects.
- IO.

### Services and Infrastructure (`ChartHub/Services`, `ChartHub/Configuration`, platform folders)

Allowed:

- All external IO (filesystem, HTTP, DB, OS/platform integration, parsing).
- Provider integrations (RhythmVerse, Encore, Google Auth, sync APIs).
- Config persistence and migration.

Not allowed:

- UI rendering concerns.
- Dependencies on `ChartHub.Views` or `ChartHub.Controls` namespaces.

## Boundary Enforcement

Architecture boundaries are enforced by tests:

- `ArchitectureBoundariesTests.Services_And_Configuration_DoNotDependOn_ViewLayer`
- `ArchitectureBoundariesTests.ViewModels_DoNotDependOn_ViewsOrControls`

These guard against forbidden namespace imports from Services/Configuration or ViewModels into Views/Controls.

## Dependency Rules

- Prefer interface-based dependencies from ViewModels to services.
- Keep dependencies flowing inward toward stable abstractions.
- Avoid introducing new dependencies unless clearly necessary.

## Platform-Specific Code

All Android-specific code and usings must be guarded with `#if ANDROID`.

## Async and Error Handling

- Use async end-to-end for IO paths where possible.
- Do not block on async (`.Result` and `.Wait()` are forbidden).
- Avoid `async void` except UI event handlers.
- Handle failure paths explicitly with meaningful context.
- Avoid catch-all `catch (Exception)` unless justified.

## Change Checklist

Before merging architecture-impacting changes:

1. Verify MVVM responsibilities are still respected.
2. Verify no IO was added to ViewModels.
3. Verify no View/Control dependencies were introduced in Services or Configuration.
4. Build with zero warnings (`TreatWarningsAsErrors` is active).
5. Run and pass tests. Update `docs/developer/architecture.md` if the catalog changed.

## Notes for Contributors

- Prefer small, focused diffs.
- Follow existing project patterns over novelty.
- If behavior changes, add or update tests in `ChartHub.Tests`.
