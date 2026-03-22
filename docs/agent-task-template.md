# Agent Task Template

Use this template when assigning implementation work to an agent in this repository.

## Prompt

AGENT TASK CONTEXT

Primary objective:
- <one concrete outcome>

Scope:
- In scope:
1. <item>
2. <item>
- Out of scope:
1. <item>
2. <item>

Mandatory rule sources (must be read and followed before edits):
1. AGENTS.md
2. .github/copilot-instructions.md
3. architecture.md

Architecture and boundary requirements (hard constraints):
1. Enforce strict MVVM boundaries exactly as documented in architecture.md.
2. No IO in ViewModels.
3. No business logic in Views.
4. No View/Control dependencies from Services or Configuration.
5. Keep data flow as: View -> ViewModel -> Service/Configuration -> ViewModel -> View.

Quality and safety constraints:
1. No hacky shortcuts.
2. No warning suppressions, pragma disables, analyzer downgrades, or guard-rail weakening.
3. Do not revert unrelated existing changes.
4. Keep diffs minimal and focused; avoid unrelated refactors.
5. Add or update tests when behavior changes.

Execution requirements:
1. First, inspect affected files and summarize planned edits in 3-7 bullets.
2. Then implement changes directly (do not stop at analysis).
3. After edits, run required validation commands and report concise results.
4. If any gate fails, fix root cause and rerun.
5. If blocked, state exact blocker and propose the smallest viable alternative.

Low-Context Mode (use when scope is uncertain):
1. Run a discovery pass first and return:
- Candidate files/components.
- Dependencies touched.
- Assumptions and unknowns.
- Proposed in-scope and out-of-scope lists.
2. Assign confidence to each scope item: high, medium, or low.
3. Do not implement until either:
- explicit user confirmation, or
- user pre-authorized autonomous continuation.
4. If scope changes mid-implementation, pause and report:
- what changed,
- why,
- impact on architecture/tests.
5. Preserve hard constraints regardless of uncertainty:
- strict MVVM,
- no IO in ViewModels,
- no suppressions,
- no unrelated refactors.

Required validation gates (must pass):
1. dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore
2. dotnet build ChartHub.sln --configuration Release --no-restore
3. dotnet test ChartHub.Tests/ChartHub.Tests.csproj --configuration Release --no-build

Definition of done:
1. Build passes with zero warnings and zero errors.
2. All tests pass.
3. Changes comply with AGENTS.md, .github/copilot-instructions.md, and architecture.md.
4. Final summary includes:
- Files changed.
- Why each change was made.
- Validation results.
- Tests added or updated (or why not).
- Whether suppressions were added (expected: none).

## Filled Example

AGENT TASK CONTEXT

Primary objective:
- Add a new settings field for sync host timeout and wire it through config + UI.

Scope:
- In scope:
1. Runtime config model and validation.
2. Settings UI binding for the new field.
3. Tests for validation and settings persistence.
- Out of scope:
1. Refactoring unrelated sync services.
2. UX redesign beyond adding the field.

Mandatory rule sources (must be read and followed before edits):
1. AGENTS.md
2. .github/copilot-instructions.md
3. architecture.md

Architecture and boundary requirements (hard constraints):
1. Strict MVVM only.
2. No IO in ViewModels.
3. No business logic in Views.
4. No View/Control references in Services/Configuration.
5. Preserve View -> ViewModel -> Service/Configuration -> ViewModel -> View.

Quality and safety constraints:
1. No hacky fixes.
2. No suppressions or analyzer rule weakening.
3. Keep diffs small and targeted.
4. Do not touch unrelated files.
5. Add or update tests for any behavior changes.

Execution requirements:
1. Read relevant files and provide a short plan.
2. Implement changes.
3. Run all required gates.
4. Fix failures and rerun.
5. Report concise final summary with evidence.

Low-Context Mode (when the request is ambiguous):
1. Discovery first, then propose scope with confidence ratings.
2. Confirm scope before implementation unless autonomy is explicitly granted.
3. Report scope deltas immediately if new findings appear.
4. Keep strict MVVM and no-IO-in-ViewModel constraints even during uncertainty.

Required validation gates (must pass):
1. dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore
2. dotnet build ChartHub.sln --configuration Release --no-restore
3. dotnet test ChartHub.Tests/ChartHub.Tests.csproj --configuration Release --no-build

Definition of done:
1. Zero-warning Release build.
2. Tests passing.
3. Architecture and rule compliance.
4. Clear final summary with changed files and validation outputs.
