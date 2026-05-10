# Phase 1: Repository and Runtime Foundation - Context

**Gathered:** 2026-05-10
**Status:** Ready for planning
**Source:** gsd-discuss-phase auto decisions plus PRD Express Path

<domain>
## Phase Boundary

Phase 1 delivers the buildable Windows-first Engram skeleton and the first local memory spine. It must create a .NET solution layout, a `.engram` workspace initializer, a typed raw event contract, an append-only raw event writer, deterministic dedupe hashing, and a replay/import command.

Out of scope for Phase 1:

- Passive file, clipboard, active-window, or screenshot capture.
- OCR provider implementation.
- Markdown wiki generation.
- Search, tray UI polish, identity discovery, drift alerts, cloud model routing, Google Workspace, research automation, computer-use automation, encryption, and sync.
</domain>

<decisions>
## Implementation Decisions

### Product Boundary

- **D-001:** Phase 1 combines repository/runtime foundation with the first append-only raw store slice so the first implementation produces a working local memory spine.
- **D-002:** Full PRD scope remains in the roadmap, but Phase 1 only implements local foundation and raw event ledger behavior.

### Stack

- **D-003:** Use .NET/C# as the primary implementation stack for Windows service, tray/search shell, shared libraries, CLI/dev tooling, and tests.
- **D-004:** Use replaceable provider interfaces for later OCR, local model, cloud model, browser automation, and workspace connectors; Phase 1 only defines seams needed by local storage and commands.

### Local Store

- **D-005:** The local workspace root is `.engram` with `raw`, `wiki`, `runs`, `config`, `logs`, and `archives` directories.
- **D-006:** Raw event payload files are immutable and append-only under `.engram/raw/YYYY-MM-DD/[event_id].json`.
- **D-007:** Duplicate detection uses deterministic content hashing and must not rewrite existing raw payload files.
- **D-008:** Replay/import must enumerate persisted raw events and expose them to later processing without requiring passive capture sources.

### Privacy And Operations

- **D-009:** Phase 1 must run tests locally without cloud credentials.
- **D-010:** Sensitive capture sources stay disabled because they are out of scope until later consent and ingestion phases.
</decisions>

<canonical_refs>
## Canonical References

Downstream agents MUST read these before planning or implementing.

### Product Source

- `Artifacts/Product Requirements Document_Engram Full Specification.md` - Product requirements, raw folder contract, ingestion vision, tiering, UX, and non-functional requirements.
- `Artifacts/Engram Implementation Plan.md` - Phased implementation strategy, architecture boundaries, data contracts, and test plan.

### GSD Source

- `.planning/PROJECT.md` - Project direction and non-negotiables.
- `.planning/REQUIREMENTS.md` - Requirement IDs and traceability.
- `.planning/ROADMAP.md` - Phase scope and success criteria.
</canonical_refs>

<code_context>
## Code Context

No application code exists yet. The executor should create the first .NET solution and tests from scratch, guided by the stack and phase boundary above.

Existing repo state:

- `Artifacts/Product Requirements Document_Engram Full Specification.md`
- `Artifacts/Engram Implementation Plan.md`
- `.planning/PROJECT.md`
- `.planning/REQUIREMENTS.md`
- `.planning/ROADMAP.md`
</code_context>

<specifics>
## Specific Ideas

- Use solution-level names aligned to the implementation plan: `Engram.Service`, `Engram.Tray`, `Engram.Search`, `Engram.Store`, `Engram.Connectors`, `Engram.Memory`, and `Engram.Agents`.
- Phase 1 should create only the projects required for foundation and raw storage, while leaving later subsystem implementations as placeholders or future projects only if needed for compile-time boundaries.
- Raw event schema fields: `event_id`, `event_type`, `captured_at`, `source`, `source_uri`, `active_window`, `text`, `metadata`, `privacy_class`, `hash`, and `processing_status`.
- Replay/import is a development command for future processing pipelines, not a passive ingestion source.
</specifics>

<deferred>
## Deferred Ideas

- Passive screen, clipboard, file, and app-focus capture - Phase 3.
- Markdown wiki metabolizer and `index.md` generation - Phase 4.
- Tray/search user surfaces - Phase 5.
- Discovery SOP and intervention policy - Phase 6.
- Drift alerts - Phase 7.
- Cloud model routing and managed credit pooling - Phase 8.
- GWS ingestion - Phase 9.
- Agentic research - Phase 10.
- Computer-use automation - Phase 11.
- Encryption, sync, installer, and production hardening - Phase 12.
</deferred>

---

*Phase: 01-repository-and-runtime-foundation*
*Context gathered: 2026-05-10 via auto discussion and PRD express path*
