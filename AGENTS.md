# Agent Contribution Rules

This repository is maintained by a human owner with coding agents as contributors.
Agents must optimize for maintainability, not just passing behavior.

## Non-Negotiable Rules

1. Do not ship "hacky but works" fixes.
2. Fix root causes when reasonably possible.
3. Do not add warning suppressions unless they are verified false positives and explicitly justified.
4. Do not weaken analyzers, tests, coverage thresholds, or CI guard rails to make a change pass.
5. Do not leave TODO, HACK, or FIXME markers behind unless explicitly requested and justified.
6. Do not mass-refactor unrelated files.

## Localization Policy (Authoritative)

All new user-facing strings MUST use localization resources.

This applies to:
1. UI text in AXAML and ViewModels.
2. User-visible API text (response messages, validation messages, and error messages surfaced to clients).

Hardcoded user-facing literals in production code are forbidden.

### Allowed Exceptions

The following may remain hardcoded:
1. Icon/resource URIs and non-localizable asset identifiers.
2. Telemetry/event keys, metric names, and structured logging keys.
3. Protocol/format constants (for example HTTP headers, JSON field names, MIME types).
4. Test-only literals inside test projects.

### Touch-File Migration Rule

When a file is modified, any nearby hardcoded user-facing strings in that file should be migrated to localization in the same change unless explicitly scoped out in the PR summary with justification.

### Examples

Bad:
1. `Content="Refresh Library"` in AXAML.
2. `StatusMessage = "Failed to refresh";` in production ViewModel/API code.

Good:
1. `Content="{Binding PageStrings.RefreshLibrary}"` in AXAML.
2. `StatusMessage = UiLocalization.Get("Feature.RefreshFailed");` in production code.

## Definition Of Done

A change is not complete unless all of the following are true:

1. `dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore` passes.
2. `dotnet build ChartHub.sln --configuration Release --no-restore` passes with zero warnings and zero errors.
3. Relevant tests are added or updated for behavior changes.
4. `dotnet test ChartHub.Tests/ChartHub.Tests.csproj --configuration Release --no-build` passes.
5. No new analyzer suppressions are introduced unless explicitly requested.
6. No banned APIs are introduced.
7. Public behavior changes are described clearly in the final summary.
8. No new hardcoded user-facing strings are introduced in production code; touched files were reviewed and nearby literals were migrated or explicitly justified.


## Platform-Specific Code Rules

- All Android-specific code and usings must be guarded with `#if ANDROID`.

## Required Agent Behavior

When making code changes, agents must:

1. Prefer small, reviewable diffs.
2. Explain why a change is correct, not only that it compiles.
3. Call out tradeoffs when choosing between alternatives.
4. Preserve existing architecture unless a structural change is necessary.
5. Avoid adding new dependencies unless justified.

## Suppressions Policy

Do not add:

1. `#pragma warning disable`
2. `SuppressMessage`
3. analyzer severity reductions

Unless all of the following are true:

1. The warning is a verified false positive.
2. The justification is written in code near the suppression.
3. The final summary names the suppression explicitly.

## Testing Policy

If code behavior changes, add or update tests.
If no tests were added, explain why.
If a change affects parsing, persistence, async flow, state transitions, or API contracts, tests are expected by default.

## Final Response Requirements

Every agent final summary must include:

1. Files changed.
2. Why the change was made.
3. Validation performed.
4. Whether tests were added or updated.
5. Whether suppressions were added.
