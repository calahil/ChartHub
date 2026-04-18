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
9. If the change falls into a docs-gated category (see Docs Alignment Policy), the relevant docs page was updated or the omission was explicitly justified in the final summary.


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

### Multi-Project Test Gate

Agents must run the test suite(s) for every project they modify:

| Modified project | Required test suite |
|---|---|
| `ChartHub/` | `dotnet test ChartHub.Tests/ChartHub.Tests.csproj --configuration Release --no-build` |
| `ChartHub.Server/` | `dotnet test ChartHub.Server.Tests/ChartHub.Server.Tests.csproj --configuration Release --no-build` |
| `ChartHub.BackupApi/` | `dotnet test ChartHub.BackupApi.Tests/ChartHub.BackupApi.Tests.csproj --configuration Release --no-build` |
| Multiple projects modified | Run all relevant suites above |

## Final Response Requirements

Every agent final summary must include:

1. Files changed.
2. Why the change was made.
3. Validation performed.
4. Whether tests were added or updated.
5. Whether suppressions were added.

---

## API Contract Policy

ChartHub.Server exposes an HTTP/WebSocket API consumed by the Desktop client, the Android client, and ChartHub.Hud.

If any client-facing endpoint signature, route, request body, response shape, or authentication model changes:

1. Update `docs/developer/api-reference.md` in the same change.
2. Call out the breaking change explicitly in the final summary.
3. Breaking changes to auth, download, library, input, or volume endpoints require an explicit justification.

The HUD status endpoint (`/api/v1/hud/status`) is internal and loopback-only. Changes to it still require updating `docs/developer/hud.md` if the behavior visible to the Hud subprocess changes.

---

## Docs Alignment Policy

Docs must stay aligned with code. For the following change categories, updating the relevant doc page is a hard gate — equivalent to a test or build gate.

| Change category | Required doc update |
|---|---|
| ChartHub.Server auth flow or JWT token issuance changes | `docs/user-guide/connect-to-server.md` |
| ChartHub.Server client-facing endpoint added, changed, or removed | `docs/developer/api-reference.md` |
| Install requirements change (dependencies, paths, platform) | Relevant `docs/user-guide/install-*.md` |
| Self-hosting config, environment variables, or setup steps change | `docs/self-hosting/server-setup.md` or `docs/self-hosting/backup-api.md` |
| Song source behavior or catalog tracking changes | `docs/user-guide/song-sources.md` |

If a doc update is not included, the omission must be explicitly justified in the final summary.

---

## Release Process

Releases are created by pushing a `vMAJOR.MINOR.PATCH` tag (for example `v1.2.0`).

1. Agents may create release tags only when **explicitly instructed** to do so.
2. Tag format must follow `vMAJOR.MINOR.PATCH`. The only allowed suffix is `-rc.N` for release candidates.
3. Pushing a version tag triggers: the release CI workflow, docs deployment via `mike`, and changelog generation via `git-cliff`.
4. Agents must not push version tags speculatively or as a side effect of unrelated changes.
5. Agents must not delete or move existing release tags.

---

## CI Secrets Policy

When modifying a CI workflow step that depends on a secret, agents must:

1. Identify every GitHub environment the job runs under (check the `environment:` key on the job).
2. Verify the secret is present in `scripts/sync-github-secrets.sh` for **each** of those environments:
   - Secrets needed by `dev`, `staging`, `production` jobs → must be in `COMMON_SECRETS`.
   - Secrets needed by `server-dev`, `server-staging`, `server-production` jobs → must be in `SERVER_SECRETS`.
   - Secrets shared by all environments → add to **both** `COMMON_SECRETS` and `SERVER_SECRETS`.
3. Do not rely on `REPO_SECRETS` as a substitute for environment-scoped secrets in environment-gated jobs. GitHub does not guarantee repo-level secrets are available in environment-scoped jobs.
4. The sync script update and the workflow change must be in the same commit.
5. After updating the sync script, instruct the user to run `scripts/sync-github-secrets.sh` to push the new secrets to GitHub.

---

## Security Policy

Agents must follow these rules on all production code changes:

1. **Never commit secrets or credentials.** Use Infisical via `scripts/sync-github-secrets.sh`. No API keys, tokens, passwords, or signing keys in source.
2. **Validate all external inputs** before use: API responses from third-party sources, user-supplied server URLs, file paths, and file contents.
3. **Use EF Core with parameterized queries only.** String-concatenated SQL is forbidden.
4. **Auth tokens must not appear in logs or error messages** surfaced to clients. Google ID tokens and server JWTs must not be logged at any level.
5. **HTTP clients must not disable TLS certificate validation.** `ServerCertificateCustomValidationCallback` that always returns `true` is banned.
6. **No hardcoded default credentials, signing keys, or passwords** in any project. Configuration must come from environment variables or secrets management.
