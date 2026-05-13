# Phase 2: Immutable Raw Event Store - Context

**Gathered:** 2026-05-13
**Status:** Decisions locked
**Source:** Discuss-phase with user approval

## Phase Boundary

Phase 2 hardens the raw event ledger for robustness. It adds atomic writes, replay filtering, integrity verification, and sidecar-based processing status tracking.

Out of scope:
- Passive file/clipboard/OCR capture (Phase 3)
- Markdown wiki generation (Phase 4)
- Search, tray UI, identity, drift, cloud, GWS, research, automation, encryption

## Implementation Decisions

### D-011: Atomicity via Temp + Rename
- Write event JSON to a .tmp file first
- Then File.Move (atomic rename) to final .json path
- If crash occurs during write, .tmp file is orphaned but final .json is never corrupt
- Replay skips .tmp files

### D-012: Per-Event Processing Sidecar
- Raw event payload stays in event_id.json (immutable)
- Processing status tracked in event_id.meta.json (mutable sidecar)
- Sidecar fields: processing_status, last_processed_at, processing_error, retry_count
- Raw event JSON no longer contains processing_status field (moved to sidecar)

### D-013: Hash Verification on Read
- ReplayEnumerator verifies file integrity by recomputing hash
- If hash mismatch, event is flagged as corrupted (not silently skipped)
- Corrupted events reported separately from valid events

### D-014: ReplayQuery Object for Filtering
- New ReplayQuery class with filter properties
- Filter by: FromDate, ToDate, Source, ProcessingStatus
- All filters optional, null means "match all"
- Deterministic ordering preserved

### D-015: CLI Filter Flags
- engram replay --from YYYY-MM-DD --to YYYY-MM-DD --source <name> --status <status>
- All flags optional

## Canonical References
- Artifacts/Product Requirements Document_Engram Full Specification.md
- Artifacts/Engram Implementation Plan.md
- .planning/PROJECT.md
- .planning/REQUIREMENTS.md
- .planning/ROADMAP.md

## Code Context (from Phase 1)
- src/Engram.Store/RawEventWriter.cs — current writer (non-atomic)
- src/Engram.Store/ReplayEnumerator.cs — current replay (no filtering)
- src/Engram.Store/RawEvent.cs — model with processing_status field
- src/Engram.Store/ContentHasher.cs — SHA-256 hasher

*Phase: 02-immutable-raw-event-store*
*Context gathered: 2026-05-13*
