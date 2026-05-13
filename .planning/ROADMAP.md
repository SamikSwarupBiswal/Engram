# Roadmap: Engram

## Overview

Engram will be built as a Windows-first local semantic operating layer before expanding into cloud reasoning, workspace ingestion, research automation, computer-use automation, and encrypted sync. The roadmap covers the full PRD, while Phase 1 intentionally combines runtime foundation with a minimal append-only raw store so the first implementation has an executable local memory spine.

## Quality Gate

**Every phase must pass the quality gate defined in `docs/QUALITY-GATE-POLICY.md` before it is considered deliverable.** The gate includes: unit test coverage, integration validation, performance budgets (where applicable), security checks, build verification, and manual smoke testing. No phase is marked "Done" until the gate passes.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work.
- Decimal phases (2.1, 2.2): Urgent insertions, if needed later.

**Tier Mapping:**
- Phases 1-7: FREE TIER (local-only, $0/mo, no cloud required)
- Phases 8-12: PRO TIER (cloud-enhanced, $20-$30/mo, managed credit pooling)

- [ ] **Phase 1: Repository and Runtime Foundation** [FREE] - .NET solution skeleton plus `.engram` initializer and append-only raw event ledger.
- [ ] **Phase 2: Immutable Raw Event Store** [FREE] - Hardens raw event storage, replay, idempotency, and event processing boundaries.
- [ ] **Phase 3: Local Ingestion MVP** [FREE] - Adds low-risk local capture sources with consent, exclusions, and provider interfaces.
- [ ] **Phase 4: Markdown Wiki Memory** [FREE] - Metabolizes raw events into source-linked Markdown wiki nodes and an index.
- [ ] **Phase 5: Local Search and Briefs** [FREE] - Adds local semantic search, tray status, and morning/evening briefs.
- [ ] **Phase 6: Identity Hardening** [FREE] - Adds Discovery SOP and identity-policy gating for interventions.
- [ ] **Phase 7: Salience and Drift Engine** [FREE] - Adds salience decay, archival, and contradiction alerts.
- [ ] **Phase 8: Cloud Reasoning and Tier Routing** [PRO] - Adds audited Pro reasoning, local filtering, and cost controls.
- [ ] **Phase 9: Google Workspace Metadata Ingestion** [PRO] - Adds explicit Gmail, Calendar, and Drive metadata connectors.
- [ ] **Phase 10: Agentic Research Workflow** [PRO] - Adds browser-backed research runs with cited wiki summaries.
- [ ] **Phase 11: Computer-Use Automation** [PRO] - Adds controlled Windows automation with previews, approvals, and logs.
- [ ] **Phase 12: Encryption, Sync, and Production Hardening** [PRO] - Adds encryption, sync, export/delete, installer, and performance gates.

## Phase Details

### Phase 1: Repository and Runtime Foundation
**Goal:** As an Engram developer, I want a Windows-first .NET project skeleton with a working `.engram` initializer and append-only raw event store, so that all later ingestion and memory phases have a real local foundation.
**Mode:** mvp
**Depends on:** Nothing (first phase)
**Requirements:** [REQ-001, REQ-002, REQ-003, REQ-004, REQ-005, REQ-006, NFR-002, NFR-003]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. A fresh clone can build and test a .NET solution without cloud credentials.
  2. A command can initialize `.engram` with required local folders and config.
  3. Raw events are written as append-only JSON under `.engram/raw/YYYY-MM-DD/[event_id].json`.
  4. Duplicate raw events are detected by deterministic content hash without rewriting existing files.
  5. A replay/import command can enumerate raw events for later processing.
**Plans:** 3 plans

Plans:
- [x] 01-01: Create .NET solution skeleton, shared project layout, test harness, and developer commands.
- [x] 01-02: Implement `.engram` workspace initializer and configuration model.
- [x] 01-03: Implement raw event schema, append-only writer, dedupe hash, and replay command.

### Phase 2: Immutable Raw Event Store
**Goal:** Make the raw event ledger robust enough to support future passive ingestion and reprocessing.
**Depends on:** Phase 1
**Requirements:** [REQ-004, REQ-005, REQ-006, NFR-002]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Raw event writes are atomic and recover from partial failures.
  2. Replay supports filtering by date, source, and processing status.
  3. Processing status can be tracked without mutating original raw event payloads.
**Plans:** TBD

Plans:
- [x] 02-01: Harden raw ledger atomicity, replay filters, and processing sidecars.

### Phase 3: Local Ingestion MVP
**Goal:** Capture low-risk local signals with explicit consent and durable source attribution.
**Depends on:** Phase 2
**Requirements:** [REQ-007, REQ-008, REQ-009, NFR-004]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Users can enable or disable file, clipboard, and active-window capture independently.
  2. Excluded apps are never captured.
  3. Captured events persist into the raw ledger with source attribution.
**Plans:** TBD

Plans:
- [x] 03-01: Implement local file watcher and source attribution.
- [x] 03-02: Implement opt-in clipboard and active-window capture policies.
- [ ] 03-03: Add OCR provider interface and development fallback.

### Phase 4: Markdown Wiki Memory
**Goal:** Convert raw events into source-linked Markdown memory nodes and navigable wiki indexes.
**Depends on:** Phase 2
**Requirements:** [REQ-010, REQ-011, REQ-012]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Repeated topics update existing wiki nodes instead of creating duplicates.
  2. Every wiki fact links back to raw event evidence.
  3. `index.md` provides useful navigation through wiki links.
**Plans:** TBD

Plans:
- [x] 04-01: Define wiki node schema and front matter.
- [x] 04-02: Implement raw-to-wiki metabolizer and merge rules.
- [x] 04-03: Generate wiki index and backlinks.

### Phase 5: Local Search and Briefs
**Goal:** Make local Engram memory useful through search, status, and brief surfaces.
**Depends on:** Phase 4
**Requirements:** [REQ-013, REQ-014, REQ-015]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. User can query wiki memory locally from an Alt+Space surface.
  2. Briefs cite source wiki nodes and raw events.
  3. Tray UI exposes active context and capture pause/resume.
**Plans:** TBD

Plans:
- [x] 05-01: Implement local search index and query path.
- [x] 05-02: Implement briefs from promises, intentions, stale items, and changed facts.
- [x] 05-03: Implement tray status and pause/resume controls.

### Phase 6: Identity Hardening
**Goal:** Capture explicit user identity constraints and use them to gate proactive behavior.
**Depends on:** Phase 5
**Requirements:** [REQ-016, REQ-017]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Discovery SOP writes user-confirmed identity files.
  2. Users can edit identity constraints directly.
  3. Intervention policy can explain allowed and blocked notifications.
**Plans:** TBD

Plans:
- [x] 06-01: Implement Discovery SOP and identity files.
- [x] 06-02: Implement intervention policy evaluator.

### Phase 7: Salience and Drift Engine
**Goal:** Detect stale knowledge and contradictions between new events, wiki facts, and priorities.
**Depends on:** Phase 4, Phase 6
**Requirements:** [REQ-018, REQ-019]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Old untouched nodes decay and can be archived.
  2. Contradictory events produce source-linked drift alerts.
  3. Users can dismiss, accept, or convert drift alerts into wiki updates.
**Plans:** TBD

Plans:
- [x] 07-01: Implement salience scoring and archive movement.
- [x] 07-02: Implement drift detection and alert resolution.

### Phase 8: Cloud Reasoning and Tier Routing
**Goal:** Add audited Pro reasoning while preserving local-first privacy defaults.
**Depends on:** Phase 5, Phase 6
**Requirements:** [REQ-020, NFR-004]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Routine ingestion remains local by default.
  2. Every cloud call records reason, provider, payload summary, and cost.
  3. Private raw data is never sent without explicit policy approval.
**Plans:** TBD

Plans:
- [ ] 08-01: Implement model routing and local filtering.
- [ ] 08-02: Implement cloud audit log, budget controls, and clean cache boundary.

### Phase 9: Google Workspace Metadata Ingestion
**Goal:** Add explicit Gmail, Calendar, and Drive metadata connectors with revocable scopes.
**Depends on:** Phase 8
**Requirements:** [REQ-021, NFR-004]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Users can connect and disconnect GWS cleanly.
  2. Connector scopes are minimal and visible.
  3. GWS metadata creates source-linked raw events and wiki updates.
**Plans:** TBD

Plans:
- [ ] 09-01: Implement OAuth, scopes, and revocation.
- [ ] 09-02: Implement Gmail, Calendar, and Drive metadata event ingestion.

### Phase 10: Agentic Research Workflow
**Goal:** Run high-signal browser-backed research and write cited summaries into the wiki.
**Depends on:** Phase 5, Phase 8
**Requirements:** [REQ-022, NFR-002]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. A research prompt opens sources and writes a cited wiki summary.
  2. Failed runs can resume from persisted run state.
  3. User can inspect all visited sources.
**Plans:** TBD

Plans:
- [ ] 10-01: Implement research run model and Playwright adapter.
- [ ] 10-02: Implement source filtering, cited summaries, and run resume.

### Phase 11: Computer-Use Automation
**Goal:** Operate Windows under strict permissions, previews, approval gates, and logs.
**Depends on:** Phase 6, Phase 10
**Requirements:** [REQ-023, NFR-002, NFR-004]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Read-only automation works before write/action automation.
  2. Risky actions require user approval.
  3. Every automation action is logged with timestamp, target, and rationale.
**Plans:** TBD

Plans:
- [ ] 11-01: Implement automation permissions, previews, and approval gates.
- [ ] 11-02: Implement UI automation adapter and recovery logging.

### Phase 12: Encryption, Sync, and Production Hardening
**Goal:** Make Engram safe enough for real personal data and production installation.
**Depends on:** Phase 11
**Requirements:** [REQ-024, NFR-001, NFR-004]
**Canonical refs:**
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
**Success Criteria** (what must be TRUE):
  1. Local data is encrypted at rest.
  2. User can export and delete all Engram data.
  3. Encrypted sync does not expose plaintext wiki or raw data to the backend.
  4. Installer and performance checks are production-ready.
**Plans:** TBD

Plans:
- [ ] 12-01: Implement encryption, key management, export, and delete.
- [ ] 12-02: Implement encrypted sync and installer hardening.
- [ ] 12-03: Implement resource budget diagnostics and production gates.

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8 -> 9 -> 10 -> 11 -> 12.

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Repository and Runtime Foundation | 3/3 | **Complete** | 2026-05-13 |
| 2. Immutable Raw Event Store | 1/1 | **Complete** | 2026-05-13 |
| 3. Local Ingestion MVP | 2/2 | **Complete** | 2026-05-13 |
| 4. Markdown Wiki Memory | 3/3 | **Complete** | 2026-05-13 |
| 5. Local Search and Briefs | 3/3 | **Complete** | 2026-05-13 |
| 6. Identity Hardening | 2/2 | **Complete** | 2026-05-13 |
| 7. Salience and Drift Engine | 2/2 | **Complete** | 2026-05-13 |
| 8. Cloud Reasoning and Tier Routing | 0/TBD | Not started | - |
| 9. Google Workspace Metadata Ingestion | 0/TBD | Not started | - |
| 10. Agentic Research Workflow | 0/TBD | Not started | - |
| 11. Computer-Use Automation | 0/TBD | Not started | - |
| 12. Encryption, Sync, and Production Hardening | 0/TBD | Not started | - |
