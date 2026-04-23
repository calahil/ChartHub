# Oracle Parity Harness

This directory defines parity fixtures and committed checksums for conversion comparisons.

## Baseline

- Oracle tool: Onyx
- Pinned release tag: `20251011`
- Pinned commit: `ad8dc396ce90a3ace3a8d69164de5564f1904207`

## Repository Layout

- `fixtures.yaml`: fixture catalog and comparison policy per fixture.
- `checksums/`: committed checksum manifests generated from successful oracle runs.

## Local Artifacts (gitignored)

Local outputs must be written under `.parity-artifacts/`:

- `.parity-artifacts/onyx/` for oracle outputs
- `.parity-artifacts/charthub/` for local converter outputs

These folders are intentionally excluded from git. Only checksum manifests in `parity/checksums/` are committed.

When checksums are generated from Onyx output, oracle-internal artifacts are filtered out before manifest write.
Current exclusions include:

- `onyx-repack/` and `onyx-project/` tool-internal directories
- RB platform/runtime artifacts without ChartHub equivalents (for example `.milo_xbox`, `.png_xbox`)
- source package metadata files without direct ChartHub output equivalents (for example `songs.dta`)

Functional parity classification also treats source container audio (`.mogg`) as transformed media for comparison purposes.
This prevents byte/path comparisons between oracle source containers and ChartHub's normalized output layout.

Current validation status (local opt-in parity run):

- `rb3con-ready-to-start`: passing
- `rb3con-neighborhood-1`: passing under fixture policy
- `sng-biology`: passing under fixture policy
- `rb3con-arcade-fire-pack`: passing
- `sng-release`: passing under fixture policy

Latest verification snapshot:

- Oracle parity suite (`OracleParityComparisonTests`): green (5/5)
- Temporary MOGG candidate diagnostics removed after unblock; robust fallback logic retained
- Conversion routing: `.sng` route now converts end-to-end through the native SNG pipeline
- SNGPKG reader: magic/version + file-table extraction implemented with synthetic + real fixture tests
- SNG metadata extractor: parses embedded `song.ini` ([song] section) with filename/unknown fallbacks + focused unit coverage
- SNG MIDI extractor: `notes.mid` packages reuse RB→CH MIDI conversion with passthrough fallback for non-standard `notes.mid` payloads; `notes.chart` packages still fail explicitly pending chart import support
- SNG audio extractor: `.opus` / `.ogg` container audio entries now extract as-is (including multi-stem packages with `guitar.opus`, `drums.opus`, etc.)
- SNG album art extractor: container image entries now prefer `album.jpg` / `.jpeg` / `.png`, fall back to first supported image, and write normalized `album.*` output
- SNG install path: server `DownloadJobInstallService` now routes supported `.sng` artifacts through native conversion + standard rehome/install flow
- Supported SNG parity fixture + baseline: `sng-release` added to `fixtures.yaml` with committed Onyx checksums

Next active slice:

- Expand M3 fixture matrix coverage

## Opt-In Local Runs

Parity runs are opt-in. Set this environment variable to enable parity tests locally:

- `CH_PARITY_ENABLE_ORACLE=1`

Optional environment variables:

- `CH_PARITY_ONYX_BIN`: absolute path to the Onyx binary/executable.
- `CH_PARITY_ONYX_ARGS_TEMPLATE`: argument template for Onyx invocation. Must include `{input}` and `{output}`.
- `CH_PARITY_ARTIFACTS_ROOT`: override local artifact root (defaults to `.parity-artifacts`).
- `CH_PARITY_UPDATE_CHECKSUMS=1`: rewrite committed checksum entries from local oracle outputs.
- `CH_PARITY_ONYX_TIMEOUT_SECONDS`: timeout for oracle process execution (default `300`).
- `CH_PARITY_FORCE_REGEN=1`: force regeneration of local oracle artifacts.

## Expected Local Output Layout

Per-fixture outputs should be written to:

- `.parity-artifacts/onyx/<fixture-id>/`
- `.parity-artifacts/charthub/<fixture-id>/`

---

## Onyx Converter Port — Milestone Checklist

Track progress on the native re-implementation of Onyx's conversion pipeline in `ChartHub.Conversion`.

### M1 — RB3CON parity hardening (current)

Goal: ChartHub output must be functionally equivalent to Onyx for all committed RB3CON fixtures.

- [x] STFS/CON reader with fan-made hash-pointer fallback
- [x] DTA parser (S-expression, song metadata, channel map)
- [x] MOGG extractor — unencrypted (0x0A): OGG offset recovery, stem splitting, ffmpeg invocation
- [x] MOGG decryptor — encrypted (0x0B AES-CTR): key-based decryption before OGG extraction
- [x] RB3 MIDI → CH MIDI converter (track filtering)
- [x] song.ini generator
- [x] ConversionService wired end to end for `.con` / `.rb3con`
- [x] Oracle parity harness infrastructure (opt-in, env-driven, update-mode)
- [x] Committed Onyx baselines for `rb3con-ready-to-start` and `rb3con-neighborhood-1`
- [x] Parity comparison assertions upgraded from byte-only → full functional (all `comparison: functional` files compared by role, not just byte files)
- [x] At least one fixture with multi-song DTA (more than one song entry per CON)
- [x] Parity passing for all committed RB3CON fixtures (local opt-in run clean, no deltas)

### M2 — Supported SNG conversion pipeline

Goal: Standard SNGPKG files (fan-made, Encore-style) install cleanly through the server pipeline.

Pre-requisites: M1 complete.

- [x] SNG header detection (`SNGPKG` magic) in server intake
- [x] Encrypted/official SNG variant detection and explicit install failure routing (`EncryptedSng`)
- [x] SNG reader (parse SNGPKG container: file table, file entries, file data blocks)
- [x] SNG metadata extractor (song.ini embedded in container or derived fields)
- [x] SNG MIDI extractor + RB→CH MIDI conversion reuse
- [x] SNG audio extractor (OGG stem files from container)
- [x] SNG album art extractor
- [x] `ConversionService.ConvertAsync` branch wired for `.sng`
- [x] `DownloadJobInstallService.InstallSngAsync` (parallel to existing `InstallConAsync`)
- [x] Parity fixture(s) for supported SNG (e.g. fan-made Arcade Fire SNG added to `fixtures.yaml`)
- [x] Committed Onyx baselines for at least one SNG fixture
- [x] Parity passing for SNG fixtures

### M3 — Broadened fixture matrix and stricter production gate

Goal: Parity suite covers enough real-world diversity to catch regressions before they reach users.

Pre-requisites: M2 complete.

- [ ] RB3CON fixtures expanded to cover: multi-stem, no-album-art, non-ASCII metadata, crowd track
- [ ] SNG fixtures expanded to cover: single-stem, keys-only, no-video variant
- [x] `sng-biology` fixture policy documented (skip/xfail with reason; encrypted official not supported)
- [ ] All fixture comparisons using strict functional mode for chart and metadata roles
- [ ] CI integration: parity baseline staleness check (fail if manifest out of date with oracle binary pin)

### Out of scope (explicitly deferred)

- Biology-like encrypted official SNG conversion — classified as `EncryptedSng`, fails with explicit message
- LIVE/PIRS package support (non-CON STFS variants)
