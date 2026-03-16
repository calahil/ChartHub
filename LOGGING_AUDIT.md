# Logging Audit

## Implementation Checklist

Status key:

- `[x]` completed
- `[-]` in progress / started
- `[ ]` not started

### Phase 1. Logging foundation

- [x] Replace the current static file writer with a thread-safe logger that supports levels, categories, exception overloads, and rolling files.
- [x] Preserve backward compatibility for existing `Logger.LogMessage(...)` and `Logger.LogError(...)` call sites.
- [x] Add session lifecycle logging at app startup and shutdown.
- [x] Add logger tests for concurrent writes, exception formatting, and redaction.

### Phase 2. Failure logging policy

- [x] Standardize expected cancellation logging as `Info`/`Debug` instead of `Error`.
- [x] Standardize validation failures as `Warning` with field keys.
- [x] Standardize unexpected exceptions to include full exception details.
- [x] Add redaction rules for secrets, tokens, auth codes, and sensitive URL parts.

### Phase 3. Workflow boundary instrumentation

- [x] Instrument app bootstrap and process lifecycle.
- [x] Instrument settings load/save/validation lifecycle.
- [x] Instrument Google auth start/success/failure.
- [x] Instrument Google Drive initialization and watcher lifecycle.
- [x] Instrument transfer queueing, stage transitions, success, cancellation, and failure.
- [x] Instrument Onyx import/build lifecycle.

### Phase 4. Sink cleanup

- [x] Replace all `Console.WriteLine(...)` usage in app code with the shared logger.
- [x] Ensure desktop and Android diagnostics land in durable, consistent sinks.

### Phase 5. Correlation and timing

- [x] Add app session IDs.
- [x] Add transfer correlation IDs.
- [x] Add auth session correlation IDs.
- [x] Add elapsed-time markers to remote/process-heavy workflows.

### Phase 6. Validation and smoke coverage

- [x] Add tests for log rotation behavior.
- [x] Add tests that verify secrets are redacted.
- [x] Add a smoke path that confirms startup/shutdown headers are emitted.

### Current Execution Order

1. Phase 1 foundation
2. Phase 2 failure policy
3. Phase 3 workflow boundaries
4. Phase 4 sink cleanup
5. Phase 5 correlation and timing
6. Phase 6 validation

## Scope

This audit reviews the current logging system in RhythmVerseClient with two goals:

1. Determine whether current logs are reliable enough to diagnose failures.
2. Define a concrete plan to improve logging around the highest-value failure points.

## Executive Summary

The current logging implementation is not sufficient for reliable debugging of production failures.

Primary issues:

- Logging is implemented as a minimal static file writer with no severity levels, no event IDs, no categories, and no correlation between related operations.
- Many failure paths log only `ex.Message`, which drops stack traces, inner exceptions, and operation context.
- Logging sinks are inconsistent: some code uses the app logger, some uses `Console.WriteLine`, and some failures only update UI state with no persistent log.
- Log write failures are silently swallowed, so the logging system itself cannot be trusted or monitored.
- Critical workflows like bootstrap, Google auth, Google Drive sync, downloads, filesystem watchers, transfer orchestration, and external tool execution do not currently emit a consistent lifecycle trail.

Net effect: the app can often tell the user that something failed, but it cannot reliably explain why, where, under what inputs, or at what stage.

## Current State

### 1. Core logger is too weak

Current implementation: [RhythmVerseClient/Utilities/Logger.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Utilities/Logger.cs)

Observed behavior:

- Writes to a single `errorlog.txt` under app data.
- Supports only `LogMessage(string)` and `LogError(Exception)`.
- `LogMessage` has no severity distinction.
- `LogError` is rarely used consistently.
- Both methods silently swallow logger I/O failures.

Impact:

- No reliable signal for `Debug`, `Info`, `Warning`, `Error`, `Critical`.
- No machine-sortable categories like `Auth`, `Transfer`, `Drive`, `Watcher`, `Config`.
- No structured fields for file IDs, URLs, directory paths, backend selection, elapsed time, or operation stage.
- No indication when the logger itself stopped working.

### 2. Failure logs often lose the useful parts of the exception

Examples:

- [RhythmVerseClient/ViewModels/AuthGateViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/AuthGateViewModel.cs)
- [RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs)
- [RhythmVerseClient/Utilities/AppBootstrapper.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Utilities/AppBootstrapper.cs#L37)
- [RhythmVerseClient/Services/ResourceWatcher.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/ResourceWatcher.cs#L118)

Common pattern:

- Log message text is built from `ex.Message` only.
- Stack traces and inner exception chains are usually not preserved.
- Operation-specific state is missing.

Impact:

- Hard to distinguish transport failures, auth failures, permission problems, path validation failures, cancellation, and third-party library exceptions.

### 3. Mixed logging sinks create blind spots

Examples:

- [RhythmVerseClient/Services/IGoogleDriveClient.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/IGoogleDriveClient.cs#L94)
- [RhythmVerseClient/Services/DownloadService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/DownloadService.cs#L254)
- [RhythmVerseClient/ViewModels/DownloadViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/DownloadViewModel.cs#L173)
- [RhythmVerseClient/Services/ApiClientService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/ApiClientService.cs#L377)

Observed behavior:

- Some code writes to the persistent logger.
- Some code writes to stdout only.
- Some code updates a view-model status/error property but emits no persistent log.

Impact:

- Reconstructing a failure requires checking multiple outputs, some of which may not exist in the field.
- Desktop and Android observability differ unpredictably.

### 4. Startup and background-task failures are under-instrumented

Relevant files:

- [RhythmVerseClient/App.axaml.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/App.axaml.cs)
- [RhythmVerseClient/Utilities/AppBootstrapper.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Utilities/AppBootstrapper.cs)
- [RhythmVerseClient/ViewModels/MainViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/MainViewModel.cs#L159)
- [RhythmVerseClient/ViewModels/RhythmVerseViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/RhythmVerseViewModel.cs#L369)

Observed behavior:

- Some background-task faults are observed, but only as message text.
- App startup has no clear session header with app version, platform, config root, secret backend, or feature mode.
- Shutdown cleanup exceptions are ignored.
- No global capture is visible for unhandled exceptions, unobserved task exceptions, or first-failure app boot state.

Impact:

- Failures during startup, shutdown, or background initialization are difficult to reproduce.

### 5. Key workflows do not emit lifecycle logs

Important workflows missing start/success/failure markers:

- Google auth authorize, silent auth, refresh, revoke, sign-out.
- Google Drive service initialization and watcher polling.
- Redirect resolution, download start, download completion, archive extraction, upload, cloud copy fallback.
- Settings load, settings save, validation failure, secret save, secret clear, secret backend fallback selection.
- Onyx execution start, arguments summary, elapsed time, output path, and failure context.

Relevant files:

- [RhythmVerseClient/Services/IGoogleAuthProvider.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/IGoogleAuthProvider.cs)
- [RhythmVerseClient/Services/IGoogleDriveClient.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/IGoogleDriveClient.cs)
- [RhythmVerseClient/Services/DownloadService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/DownloadService.cs)
- [RhythmVerseClient/Services/GoogleDriveWatcher.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/GoogleDriveWatcher.cs)
- [RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs)
- [RhythmVerseClient/Services/OnyxService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/OnyxService.cs)
- [RhythmVerseClient/ViewModels/SettingsViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/SettingsViewModel.cs)

Impact:

- Even when a workflow fails, there is usually no durable breadcrumb trail showing the prior stage transitions.

## Reliability Gaps

### Missing context fields

The logs currently do not consistently capture:

- operation name
- stage name
- correlation ID / transfer ID / auth session ID
- file name and sanitized destination path
- source host or source type
- Google Drive file ID or folder ID
- selected backend or runtime mode
- elapsed time
- cancellation vs failure vs validation rejection

### Missing exception detail policy

The codebase needs a consistent rule:

- User-facing messages should stay concise.
- Persistent logs should include full exception detail.
- Expected cancellations should log at `Info` or `Debug`, not `Error`.
- Validation failures should log at `Warning` with the rejected fields.

### Missing log lifecycle policy

Current logger has no visible policy for:

- session start and end markers
- log rotation / size cap
- thread safety under concurrent writes
- Android log sink strategy
- privacy rules for secrets, tokens, auth codes, and full URLs

## High-Value Failure Points To Instrument

These are the highest-value places to add reliable debug logs first.

### App bootstrap and process lifecycle

Files:

- [RhythmVerseClient/App.axaml.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/App.axaml.cs)
- [RhythmVerseClient/Utilities/AppBootstrapper.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Utilities/AppBootstrapper.cs)

Add logs for:

- app session start
- platform and target framework
- config root path
- secret backend selected
- services boot completed
- bootstrap migration started / succeeded / failed
- app shutdown start / cleanup end
- unhandled exceptions and unobserved task exceptions

### Google auth flow

Files:

- [RhythmVerseClient/Services/IGoogleAuthProvider.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/IGoogleAuthProvider.cs)
- [RhythmVerseClient/ViewModels/AuthGateViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/AuthGateViewModel.cs)

Add logs for:

- interactive auth requested
- silent auth attempted
- token loaded from store vs migrated from legacy store
- token refresh attempted / succeeded / failed
- revoke attempted / failed
- sign-out cleanup completed
- auth failure category with provider, platform, and non-secret client-id source

Never log:

- auth codes
- access tokens
- refresh tokens
- client secrets

### Google Drive service and watcher

Files:

- [RhythmVerseClient/Services/IGoogleDriveClient.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/IGoogleDriveClient.cs)
- [RhythmVerseClient/Services/GoogleDriveWatcher.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/GoogleDriveWatcher.cs)

Add logs for:

- drive initialization start / success / failure
- folder discovery or creation result
- watcher start / stop / poll cycle failures
- created / deleted remote file events through logger, not console
- file count returned from list operations

### Download and transfer pipeline

Files:

- [RhythmVerseClient/Services/DownloadService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/DownloadService.cs)
- [RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/Transfers/TransferOrchestrator.cs)
- [RhythmVerseClient/ViewModels/RhythmVerseViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/RhythmVerseViewModel.cs)

Add logs for:

- transfer requested with source type and destination type
- redirect resolution result with final host only
- download started with file name and destination container
- Google Drive direct copy attempted / skipped / failed / fell back
- folder zip download started / completed
- local move started / completed
- upload started / completed
- cancellation requested / observed
- transfer failed with full exception plus stage and identifiers

### File watchers and local storage inspection

Files:

- [RhythmVerseClient/Services/ResourceWatcher.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/ResourceWatcher.cs)
- [RhythmVerseClient/Services/SnapshotResourceWatcher.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/SnapshotResourceWatcher.cs)
- [RhythmVerseClient/ViewModels/DownloadViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/DownloadViewModel.cs)

Add logs for:

- watcher created for path
- watched directory missing
- file type probe failures with path and exception type
- add / delete / rename event counts
- upload failures currently written to console only

### Settings and secret storage

Files:

- [RhythmVerseClient/ViewModels/SettingsViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/SettingsViewModel.cs)
- [RhythmVerseClient/Configuration/Stores/PlatformSecretStores.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Configuration/Stores/PlatformSecretStores.cs)
- [RhythmVerseClient/Utilities/Initializer.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Utilities/Initializer.cs)

Add logs for:

- settings reload requested / completed
- settings save requested / validated / rejected / persisted
- number of changed fields and which logical sections changed
- secret backend selected and whether it is OS-backed or fallback
- secret write / clear failures with key names only, never values
- async runtime config update failures with field names

### Onyx execution

File:

- [RhythmVerseClient/Services/OnyxService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/OnyxService.cs)

Add logs for:

- executable resolution result
- import start / finish
- build start / finish
- elapsed time per phase
- destination path
- exit code and stderr summary on failure

## Improvement Plan

### Phase 1: Replace the logger foundation

Priority: critical

Implement a real application logger with:

- severity levels: `Debug`, `Info`, `Warning`, `Error`, `Critical`
- categories: `App`, `Bootstrap`, `Auth`, `Drive`, `Transfer`, `Watcher`, `Config`, `Secrets`, `Onyx`
- overloads that accept both message and exception
- structured context fields as key-value pairs
- thread-safe writes
- session header and footer
- rolling file behavior or size-based truncation

Minimum API shape:

```csharp
AppLog.Debug("Transfer", "Transfer queued", new { transferId, sourceType, destinationType, displayName });
AppLog.Error("Auth", ex, "Interactive auth failed", new { provider = "Google", mode = "Desktop" });
```

Recommendation:

- Prefer a small wrapper over `Microsoft.Extensions.Logging` so the rest of the app can stay simple.
- If you want minimal churn, keep a static facade but back it with a proper implementation.

### Phase 2: Standardize failure logging rules

Priority: critical

Adopt these rules everywhere:

- `catch (OperationCanceledException)`: log as `Info` or `Debug` with operation and stage.
- validation rejection: log as `Warning` with rejected keys.
- unexpected exceptions: log as `Error` with full exception object.
- process-wide failure hooks: log as `Critical`.

Add a redact policy:

- never log secrets, tokens, raw auth responses, or full credential payloads
- avoid full query strings in URLs unless explicitly scrubbed
- log path roots and file names, but avoid dumping arbitrary file contents

### Phase 3: Instrument workflow boundaries first

Priority: high

Instrument these boundaries before deeper per-method logs:

1. app start and app shutdown
2. settings load and save
3. Google auth start / success / failure
4. drive initialize and watcher start
5. transfer queued / stage transition / success / fail / cancel
6. Onyx import and build

This gives immediate diagnostic value without flooding the codebase.

### Phase 4: Replace all `Console.WriteLine`

Priority: high

Every `Console.WriteLine` in application code should be replaced with the shared logger so all diagnostics land in one durable sink.

Known current targets include:

- [RhythmVerseClient/Services/IGoogleDriveClient.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/IGoogleDriveClient.cs#L94)
- [RhythmVerseClient/Services/DownloadService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/DownloadService.cs#L254)
- [RhythmVerseClient/ViewModels/DownloadViewModel.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/ViewModels/DownloadViewModel.cs#L173)
- [RhythmVerseClient/Services/ApiClientService.cs](/srv/games/source/rhythmverse-client/rhythmverseclient/RhythmVerseClient/Services/ApiClientService.cs#L377)

### Phase 5: Add correlation IDs for long-running operations

Priority: high

Introduce IDs for:

- auth sessions
- transfer jobs
- watcher instances
- app session

Every log within a workflow should include the relevant correlation ID.

This is the difference between “download failed” and “transfer `trf-20260316-0042` failed after redirect resolution succeeded and Drive copy fallback started”.

### Phase 6: Add elapsed time and stage markers

Priority: medium

For remote/network/process-bound workflows, capture:

- started-at timestamp
- completed-at timestamp
- elapsed milliseconds
- current stage

Best initial targets:

- drive initialization
- download pipeline
- transfer orchestration
- Onyx import/build
- settings persistence

### Phase 7: Add logging tests and smoke checks

Priority: medium

Add tests that verify:

- exception overloads preserve stack trace text
- secrets are redacted
- concurrent writes do not corrupt log lines
- session header is emitted once per app run
- log sink remains writable under repeated failures

## Recommended Initial Backlog

This is the concrete order I would implement.

1. Introduce `AppLog` with levels, categories, exception overloads, and rolling file sink.
2. Add session-start logging in app bootstrap and global exception hooks.
3. Replace every `Console.WriteLine` with `AppLog`.
4. Instrument Google auth, Google Drive init, transfer orchestration, and Onyx execution with start/success/failure markers.
5. Add correlation IDs to transfer and auth flows.
6. Add settings/config logging with validation and persistence summaries.
7. Add tests for logger reliability and redaction.

## Example Of Good Failure Logging

Desired pattern:

```text
[2026-03-16T18:14:23.551Z] [Error] [Transfer] Transfer failed
sessionId=app-20260316-181101
transferId=trf-20260316-0042
stage=Uploading
sourceType=GoogleDriveFile
destinationType=GoogleDrive
displayName=Artist - Song.zip
driveFileId=1abcDEF...
exceptionType=HttpRequestException
message=Unexpected response status code: Forbidden
stackTrace=...
```

This gives enough information to reproduce, classify, and correlate the issue.

## Conclusion

The current system provides only partial and fragile diagnostics. The next step should not be “add more `Logger.LogMessage` calls” to the existing implementation. The correct move is to first replace the logging foundation, then instrument the major workflow boundaries with structured, durable, redacted, stage-aware logs.

That will make failures in auth, sync, downloads, settings, and external tooling materially easier to debug.