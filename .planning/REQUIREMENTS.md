# Requirements: Engram

## Functional Requirements

- [ ] **REQ-001** Create a Windows-first .NET solution skeleton for Engram with service, tray/search shell, shared local store library, CLI/dev tooling, and tests. Phase: 1.
- [ ] **REQ-002** Initialize a local `.engram` workspace with `raw`, `wiki`, `runs`, `config`, `logs`, and `archives` directories. Phase: 1.
- [ ] **REQ-003** Define a typed raw event schema with event id, type, capture timestamp, source, source URI, active window, text, metadata, privacy class, content hash, and processing status. Phase: 1.
- [ ] **REQ-004** Persist raw events under `.engram/raw/YYYY-MM-DD/[event_id].json` using append-only semantics. Phase: 1.
- [ ] **REQ-005** Compute deterministic content hashes and prevent duplicate raw events without rewriting existing files. Phase: 1.
- [ ] **REQ-006** Provide a replay/import command that can read raw events and hand them to later processing pipelines. Phase: 1.
- [ ] **REQ-007** Implement local file watching for Downloads, Documents, and Desktop with source attribution and user-controlled paths. Phase: 3.
- [ ] **REQ-008** Implement opt-in clipboard and active-window/app-focus capture with pause controls and excluded-app enforcement. Phase: 3.
- [ ] **REQ-009** Define OCR provider boundaries for Windows Copilot Runtime and development fallback providers. Phase: 3.
- [ ] **REQ-010** Create Markdown wiki node schema with front matter, node types, source event links, salience, confidence, and backlinks. Phase: 4.
- [ ] **REQ-011** Convert raw events into wiki nodes for people, projects, goals, concepts, documents, receipts, and decisions. Phase: 4.
- [ ] **REQ-012** Generate `index.md` navigation over wiki nodes using Markdown links rather than mandatory vector search. Phase: 4.
- [ ] **REQ-013** Provide Alt+Space semantic search over wiki files and local metadata. Phase: 5.
- [ ] **REQ-014** Generate morning/evening briefs with source links for promises, intentions, stale items, and changed facts. Phase: 5.
- [ ] **REQ-015** Expose tray UI state for active context, capture status, pause/resume, and energy/status indicators. Phase: 5.
- [ ] **REQ-016** Implement the identity Discovery SOP and write user-confirmed identity constraints into `user_identity.md`, `priorities.md`, and `anti_goals.md`. Phase: 6.
- [ ] **REQ-017** Gate proactive interventions with identity constraints and provide a reason for allowed or blocked interventions. Phase: 6.
- [ ] **REQ-018** Implement salience decay, archive movement, and stale-node detection. Phase: 7.
- [ ] **REQ-019** Detect drift when new events contradict wiki facts or priorities, with source-linked alerts. Phase: 7.
- [ ] **REQ-020** Add cloud model routing with local filtering, cost controls, provider audit logs, and no raw private upload by default. Phase: 8.
- [ ] **REQ-021** Add Google Workspace metadata ingestion with OAuth, minimal scopes, revocation, and source-linked raw events. Phase: 9.
- [ ] **REQ-022** Implement agentic research workflow with Playwright, multi-tab source collection, cited wiki summaries, and resumable run logs. Phase: 10.
- [ ] **REQ-023** Implement computer-use automation under strict permission, preview, approval, and action logging rules. Phase: 11.
- [ ] **REQ-024** Add AES-256 local encryption, key management, export/delete flows, encrypted sync, installer, and production performance checks. Phase: 12.

## Non-Functional Requirements

- [ ] **NFR-001** Background sensing must stay within a documented CPU/NPU budget and expose resource diagnostics. Phase: 12.
- [ ] **NFR-002** All agent and automation runs must be idempotent and resumable from persisted run state. Phase: 1, 10, 11.
- [ ] **NFR-003** Tests must run locally without cloud credentials for all local-first phases. Phase: 1.
- [ ] **NFR-004** Privacy-sensitive features must have explicit consent defaults, audit logs, and kill switches. Phase: 3, 8, 9, 11, 12.

## Traceability

| ID | Source | Phase | Status |
|----|--------|-------|--------|
| REQ-001 | Implementation Plan section 3 and 5 | Phase 1 | Pending |
| REQ-002 | PRD section 2.2, Implementation Plan section 5 | Phase 1 | Pending |
| REQ-003 | Implementation Plan section 4 | Phase 1 | Pending |
| REQ-004 | PRD section 2.2 | Phase 1 | Pending |
| REQ-005 | Implementation Plan section 5 | Phase 1 | Pending |
| REQ-006 | Implementation Plan section 10 | Phase 1 | Pending |
| REQ-007 | PRD section 2.1 | Phase 3 | Pending |
| REQ-008 | PRD section 2.1 | Phase 3 | Pending |
| REQ-009 | PRD section 2.1 | Phase 3 | Pending |
| REQ-010 | Implementation Plan section 4 | Phase 4 | Pending |
| REQ-011 | PRD section 3.2 | Phase 4 | Pending |
| REQ-012 | PRD section 3.1 | Phase 4 | Pending |
| REQ-013 | PRD section 9 | Phase 5 | Pending |
| REQ-014 | PRD section 9 | Phase 5 | Pending |
| REQ-015 | PRD section 9 | Phase 5 | Pending |
| REQ-016 | PRD section 4 | Phase 6 | Pending |
| REQ-017 | PRD sections 4 and 9 | Phase 6 | Pending |
| REQ-018 | PRD section 3.2 | Phase 7 | Pending |
| REQ-019 | PRD section 3.2 | Phase 7 | Pending |
| REQ-020 | PRD section 8 | Phase 8 | Pending |
| REQ-021 | PRD section 2.1 | Phase 9 | Pending |
| REQ-022 | PRD section 6.1 | Phase 10 | Pending |
| REQ-023 | PRD section 6 | Phase 11 | Pending |
| REQ-024 | PRD section 10 and Implementation Plan section 5 | Phase 12 | Pending |
| NFR-001 | PRD section 10 | Phase 12 | Pending |
| NFR-002 | PRD section 10 and Implementation Plan section 4 | Phase 1, Phase 10, Phase 11 | Pending |
| NFR-003 | Implementation Plan section 5 | Phase 1 | Pending |
| NFR-004 | Implementation Plan section 2 | Phase 3, Phase 8, Phase 9, Phase 11, Phase 12 | Pending |
