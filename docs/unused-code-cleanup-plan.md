# Unused Code Cleanup Plan

## Objective

Track the cleanup of dangling, unused, or legacy code in small phases with explicit validation after each batch.

## Current Baseline

- Date: 2026-03-21
- Build baseline: `dotnet build ChartHub/ChartHub.csproj`
- Test baseline: `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`
- Latest verified result: build passed, tests passed (`219/219`)

## Success Criteria

- No build regressions.
- No test regressions.
- No removal of code that is used via DI, reflection, XAML binding, or platform-specific entry points.
- Each cleanup batch is small enough to review and revert independently.

## Phase 0: Baseline And Safety

Status: Not started

### Tasks

- [ ] Create a dedicated cleanup branch.
- [ ] Re-run build baseline.
- [ ] Re-run test baseline.
- [ ] Capture any warnings or analyzer output before removing code.

### Validation

- [ ] `dotnet build ChartHub/ChartHub.csproj`
- [ ] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`

### Notes

- Keep this phase as the comparison point for every later change.

## Phase 1: Remove Orphaned Project

Status: Ready

### Scope

- `SettingsManager/`
- Solution entry in `ChartHub.sln`

### Evidence

- `SettingsManager` is included in the solution but is not referenced by `ChartHub` or `ChartHub.Tests`.
- The project contains legacy/stub code and commented properties.

### Tasks

- [ ] Remove `SettingsManager/SettingsManager.csproj` from `ChartHub.sln`.
- [ ] Delete the `SettingsManager/` directory.
- [ ] Search the repo for any remaining `SettingsManager` references.
- [ ] Confirm no documentation still refers to the removed project.

### Validation

- [ ] `dotnet sln ChartHub.sln list`
- [ ] `dotnet build ChartHub/ChartHub.csproj`
- [ ] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`
- [ ] Search for `SettingsManager` returns no active app/test references.

### Risks

- External workflows outside this repository may still assume the project exists.

## Phase 2: Remove Or Clarify Explicitly Unused Watcher Code

Status: Ready

### Scope

- `ChartHub/Services/ResourceWatcher.cs`

### Evidence

- `FileSystemWatcher.Changed` is subscribed.
- `OnChanged` currently has an intentionally empty body.

### Tasks

- [ ] Decide whether `Changed` events are unnecessary or just unimplemented.
- [ ] If unnecessary, remove the event subscription and handler.
- [ ] If necessary, implement the expected behavior with tests.
- [ ] Check git history if intent is unclear before removing it.

### Validation

- [ ] `dotnet build ChartHub/ChartHub.csproj`
- [ ] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`
- [ ] Desktop watcher behavior still works for create, rename, and delete flows.

### Risks

- `Changed` events may have been left disabled intentionally to avoid noisy refresh behavior.

## Phase 3: Consolidate Watcher Duplication

Status: Proposed

### Scope

- `ChartHub/Services/ResourceWatcher.cs`
- `ChartHub/Services/SnapshotResourceWatcher.cs`
- `ChartHub/ViewModels/DownloadViewModel.cs`

### Evidence

- Android uses `SnapshotResourceWatcher`.
- Desktop uses `ResourceWatcher`.
- Shared file typing and icon-selection logic can drift over time.

### Tasks

- [ ] Identify the shared logic that can move into a helper or base type.
- [ ] Extract only the shared logic, not the platform-specific behavior.
- [ ] Keep Android snapshot mode and desktop live-watching behavior separate.
- [ ] Add or update tests around watcher file typing if extraction changes behavior.

### Validation

- [ ] `dotnet build ChartHub/ChartHub.csproj`
- [ ] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`
- [ ] Manual smoke test for download list behavior on desktop.
- [ ] Manual smoke test for Android if this phase changes platform code.

### Risks

- Refactoring here is cleanup, not dead-code removal. Keep scope tight.

## Phase 4: Add Ongoing Unused-Code Detection

Status: Proposed

### Scope

- Roslyn analyzers / optional Roslynator workflow
- Build or CI configuration if adopted

### Tasks

- [ ] Decide which unused-code diagnostics are worth enabling.
- [ ] Start in report-only mode.
- [ ] Triage findings into real issues vs false positives.
- [ ] Promote stable diagnostics into the normal build/CI gate.

### Validation

- [ ] Analyzer output is reviewed and actionable.
- [ ] No high-noise rules are enabled without a suppression strategy.

### Risks

- Reflection, XAML, DI, and platform entry points can look unused to analyzers.

## Phase 5: Ongoing Triage Batches

Status: Proposed

### Candidate Categories

- [ ] Declaration-only symbols
- [ ] Thin wrappers with low architectural value
- [ ] Legacy comments and commented-out code
- [ ] Old compatibility paths that are no longer exercised
- [ ] Duplicate platform checks that can be centralized without changing behavior

### Batch Rules

- [ ] Limit each batch to one narrow concern.
- [ ] Re-run build and tests after each batch.
- [ ] Record what was removed and why.
- [ ] Stop if a candidate is used indirectly by reflection, DI, or XAML.

## Verified Keep List

Do not remove these without deeper analysis:

- Settings metadata attributes under `ChartHub/Configuration/Metadata/` are used via reflection in `SettingsViewModel`.
- Test sample files under `ChartHub/Tests/` are consumed by `ChartHub.Tests`.
- `ICloudStorageAccountService` and its implementation are active through DI and view model usage.
- Platform-specific Android and desktop code paths must be checked separately before deletion.

## Progress Log

### 2026-03-21

- Baseline analysis completed.
- Build verified.
- Test suite verified: `219/219` passing.
- High-confidence orphan identified: `SettingsManager` project.
- Low-confidence dangling handler identified: `ResourceWatcher.OnChanged`.

## Decision Log

Use this section to record decisions that affect later cleanup work.

- [ ] Keep `SnapshotResourceWatcher` as a platform-specific optimization.
- [ ] Remove `SnapshotResourceWatcher` after validation proves it is redundant.
- [ ] Keep `ICloudStorageAccountService` as an architectural seam.
- [ ] Inline `ICloudStorageAccountService` if the abstraction no longer adds value.