# Copilot Agent Instructions

This file defines how coding agents must behave when contributing to ChartHub.

The authoritative policy document is `AGENTS.md`.  
If there is any conflict, follow `AGENTS.md`.

---

# Project Purpose

ChartHub exists to:

> Install and manage song charts from official sources and convert them reliably for Clone Hero.

Agents must optimize for:
- correctness of chart data
- long-term maintainability
- predictable behavior across sources

---

# Operating Mode

Agents must operate in **enforcement mindset**:

- Do not take shortcuts to make code pass.
- Do not guess when behavior is unclear.
- Prefer rejecting a change over introducing risk.

---

# Architecture (MVVM - REQUIRED)

This project uses strict MVVM.

## Allowed Responsibilities

### View
- UI only
- No business logic
- No direct data access

### ViewModel
- State + orchestration
- Calls services
- No file system, HTTP, or parsing logic directly

### Model
- Pure data structures
- No side effects

### Services / Infrastructure
- All IO (filesystem, HTTP, parsing)
- External integrations

---

## Hard Rules

Agents MUST NOT:

1. Put business logic in Views
2. Put IO logic in ViewModels
3. Access services directly from Views
4. Bypass ViewModels for data flow
5. Mix parsing logic into UI layers

---

# Data Flow Rules

All data must flow:

View → ViewModel → Service → ViewModel → View

Never:
- View → Service
- View → Model mutation
- Service → View

---

# Async and IO Rules

1. Never block on async (`.Result`, `.Wait()` are banned)
2. Avoid `async void` (except UI event handlers)
3. All IO must be async where possible
4. Do not fake async with `Task.Run` unless justified

---

# Error Handling

Agents must:

- Handle failure paths explicitly
- Avoid silent failures
- Avoid catch-all `catch (Exception)` unless justified
- Provide meaningful error context

---

# Null and Data Safety

- Do not assume external data is valid
- Validate API responses
- Avoid null-forgiving operator (`!`) unless proven safe

---

# Dependencies

Agents must:

- Avoid adding new dependencies unless necessary
- Justify every new dependency in the final summary
- Prefer existing project patterns

---

# Change Strategy

When implementing changes:

1. Start from existing patterns in the repo
2. Prefer consistency over novelty
3. Keep diffs small and focused
4. Do not refactor unrelated code

---

# Tests

Agents must:

- Add tests when behavior changes
- Cover:
  - parsing
  - transformations
  - state transitions

If tests are not added, explain why.

---

# When Uncertain

Agents must:

- State uncertainty explicitly
- Avoid inventing behavior
- Ask for clarification (via comments or summary)

---

# Anti-Patterns (Explicitly Forbidden)

- "Hacky but works" fixes
- Bypassing architecture layers
- Suppressing warnings to pass CI
- Hardcoding values from external sources
- Duplicating logic instead of extracting it

---

# Definition of Completion

A change is complete only if:

- It follows MVVM boundaries
- It respects all rules above
- It passes all CI checks defined in `AGENTS.md`
- It includes a clear, structured final summary