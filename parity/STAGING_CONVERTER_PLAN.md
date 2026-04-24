# Staging-Ready Converter Plan Ledger

Last updated: 2026-04-23
Owner: Conversion library workstream

This file is the repository source of truth for staging readiness against the modified M1-M7 plan.
Status values:
- Complete: implemented and backed by committed tests/fixtures.
- Partial: meaningful implementation exists, but at least one acceptance requirement is still missing.
- Blocked: cannot proceed without an external dependency or unresolved decision.
- Not started: no implemented path yet.

## Locked Scope Requirements

- RB3CON support must include MOGG versions 0x0A, 0x0B, and 0x0D.
- RB3CON no_album_art fixture coverage is explicitly out-of-scope for final release.
- Unencrypted SNG support must cover notes.mid and notes.chart with canonical normalization expectations.
- Parity gate is strict: all committed fixtures pass with no known-unsupported skips.
- Every required edge case needs committed fixture-backed proof.
- Post-processing must fully handle chart-based installs.
- External decode tools are not a runtime requirement; audio extraction must be internal and deterministic.
- Performance gates must be measurable.
- Converter API contract must be frozen for staging.

## Milestone Audit

### M1. Contract Lock

Status: Partial

What exists:
- Converter result contract includes structured conversion statuses:
  - ChartHub.Conversion/Models/ConversionModels.cs
- Server API contract surfaces conversion statuses:
  - ChartHub.Server/Contracts/DownloadJobContracts.cs
- OpenAPI examples include conversion status schema examples with tests:
  - ChartHub.Server/OpenApi/OpenApiTransformers.cs
  - ChartHub.Server.Tests/OpenApiTransformersTests.cs

Remaining gaps:
- No explicit versioned contract freeze artifact (for example, contract snapshot + break detector policy).
- Error taxonomy is not fully enumerated/locked as a staging contract.
- Acceptance matrix for all converter inputs/failures is not captured as a formal locked document.

### M2. RB3CON Complete Audio Support

Status: Partial

What exists:
- MOGG decrypt/extract supports 0x0A, 0x0B, and 0x0D:
  - ChartHub.Conversion/Audio/MoggExtractor.cs
- CON conversion is wired end-to-end:
  - ChartHub.Conversion/ConversionService.cs
- Fixture corpus audit command confirms all 3 versions exist in available fixtures:
  - Run: `./merges/audit-conversion-fixtures.sh merges/`
- Malformed block-chain recovery paths exist in conversion logic and tests:
  - ChartHub.Conversion/ConversionService.cs
  - ChartHub.Conversion.Tests/ConversionTests.cs

Remaining gaps:
- Internal per-instrument stem splitting parity is not complete yet in the native path.

### M3. SNG Complete Chart Support

Status: Partial

What exists:
- SNG pipeline is wired through converter and server install flow:
  - ChartHub.Conversion/ConversionService.cs
  - ChartHub.Server/Services/DownloadJobInstallService.cs
- notes.mid path converts RB MIDI for standard payloads and preserves non-standard payloads.
- notes.chart path extracts and installs directly:
  - ChartHub.Conversion/Sng/SngMidiExtractor.cs
- Fixture set includes both notes.mid and notes.chart SNGs:
  - parity/fixtures.yaml
  - Verified via `./merges/audit-conversion-fixtures.sh merges/`

Remaining gaps:
- Canonical normalization equivalence is not implemented/proven between notes.mid and notes.chart for same-song pairs.
- No committed parity/equivalence fixture asserting identical canonical semantics for paired notes.mid vs notes.chart sources.

### M4. Post-Processing Parity Hardening

Status: Partial

What exists:
- Post-processing drum merge path exists for installed songs.

Remaining gaps:
- Current implementation assumes notes.mid at install location:
  - ChartHub.Server/Services/PostProcessingService.cs
- No explicit chart-origin merge strategy implemented for notes.chart-only installs.
- No fixture-backed post-processing parity tests across both chart origins.

### M5. Fixture Matrix Completion

Status: Partial

What exists:
- Committed fixture manifest and checksum baseline process:
  - parity/fixtures.yaml
  - parity/checksums/manifest.yaml
- Oracle pin staleness guard test is in place:
  - ChartHub.Conversion.Tests/Parity/OracleParityHarnessTests.cs

Remaining gaps:
- Parity comparison currently contains known-unsupported skip behavior:
  - ChartHub.Conversion.Tests/Parity/OracleParityComparisonTests.cs
- Strict release gate requirement (no known-unsupported skips) is not satisfied.
- Edge-case matrix is not fully closed with committed parity fixtures for all required tags that remain in release scope.

### M6. Non-Functional Staging Gates

Status: Not started

Missing:
- No committed measurable SLO definitions for conversion latency/throughput/memory ceiling.
- No repeatability gate proving same input -> same canonical output as a staging criterion.
- No committed low-resource profile gate.
- No explicit resilience gate suite for rollback and deterministic partial-failure behavior.

### M7. Staging Exit Criteria

Status: Not ready

Not yet satisfied:
- 100% committed fixture pass with zero known-unsupported skips.
- Zero Sev1/Sev2 converter defect ledger and stabilization-cycle contract freeze proof.
- Staging soak evidence with deterministic outcomes.

## Current Snapshot

Implemented recently:
- Conversion warning/status pipeline for degraded success states (audio-incomplete) now flows through conversion, server persistence, API responses, OpenAPI examples, and client UI surfacing.

Critical blockers to staging-ready claim:
- Remove known-unsupported parity skips and close unsupported fixture policy gap.
- Complete internal per-instrument stem splitting parity.
- Complete canonical notes.mid/notes.chart equivalence normalization proof.
- Implement chart-origin-aware post-processing merge strategy.
- Add measurable non-functional staging gates.

## Next Slice Order

1. Remove known-unsupported skip policy from parity comparison and align fixture set to strict gate.
2. Implement and test internal per-instrument stem splitting parity.
3. Add paired same-song notes.mid vs notes.chart canonical equivalence fixtures and assertions.
4. Implement chart-aware post-processing merge behavior with fixture-backed tests.
5. Define and enforce SLO, repeatability, resilience, and soak gates.

## Update Discipline

For every conversion-library slice:
- Update this ledger in the same change.
- Record evidence paths (code + tests + fixture IDs).
- Do not mark a milestone complete unless all acceptance requirements in that milestone are met.
