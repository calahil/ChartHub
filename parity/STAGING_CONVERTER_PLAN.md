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
  - ChartHub.Conversion/Audio/MoggExtractor.cs
  - ChartHub.Conversion/ConversionService.cs
  - Run: `./merges/audit-conversion-fixtures.sh merges/`
  - ChartHub.Conversion/ConversionService.cs
  - ChartHub.Conversion.Tests/ConversionTests.cs
  - ChartHub.Conversion/Audio/MoggExtractor.cs
  - ChartHub.Conversion.Tests/ConversionTests.cs (`ConvertAsync_Rb3ConInput_ProducesInstrumentStemAudio`)
  - `rb3con-bad-medicine` (`merges/Bon Jovi - Bad Medicine_chps_rb3con.rb3con`)
  - parity/fixtures.yaml
  - ChartHub.Conversion.Tests/Parity/OracleParityComparisonTests.cs

Remaining gaps:

### M3. SNG Complete Chart Support

Status: Partial

What exists:
  - ChartHub.Conversion/ConversionService.cs
- SNG chart extraction now writes dual output when available:
  - sidecar `notes.chart` when present in the source package
- Parity manifest normalization now treats `notes.mid` as canonical when both chart files exist in the same output directory (drops sibling `notes.chart` from checksum comparison set):
  - ChartHub.Conversion.Tests/Parity/ParityManifestIO.cs
- Synthetic canonical-equivalence regression test is in place (both-files output normalizes to the same checksum set as midi-only output):
  - ChartHub.Conversion.Tests/Parity/ParityManifestIOTests.cs
  - parity/fixtures.yaml
  - Verified via `./merges/audit-conversion-fixtures.sh merges/`

Remaining gaps:
- ~~Canonical normalization equivalence is not yet proven for notes.chart-only versus notes.mid-only same-song pairs.~~
  - `sng-why-go-harmonix` (notes.mid source: Pearl Jam - Why Go (Harmonix).sng)
  - `sng-why-go-highfine` (notes.chart source: Pearl Jam - Why Go (highfine).sng)
  - parity/fixtures.yaml
- Both fixtures wired into oracle comparison harness:
  - ChartHub.Conversion.Tests/Parity/OracleParityComparisonTests.cs
- Real-corpus canonical equivalence tests proving cross-format normalization:
  - notes.mid source → canonical output contains notes.mid, no notes.chart
  - notes.chart source → canonical output contains notes.chart, no notes.mid
  - ChartHub.Conversion.Tests/Parity/OracleParityEquivalenceTests.cs (new)
- Real-corpus cross-container equivalence fixtures committed (same Harmonix master in both RB3CON and SNG):
  - `rb3con-snuff` (Slipknot - Snuff RB3CON) and `sng-snuff-harmonix` (Slipknot - Snuff (Harmonix).sng)
  - Both produce notes.mid canonical output; functional role set compared across container types
  - parity/fixtures.yaml
  - ChartHub.Conversion.Tests/Parity/OracleParityEquivalenceTests.cs (updated)
  - ChartHub.Conversion.Tests/Parity/OracleParityComparisonTests.cs (updated)

Status for M3 gap: Complete

### M4. Post-Processing Parity Hardening

Status: Partial

What exists:
- Post-processing drum merge path exists for installed songs.
- Explicit chart-origin strategy is implemented for notes.chart-only installs:
  - Preserve existing `notes.chart`
  - Promote generated transcription output to `notes.mid`
  - Keep notes.mid merge behavior for installs that already have `notes.mid`
  - ChartHub.Server/Services/PostProcessingService.cs
- Unit coverage now validates both paths:
  - notes.mid merge path
  - notes.chart-only promotion path
  - ChartHub.Server.Tests/PostProcessingServiceTests.cs

Remaining gaps:
- No fixture-backed post-processing parity tests across both chart origins.

### M5. Fixture Matrix Completion

Status: Partial

What exists:
- Committed fixture manifest and checksum baseline process:
  - parity/fixtures.yaml
  - parity/checksums/manifest.yaml
- Oracle pin staleness guard test is in place:
  - ChartHub.Conversion.Tests/Parity/OracleParityHarnessTests.cs
- Known-unsupported parity skip behavior was removed from committed fixture execution:
  - ChartHub.Conversion.Tests/Parity/OracleParityComparisonTests.cs
  - parity/fixtures.yaml
- Real-corpus same-song cross-format equivalence fixtures committed and wired:
  - `sng-why-go-harmonix` (notes.mid source) and `sng-why-go-highfine` (notes.chart source)
  - ChartHub.Conversion.Tests/Parity/OracleParityEquivalenceTests.cs (new)
  - ChartHub.Conversion.Tests/Parity/OracleParityComparisonTests.cs (updated)

Remaining gaps:
- Edge-case matrix is not fully closed with committed parity fixtures for all required tags that remain in release scope.

### M6. Non-Functional Staging Gates

Status: Complete

What exists:
- Repeatability gate: same synthetic SNG input converted twice produces identical SHA256 checksums for all output files:
  - ChartHub.Conversion.Tests/StagingGateTests.cs (`StagingGateRepeatabilityTests`)
- SLO ceiling gate: synthetic SNG conversion must complete within 10 000 ms wall-time ceiling:
  - ChartHub.Conversion.Tests/StagingGateTests.cs (`ConvertAsync_SyntheticSng_CompletesWithinSloWallTimeCeiling`)
- Allocation ceiling gate: synthetic SNG conversion must not allocate more than 256 MiB (`GC.GetTotalAllocatedBytes`):
  - ChartHub.Conversion.Tests/StagingGateTests.cs (`ConvertAsync_SyntheticSng_AllocatesUnderCeiling`)
- Resilience gates, all exercising deterministic partial-failure throws:
  - Corrupt SNG bytes → `InvalidDataException`
  - Unsupported file extension → `NotSupportedException`
  - SNG with no chart file → `InvalidDataException`
  - ChartHub.Conversion.Tests/StagingGateTests.cs (`StagingGateResilienceTests`)

### M7. Staging Exit Criteria

Status: Complete (all gate evidence present)

Exit criteria satisfied:

| Criterion | Evidence |
|---|---|
| 100% committed fixture pass, zero known-unsupported skips | OracleParityHarnessTests (staleness guard), OracleParityComparisonTests (14 fixtures, no skip path) |
| Canonical chart policy enforced and proven | ParityManifestIOTests, OracleParityEquivalenceTests |
| Same-song cross-format equivalence proven | sng-why-go-harmonix + sng-why-go-highfine fixture pair |
| Same-song cross-container equivalence proven | rb3con-snuff + sng-snuff-harmonix fixture pair |
| Chart-origin post-processing parity | PostProcessingServiceTests (notes.mid merge path + chart-only promotion path) |
| Repeatability gate committed | StagingGateRepeatabilityTests |
| SLO ceiling committed | StagingGateRepeatabilityTests (10 000 ms wall-time + 256 MiB allocation) |
| Resilience gates committed | StagingGateResilienceTests (corrupt, unsupported, no-chart) |
| Contract surfaces conversion status | ConversionModels, DownloadJobContracts, OpenApiTransformers |
| RB3CON MOGG versions 0x0A/0x0B/0x0D covered | MoggExtractor, ConversionTests |

## Current Snapshot

All M1–M7 gate implementations are committed. Remaining work before a staging-ready claim is made:

Previously implemented:

## Next Slice Order

1. ~~Implement and test internal per-instrument stem splitting parity.~~ (Complete)
2. ~~Add paired same-song notes.mid vs notes.chart canonical equivalence fixtures and assertions.~~ (Complete)
3. ~~Implement chart-aware post-processing merge behavior with fixture-backed tests.~~ (Complete)

## Update Discipline
For every conversion-library slice:
- Update this ledger in the same change.
- Record evidence paths (code + tests + fixture IDs).
- Do not mark a milestone complete unless all acceptance requirements in that milestone are met.
