# Unused Code Cleanup Plan

## Objective

Track cleanup of dangling, unused, or legacy code in small phases with explicit validation after each batch.

## Current Baseline

- Date: 2026-03-21
- Build baseline: `dotnet build ChartHub/ChartHub.csproj`
- Test baseline: `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`
- Latest verified result: build passed, tests passed (`219/219`)

## Success Criteria

- No build regressions.
- No test regressions.
- No removal of code used via DI, reflection, XAML binding, or platform-specific entry points.
- Each cleanup batch stays small enough to review and revert independently.

## Phase 0: Baseline And Safety

Status: Complete

### Tasks

- [x] Create a dedicated cleanup branch.
- [x] Re-run build baseline.
- [x] Re-run test baseline.
- [x] Capture warnings/analyzer output before removal work.

### Validation

- [x] `dotnet build ChartHub/ChartHub.csproj`
- [x] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`

## Phase 1: Remove Orphaned Project

Status: Complete

### Scope

- `SettingsManager/`
- Solution entry in `ChartHub.sln`

### Tasks

- [x] Remove `SettingsManager/SettingsManager.csproj` from `ChartHub.sln`.
- [x] Delete the `SettingsManager/` directory.
- [x] Search repo for remaining `SettingsManager` references.

### Validation

- [x] `dotnet sln ChartHub.sln list`
- [x] `dotnet build ChartHub/ChartHub.csproj`
- [x] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`

## Phase 2: Remove Explicitly Unused Watcher Handler

Status: Complete

### Scope

- `ChartHub/Services/ResourceWatcher.cs`

### Tasks

- [x] Verify `FileSystemWatcher.Changed` handler intent from code/history.
- [x] Remove no-op `Changed` subscription and empty handler.

### Validation

- [x] `dotnet build ChartHub/ChartHub.csproj`
- [x] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`

## Phase 3: Consolidate Watcher Duplication

Status: Complete

### Scope

- `ChartHub/Services/ResourceWatcher.cs`
- `ChartHub/Services/SnapshotResourceWatcher.cs`
- `ChartHub/Services/WatcherFileTypeResolver.cs`

### Tasks

- [x] Extract shared watcher file typing/icon logic into a helper.
- [x] Keep platform-specific watcher behavior separate.
- [x] Verify desktop and Android smoke behavior.

### Validation

- [x] `dotnet build ChartHub/ChartHub.csproj`
- [x] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj`
- [x] Desktop smoke check passed (after resolving local port conflict).
- [x] Android smoke check passed.

## Phase 4: Add Ongoing Unused-Code Detection

Status: Complete

### Scope

- Roslyn diagnostics in local build flow
- Build configuration for report-only analyzer visibility

### Tasks

- [x] Decide which unused-code diagnostics to enable.
- [x] Start in report-only mode.
- [x] Triage false positives vs real findings.
- [x] Keep rules non-blocking while stabilizing noise.

### Validation

- [x] Analyzer output is reviewed and actionable.
- [x] False-positive suppression strategy is documented and scoped.

### Notes

- Added repo-root `.editorconfig`:
  - `IDE0051 = warning`
  - `IDE0052 = warning`
  - `IDE0060 = suggestion`
  - Per-file suppression: `ChartHub/Services/IGoogleAuthProvider.cs` sets `IDE0051 = none` for `#if ANDROID` false positives.
- Added repo-root `Directory.Build.props` with `EnforceCodeStyleInBuild=true`.

## Phase 5: Ongoing Triage Batches

Status: In Progress

### Candidate Categories

- [x] Declaration-only symbols
- [x] Thin wrappers with low architectural value
- [x] Legacy comments and commented-out code
- [x] Old compatibility paths no longer exercised
- [x] Duplicate platform checks (no consolidation opportunities identified)

### Confirmed Batch Items

#### Batch A: Write-only/dead injected fields

- [x] Remove `ApiClientService._isAndroid` and associated constructor dependency.
- [x] Remove `ApiClientService.ResponseDebug` write-only debug field.
- [x] Remove unused `EncoreViewModel._libraryCatalog` constructor dependency and field.

#### Batch B: Unused convenience overload

- [x] Remove `IngestionSyncApiHost.ExecuteSerializedMutationAsync(Func<Task>, CancellationToken)` wrapper overload.

#### Batch C: Legacy commented-out code

- [x] Remove stale `//string payload;` commented-out line from `ApiClientService.GetSongFilesAsync`.

#### Batch D: Thin wrapper cleanup

- [x] Simplify `AppShellViewModel.SwitchToMainAsync` (private wrapper returning `Task.CompletedTask`) into synchronous `SwitchToMain`.
- [x] Remove unnecessary await call site in `HandlePostSplashAsync`.

#### Batch E: Old compatibility path removal

- [x] Remove backward-compatible `SongInstallService` constructor overload used for historical test setup.
- [x] Update host/test caller to use the primary `SongInstallService` constructor with explicit dependencies.

### Phase 5 Validation (Current)

- [x] `dotnet build ChartHub/ChartHub.csproj -f net10.0`
- [x] `dotnet test ChartHub.Tests/ChartHub.Tests.csproj --no-build`
- [x] No remaining `IDE0051`/`IDE0052` warnings from current Phase 5 targets.

## Verified Keep List

Do not remove these without deeper analysis:

- Settings metadata attributes under `ChartHub/Configuration/Metadata/` are used via reflection in `SettingsViewModel`.
- Test sample files under `ChartHub/Tests/` are consumed by `ChartHub.Tests`.
- `ICloudStorageAccountService` and its implementation are active through DI and view model usage.
- Platform-specific Android and desktop code paths must be validated separately before deletion.

## Progress Log

### 2026-03-21

- Phase 0 completed (build/test baseline, cleanup branch created).
- Phase 1 completed (removed orphaned `SettingsManager` project and directory).
- Phase 2 completed (removed no-op watcher `Changed` handler/subscription).
- Phase 3 completed (extracted `WatcherFileTypeResolver`, validated desktop and Android smoke).
- Phase 4 completed (enabled report-only unused-code detection, triaged Android preprocessor false positives).
- Phase 5 started.
- Phase 5 Batch A completed:
  - Removed `ApiClientService._isAndroid` and internal test-constructor dependency.
  - Removed `ApiClientService.ResponseDebug`.
  - Removed unused `EncoreViewModel._libraryCatalog` dependency.
- Phase 5 Batch B completed:
  - Removed unused `IngestionSyncApiHost` 2-arg mutation wrapper overload.
- Phase 5 Batch C completed:
  - Removed stale commented-out code line (`//string payload;`) in `ApiClientService.GetSongFilesAsync`.
- Phase 5 Batch D completed:
  - Simplified thin wrapper in `AppShellViewModel` by replacing private `SwitchToMainAsync` (returned `Task.CompletedTask`) with synchronous `SwitchToMain`.
  - Updated `HandlePostSplashAsync` to call `SwitchToMain` directly.
- Targeted auth-flow tests passed (`AuthFlowViewModelTests`: `5/5`).
- Phase 5 Batch E completed:
  - Removed backward-compatible `SongInstallService` constructor overload that existed for older test wiring.
  - Updated `IngestionSyncApiHostTests` host setup to call the primary `SongInstallService` constructor with explicit dependencies.
- Targeted compatibility-path tests passed (`IngestionSyncApiHostTests` + `SongInstallServiceTests`: `42/42`).
- Searched for additional obvious commented-out code blocks; none found that were safe and non-ambiguous for automatic removal.
- Phase 5 Batch F (duplicate platform checks) evaluated:
  - Scanned codebase for duplicate `OperatingSystem.IsAndroid()` patterns.
  - Found platform checks in `AppBootstrapper.cs` (3 checks), `MainViewModel.cs`, `RhythmVerseViewModel.cs`, `AppShellViewModel.cs`, `SettingsViewModel.cs`, and `Initializer.cs`.
  - Conclusion: All platform checks are contextually appropriate for their specific use cases (async execution strategy, service registration, UI property binding). No consolidation opportunities identified without over-engineering.
- Re-ran build: success.
- Re-ran tests: success, `219/219` passing.
- **Phase 5 Complete**: All 6 candidate categories evaluated; 5 removed safe dead-code items, 1 (duplicate platform checks) confirmed as already optimal.

## Decision Log

- [x] Keep `SnapshotResourceWatcher` as a platform-specific optimization for Android.
- [x] Keep `ICloudStorageAccountService` as an architectural seam.
- [x] Keep Android-only auth helper diagnostics suppressed at file scope where `#if ANDROID` excludes desktop call sites.
- [x] Keep current platform checks in `AppBootstrapper`, `MainViewModel`, `RhythmVerseViewModel`, etc. — Each serves a distinct purpose and consolidation would reduce code clarity.
