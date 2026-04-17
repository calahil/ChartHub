# ADR 001 — BackupApi Schema: Single-Tenant Design

**Status:** Accepted (current) — superseded pending distributed mirror rework  
**Date:** 2026-04-17  
**Author:** Calahil Studios

---

## Context

ChartHub.BackupApi mirrors RhythmVerse song metadata into a local database for read access, search, image caching, and download redirection. The upstream RhythmVerse service is known to be unreliable.

The long-term goal is a **distributed mirror network**: each operator self-hosts a BackupApi instance, and all instances join a load-balanced pool at `mirror.calahilstudios.com`. This gives the community resilient, redundant access to RhythmVerse data without a single point of failure.

---

## Decision

The current schema is intentionally **single-tenant**: designed to mirror one upstream source into one local database, operated by one instance.

This was the correct starting point for:

- validating the sync and reconciliation model
- proving the client compatibility layer
- keeping deployment simple during early development

---

## Current Schema Characteristics (as-is)

| Table | Purpose |
|---|---|
| `Songs` | Mirrored song rows with soft-delete support and run-tracking (`ReconciliationRunId`) |
| `SyncRuns` | Reconciliation run metadata (start time, completion status, run ID) |

**Key properties:**

- One source only — no origin column per row
- Soft-delete is local — no propagation protocol
- No row versioning or conflict resolution
- Run tracking is per-instance, not cross-instance

---

## Known Limitations for Distributed Use

These are **not defects** in the current single-tenant context. They become blockers only when the distributed mirror architecture is implemented.

| Limitation | Distributed Impact |
|---|---|
| No per-row origin tracking | Cannot attribute which mirror a record came from or merge records across operators |
| Soft-delete is local | Cannot propagate deletes to other nodes; each instance decides independently |
| No row versioning | Cannot resolve conflicts when two mirrors diverge |
| Single-source schema | Cannot ingest from multiple upstream operators or sources |
| No instance identity columns | Load balancer cannot route stale-reads to fresher peers |

---

## Constraints for Agents Working on BackupApi

1. **Do not** add multi-source assumptions to the current schema.
2. **Do not** add cross-instance state tracking columns without a deliberate schema migration plan.
3. When implementing features or fixes, assume single-tenant operation unless a schema rework has explicitly been initiated.
4. If a change requires multi-instance awareness, stop and flag it — do not invent a design.
5. The `docs/self-hosting/backup-api.md` user-facing doc must be updated if public-facing behavior changes.

---

## Future Direction (not yet designed — do not implement speculatively)

When the distributed mirror rework is initiated, the schema and architecture will need:

- Per-row origin tracking (which operator/mirror contributed the row)
- Instance identity and registration with the coordinator
- A cross-instance propagation protocol for soft-deletes and updates
- Conflict resolution strategy (last-write-wins, coordinator-authoritative, or merge)
- Load balancer integration for the `mirror.calahilstudios.com` pool

A new ADR will be created when this work is planned.

---

## Consequences

- Current BackupApi deployments are fully functional personal mirrors.
- The schema rework is a prerequisite for the distributed pool vision.
- All agents must treat the current schema as intentionally temporary at the distributed layer — do not build on top of it as if it were stable for multi-instance use.
