# Onyx Parity Execution Plan

Last updated: 2026-04-25
Owner: ChartHub.Conversion workstream

## Goal
Align ChartHub conversion outputs with Onyx Clone Hero output behavior, anchored to:
- onyx-source/onyx/haskell/packages/onyx-lib/src/Onyx/Build/CloneHero.hs
- onyx-source/onyx/haskell/packages/onyx-lib/src/Onyx/Image/DXT.chs

## Execution Sequence

### Patch 1: song.ini key and field parity
Status: Completed

Scope:
- Rename key names to Onyx-compatible names:
  - diff_guitarghl -> diff_guitar_ghl (Completed)
  - diff_bassghl -> diff_bass_ghl (Completed)
- Keep current value behavior for now where source data is unavailable.
- Update tests to assert the corrected key names.

Acceptance:
- song.ini emits Onyx key names for GHL fields.
- Conversion tests pass.

### Patch 2: DTA metadata expansion for song.ini decisions
Status: Completed

Scope:
- Extend parsed DTA metadata for drum mode and optional song.ini fields.
- Wire fields needed for pro_drums/five_lane_drums/drum_fallback_blue, loading_phrase, tags.

Acceptance:
- New parser fields covered by unit tests.
- SongIniGenerator consumes these values.

### Patch 3: Audio stem parity (drum split + optional stem gating)
Status: Completed

Scope:
- Implement Onyx-aligned optional stems:
  - drums.ogg or drums_1..drums_4
  - guitar/rhythm/keys/vocals/crowd
- Keep silent stem suppression behavior.

Acceptance:
- Fixture-backed tests validate expected stem presence matrix.

### Patch 4: expert+.mid generation parity
Status: Completed

Scope:
- Emit expert+.mid when drums are present, preserving notes.mid.
- Reuse/add MIDI helper to generate 2x-kick compatible output.

Acceptance:
- Tests validate expert+.mid behavior.

### Patch 5: Timing/length alignment
Status: Completed

Scope:
- Ensure backing/stems are consistently duration-clamped.
- Keep song.ini song_length behavior aligned with chosen duration source.

Acceptance:
- Duration-focused regression tests pass.

### Patch 6: Parity harness hardening
Status: Completed

Scope:
- Add parity assertions for file presence and song.ini key family.
- Add explicit drum split fixture expectations.

Acceptance:
- Parity tests fail on regressions introduced by future changes.

### Patch 7: Validation and install verification
Status: Completed

Scope:
- Run full completion gates:
  - dotnet format ChartHub.sln --verify-no-changes --severity error --no-restore
  - dotnet build ChartHub.sln --configuration Release --no-restore
  - dotnet test ChartHub.Conversion.Tests/ChartHub.Conversion.Tests.csproj --configuration Release --no-build
- Re-run a real local install and inspect server logs/job DB timeline.

Acceptance:
- Build/test gates green. ✓
- Local install succeeds and output folder artifacts match expected shape. ✓

## Change Log
- 2026-04-25: Plan created and execution started with Patch 1.
- 2026-04-25: Patch 1 step completed: renamed GHL song.ini keys and updated conversion tests.
- 2026-04-25: Patch 2 started: added loading_phrase and cover-tag metadata through DTA parser -> song.ini generator -> tests.
- 2026-04-25: Patch 2 step completed: added pro_drums/five_lane_drums/drum_fallback_blue DTA overrides with song.ini regression coverage.
- 2026-04-25: Patch 3 started: added Onyx-aligned drum split stem normalization/parity logic and extractor unit coverage.
- 2026-04-25: Patch 3 completed: drum split alias normalization (kick→drums_1, snare→drums_2, cymbals/kit→drums_3, toms→drums_4), rank gating for split stems, combined/split mutual exclusion. 84 tests passing.
- 2026-04-25: Patch 4 completed: ExpertPlusMidiGenerator.cs mirrors Onyx expertWith2x (note 95→96 on PART DRUMS); WriteExpertPlusMidiAsync wired into ConversionService; gated on drum rank > 0. 88 tests passing.
- 2026-04-25: Patch 5 completed: WithSongLengthFromAudio now prefers DTA song_length (authoritative MIDI-derived value) over audio measurement; audio is fallback only when DTA carries 0. 91 tests passing.
- 2026-04-25: Patch 6 completed: ParityHardeningTests class added with fixture-guarded assertions for required output files, song.ini key family (Onyx GHL key names, no legacy keys), drum split/combined mutual exclusion, and expert+.mid structural validity. 96 tests passing.
- 2026-04-25: Patch 7 complete: all gates ✓, local install verified with Tool - Lateralus (BearzUnlimited). Output: notes.mid, expert+.mid, song.ogg, album.png, song.ini with DTA song_length=569119, pro_drums=True, Onyx GHL key names, full diff key family. All patches complete.
