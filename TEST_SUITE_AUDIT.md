# Test Suite Audit And Expansion Plan

## Implementation Checklist

Status key:

- `[x]` completed
- `[-]` in progress / started
- `[ ]` not started

### Phase 0. Test Infrastructure Hardening

- [x] Switch test project to reference application assembly (`ProjectReference`) and remove linked source-file compile hacks.
- [x] Add reusable test helper utilities for temp directories, deterministic IDs, and cancellation helpers.
- [x] Introduce explicit test categories (`Unit`, `IntegrationLite`) and tagging conventions via `Trait("Category", "Unit|IntegrationLite")`.

Phase 0 status: `[x] complete`

### Phase 1. High-Risk Orchestrator Coverage

- [x] Add `TransferOrchestrator` tests for invalid metadata, local success, Drive copy-first success/fallback, cancellation, and exception paths.
- [x] Add `SettingsOrchestrator` tests for valid update persistence, invalid update rejection, reload semantics, and clone isolation.
- [x] Add `JsonAppConfigStore` and `DefaultConfigValidator` tests for defaults, legacy mapping, atomic save behavior, and path validation.

Phase 1 status: `[x] complete`

### Phase 2. Auth/Drive And Watcher Coverage

- [x] Add `GoogleAuthTokenDataStore` tests for store/get/delete/clear and registry integrity.
- [x] Add `GoogleDriveWatcher` tests for missing folder start behavior, list-sync add/remove behavior, and cancellation exit.
- [x] Add `AuthGateViewModel` and `AppShellViewModel` tests for sign-in/sign-out state transitions and error handling.

Phase 2 status: `[x] complete`

### Phase 3. ViewModel Behavior Coverage

- [x] Add `SettingsViewModel` tests for metadata field generation, live validation, command enablement, and secret state transitions.
- [x] Add `DownloadViewModel` tests for check-state behavior and transfer command wiring.
- [x] Add `MainViewModel` tests for tab visibility and background task fault observation behavior.

Phase 3 status: `[x] complete`

### Phase 4. API Mapping + Process Pipeline Safety

- [x] Add `ApiClientService` response mapping and mock fallback tests.
- [x] Introduce an `OnyxService` process seam and add command/failure behavior tests.
- [x] Add `AppBootstrapper` smoke tests for service resolution and migration non-crash guarantees.

Phase 4 status: `[x] complete`

### Current Execution Order

1. Phase 0 test infrastructure hardening
2. Phase 1 high-risk orchestrator coverage
3. Phase 2 auth/drive/watcher coverage
4. Phase 3 view-model behavior coverage
5. Phase 4 API/process/bootstrap safety

## Executive Summary

The current test suite is healthy and materially broader than the original utility-only baseline.

- Current status: `56 passed, 0 failed`.
- Scope: utilities, configuration stores/orchestrators, auth/Drive services, watchers, and key view-model behaviors.
- Breadth gap: `13` test C# files vs `71` production C# files.
- Major uncovered surfaces: API response mapping, process execution seams, and bootstrap wiring.

The biggest quality risk is not failing tests, but untested behavior in stateful orchestration paths (transfer pipeline, settings orchestration, cloud sync/auth interactions).

## What Is Covered Today

### Existing test project

- `RhythmVerseClient.Tests/RhythmVerseClient.Tests.csproj`
- Framework: `xUnit` + `Microsoft.NET.Test.Sdk` + `coverlet.collector`

### Existing test files

- `RhythmVerseClient.Tests/SafePathHelperTests.cs`
- `RhythmVerseClient.Tests/LoggerLifecycleTests.cs`
- `RhythmVerseClient.Tests/LoggerRedactionTests.cs`
- `RhythmVerseClient.Tests/AssemblyInfo.cs` (disables parallelization)

### Current validated behaviors

- Path traversal and rooted-path defense in `SafePathHelper`.
- Logger lifecycle markers (`Session started/ended`).
- Logger rotation behavior on size threshold.
- Logger redaction behavior for sensitive values in context/message/exception output.
- Concurrent logger write durability (single-process test-level confidence).

## Coverage Gaps (Risk-Ranked)

### High Risk Gaps

1. Transfer orchestration and stage outcomes
- Files: `RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs`, destination/source writer classes.
- Why high risk: controls user-visible success/failure, cloud/local data movement, cancellation, and fallback logic.
- Current tests: none.

2. Settings orchestration and persistence semantics
- Files: `RhythmVerseClient/Configuration/Stores/SettingsOrchestrator.cs`, `JsonAppConfigStore.cs`, `DefaultConfigValidator.cs`, `SettingsMigrationService.cs`.
- Why high risk: startup config validity, persistence correctness, migration of secrets/config shape.
- Current tests: none.

3. Auth and Drive lifecycle orchestration
- Files: `RhythmVerseClient/Services/IGoogleAuthProvider.cs`, `IGoogleDriveClient.cs`, `GoogleAuthTokenDataStore.cs`, `GoogleDriveWatcher.cs`.
- Why high risk: auth/session continuity, sign-in/sign-out behavior, sync polling lifecycle.
- Current tests: none.

### Medium Risk Gaps

4. View-model state transitions and command behavior
- Files: `RhythmVerseClient/ViewModels/*.cs`.
- Why medium risk: command wiring, collection updates, tab/sign-in transitions, validation feedback.
- Current tests: none.

5. API parsing and fallback behavior
- Files: `RhythmVerseClient/Services/ApiClientService.cs`.
- Why medium risk: response mapping, null/edge handling, mock-data fallback behavior.
- Current tests: none.

6. Onyx process pipeline and failure surfaces
- File: `RhythmVerseClient/Services/OnyxService.cs`.
- Why medium risk: external process execution and artifact generation.
- Current tests: none.

### Low Risk / Structural Gaps

7. DI/bootstrap wiring safety checks
- File: `RhythmVerseClient/Utilities/AppBootstrapper.cs`.
- Why low-medium risk: often fails at runtime when registrations drift.
- Current tests: none.

## Findings On Testability Constraints

1. Test project currently compiles selected source files directly
- `RhythmVerseClient.Tests/RhythmVerseClient.Tests.csproj` includes linked compile items for only `Logger.cs` and `SafePathHelper.cs`.
- Impact: most production behavior cannot be tested via normal assembly reference patterns.

2. Heavy static/global dependencies in app paths
- File system, process execution, environment/runtime checks, and platform-specific services are used directly.
- Impact: unit tests require seams (interfaces/adapters) or focused integration harnesses.

3. No explicit test layering yet
- Unit and integration boundaries are not defined.
- Impact: future suite may become slow/flaky without structure.

## Plan To Fill Out Application Tests

## Phase 0: Test Infrastructure Hardening (Week 1)

1. Switch test project to reference application assembly
- Add `<ProjectReference Include="..\RhythmVerseClient\RhythmVerseClient.csproj" />`.
- Keep isolated utility tests, but stop linked compile pattern for long-term suite growth.

2. Add test helper utilities
- Temporary directory fixture.
- Deterministic clock/test ID helpers.
- Controlled cancellation helpers.

3. Introduce test categories
- Unit: pure logic and orchestrators with fakes.
- Integration-lite: file-backed config stores and non-network persistence behavior.

Exit criteria:
- Tests can instantiate and exercise classes from `Services`, `ViewModels`, and `Configuration` without source-link hacks.

## Phase 1: High-Risk Orchestrator Coverage (Week 1-2)

### A. TransferOrchestrator

Add tests for:
1. Invalid request metadata returns failed result and does not throw.
2. Local destination success path sets completed stage and final location.
3. Google Drive copy-first success path returns completed without download fallback.
4. Copy-first exception triggers fallback path and logs warning/error path (result still proceeds when fallback succeeds).
5. Cancellation returns cancelled stage with user-safe error string.
6. Exception path returns failed stage with generic user-facing message.

### B. SettingsOrchestrator

Add tests for:
1. Valid update persists and raises `SettingsChanged`.
2. Invalid update returns validation failures and does not persist.
3. Reload updates `Current` from store and raises `SettingsChanged`.
4. Clone semantics avoid mutating original current config on failed updates.

### C. JsonAppConfigStore + DefaultConfigValidator

Add tests for:
1. Missing config file writes defaults.
2. Legacy flat keys map into `Runtime` and `GoogleAuth` sections.
3. Save writes atomically via temp file swap.
4. Validator rejects invalid/empty/non-local paths and accepts valid parent paths.

Exit criteria:
- Core transfer and settings persistence flows covered by deterministic unit/integration-lite tests.

## Phase 2: Auth/Drive And Watcher Coverage (Week 2-3)

### A. GoogleAuthTokenDataStore

Add tests for:
1. Store/Get roundtrip for token payloads.
2. Delete removes key and registry bookkeeping.
3. Clear removes all registered keys.

### B. GoogleDriveWatcher

Add tests for:
1. Start skipped when folder id is missing.
2. Load updates added/removed items from list results.
3. Poll cancellation exits cleanly.

### C. Auth/ViewModel flow

Add tests for `AuthGateViewModel` and `AppShellViewModel`:
1. Successful sign-in transitions to main view model and signed-in state.
2. Failed sign-in sets user-facing error while preserving state.
3. Sign-out resets shell state and returns to auth gate.

Exit criteria:
- Sign-in/out and watcher lifecycle are validated in automated tests.

## Phase 3: ViewModel Behavior Coverage (Week 3-4)

### A. SettingsViewModel

Add tests for:
1. Field generation from metadata.
2. Live validation error surfacing for path fields.
3. Save command enablement based on validation state.
4. Secret save/clear state transitions with fake secret store.

### B. DownloadViewModel and MainViewModel

Add tests for:
1. Check-all toggles and `IsAnyChecked` state.
2. Selected cloud file download invokes orchestrator and refreshes watcher.
3. Main tab visibility behavior by platform mode abstraction.

Exit criteria:
- UI-state logic and command behavior are covered without UI-hosted integration tests.

## Phase 4: API Mapping + Process Pipeline Safety (Week 4+)

1. `ApiClientService` response mapping tests
- Null/missing fields fallback mapping.
- Mock-data fallback behavior when local data unavailable.

2. `OnyxService` seam introduction and tests
- Extract process runner interface.
- Test import/build command construction and non-zero exit handling.

3. Bootstrap smoke tests
- `AppBootstrapper.CreateServiceProvider` resolves critical services.
- Migration path does not crash provider build.

Exit criteria:
- External boundary behavior is covered via seams and smoke checks.

## Proposed First 20 Tests (Immediate Backlog)

1. Transfer invalid metadata fails safely.
2. Transfer local success sets completed stage.
3. Transfer copy-first success skips fallback.
4. Transfer copy-first failure triggers fallback.
5. Transfer cancellation returns cancelled result.
6. Transfer exception returns user-safe failure message.
7. SettingsOrchestrator valid update persists.
8. SettingsOrchestrator invalid update blocks save.
9. SettingsOrchestrator reload raises event.
10. SettingsOrchestrator clone isolation on failed update.
11. JsonAppConfigStore load creates defaults.
12. JsonAppConfigStore legacy mapping for runtime keys.
13. JsonAppConfigStore legacy mapping for Google auth keys.
14. DefaultConfigValidator rejects blank required paths.
15. DefaultConfigValidator rejects non-local URI paths.
16. GoogleAuthTokenDataStore roundtrip stores payload.
17. GoogleAuthTokenDataStore clear removes registered keys.
18. AppShellViewModel successful auth switches to main view.
19. AppShellViewModel sign-out resets auth gate.
20. AuthGateViewModel failure surfaces expected user message.

## Success Metrics

- Short-term target: increase from `11` tests to `50+` tests focused on high-risk orchestration and configuration logic.
- Mid-term target: `100+` tests with balanced unit/integration-lite split.
- Coverage target (non-UI):
  - `Services/Transfers`: >= 75%
  - `Configuration/Stores`: >= 80%
  - `ViewModels` (logic only): >= 60%

## Recommended Process Changes

1. Require tests for every bug fix touching `Services`, `Configuration`, or `ViewModels`.
2. Add CI gate for test execution on each merge request.
3. Add monthly test-gap review against changed files.
4. Keep flaky tests at zero by isolating time/process/fs dependencies behind test seams.

## Next Step

Implement Phase 0 and Phase 1 first. That sequence gives the largest reliability gain quickly and creates the foundation for the rest of the test roadmap.
