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

## Opt-In Local Runs

Parity runs are opt-in. Set this environment variable to enable parity tests locally:

- `CH_PARITY_ENABLE_ORACLE=1`

Optional environment variables:

- `CH_PARITY_ONYX_BIN`: absolute path to the Onyx binary/executable.
- `CH_PARITY_ARTIFACTS_ROOT`: override local artifact root (defaults to `.parity-artifacts`).
