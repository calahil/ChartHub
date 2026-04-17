# Architecture

This document defines the code structure for ChartHub and the boundaries contributors must follow.

## Goals

- Keep UI concerns separate from domain and infrastructure concerns.
- Keep behavior predictable across desktop and Android targets.
- Preserve long-term maintainability by enforcing strict MVVM boundaries.

## High-Level Design

ChartHub follows a strict MVVM architecture with supporting service and configuration layers.

Flow:

1. View receives user interaction.
2. View forwards intent to ViewModel.
3. ViewModel orchestrates use cases via interfaces.
4. Services and configuration stores perform IO and integration work.
5. ViewModel updates state for the View to render.

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
- Direct dependencies on view namespaces.

### Model (`ChartHub/Models`)

Allowed:

- Pure data contracts and value/state containers.

Not allowed:

- Side effects.
- IO.

### Services and Infrastructure (`ChartHub/Services`, `ChartHub/Configuration`, platform folders)

Allowed:

- All external IO (filesystem, HTTP, DB, OS/platform integration, parsing).
- Provider integrations (RhythmVerse, Encore, Google Drive, sync APIs).
- Config persistence and migration.

Not allowed:

- UI rendering concerns.
- Dependencies on View or Control namespaces.

## Repository Structure

- `ChartHub/`
- `ChartHub/Views/`: Window/page views.
- `ChartHub/Controls/`: Reusable UI controls.
- `ChartHub/ViewModels/`: Presentation state and orchestration.
- `ChartHub/Models/`: Data models and DTO-like contracts.
- `ChartHub/Services/`: Business services, integration clients, parsers, orchestrators.
- `ChartHub/Configuration/`: Config models, metadata, migration, storage abstractions.
- `ChartHub/Utilities/`: Shared helpers that do not violate layer boundaries.
- `ChartHub/Android/`: Android-specific platform integration.
- `ChartHub.Tests/`: Unit and integration-style tests for behavior and boundaries.

## Boundary Enforcement

Architecture boundaries are enforced by tests, including:

- `ArchitectureBoundariesTests.Services_And_Configuration_DoNotDependOn_ViewLayer`
- `ArchitectureBoundariesTests.ViewModels_DoNotDependOn_ViewsOrControls`

These guard against forbidden dependencies from:

- Services/Configuration -> Views/Controls
- ViewModels -> Views/Controls

## Dependency Rules

- Prefer interface-based dependencies from ViewModels to services.
- Keep dependencies flowing inward toward stable abstractions.
- Avoid introducing new dependencies unless clearly necessary.

## Async and Error Handling

- Use async end-to-end for IO paths where possible.
- Do not block on async (`.Result` and `.Wait()` are forbidden).
- Avoid `async void` except UI event handlers.
- Handle failure paths explicitly with meaningful context.

## Change Checklist

Before merging architecture-impacting changes:

1. Verify MVVM responsibilities are still respected.
2. Verify no IO was added to ViewModels.
3. Verify no View/Control dependencies were introduced in Services or Configuration.
4. Build with zero warnings.
5. Run and pass tests.

## Notes for Contributors

- Prefer small, focused diffs.
- Follow existing project patterns over novelty.
- If behavior changes, add or update tests in `ChartHub.Tests`.
