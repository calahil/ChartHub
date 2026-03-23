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
6. Ensure the minimum coverage of 70% is maintained before pull requests

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
