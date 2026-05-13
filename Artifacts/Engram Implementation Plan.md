# Engram Implementation Plan

Source PRD: `Artifacts/Product Requirements Document_Engram Full Specification.docx`

Markdown PRD copy: `Artifacts/Product Requirements Document_Engram Full Specification.md`

## 1. Implementation Thesis

Engram should be implemented as a local-first Windows semantic operating layer before expanding into cloud reasoning and computer-use automation.

The PRD describes a broad end state: passive ingestion, immutable raw history, metabolized Markdown wiki memory, identity hardening, drift detection, tiered model routing, research automation, and lightweight UX. The safest implementation path is to prove the core loop first:

1. Capture user context locally.
2. Store every source event immutably.
3. Convert raw events into durable Markdown memory.
4. Make that memory queryable.
5. Detect conflicts or commitments from real usage.

Cloud reasoning, Google Workspace ingestion, managed credit pooling, and full computer-use automation should be built only after this loop is reliable and auditable.

## 2. Product Scope From The PRD

### Core Capabilities

- Passive ingestion from screen/OCR, clipboard, app focus, local files, and Google Workspace metadata.
- Immutable `.engram/raw/[YYYY-MM-DD]/[Event_ID].json` history.
- Metabolized `.engram/wiki/*.md` memory for people, projects, goals, and concepts.
- `index.md` navigation over wiki nodes using Markdown links.
- Salience decay and archival of stale knowledge.
- Drift detection when new events contradict stored memory or priorities.
- Identity hardening through a 15-minute Discovery SOP and `user_identity.md`.
- Compute tiering across local SLM, cloud VLM, and hybrid logic.
- System tray widget, Alt+Space search, and morning/evening briefs.
- AES-256 local encryption, low background resource use, and resumable/idempotent agent runs.

### High-Risk Capabilities

- Continuous screenshot capture at 1-2 second intervals.
- Clipboard monitoring.
- Gmail/Calendar/Drive metadata ingestion.
- Cloud upload of filtered UI state.
- Managed API credit pooling.
- Browser/computer-use automation.
- Predictive interventions that may feel intrusive if identity constraints are weak.

These need explicit consent, narrow defaults, local audit logs, and kill switches.

## 3. Target Architecture

### Local Windows Layer

- `Engram.Service`: background Windows process for ingestion, local scheduling, event routing, and health checks.
- `Engram.Tray`: system tray UI for active context, credits/status, pause controls, and brief cards.
- `Engram.Search`: Alt+Space search surface over wiki/index state.
- `Engram.Store`: encrypted local store rooted at `.engram`.
- `Engram.Connectors`: file watcher, clipboard watcher, active-window tracker, OCR provider, and later GWS/365 providers.
- `Engram.Memory`: raw-to-wiki metabolizer, link/index manager, salience scorer, conflict detector.
- `Engram.Agents`: resumable task runners for research, synthesis, and automation.

### Cloud/Pro Layer

- `Engram.CloudGateway`: model routing, cost controls, tenant auth, and managed credit pooling.
- `Engram.CleanCache`: shared cache for non-private common research topics.
- `Engram.Sync`: encrypted multi-device continuity, added after local store contracts are stable.

### Provider Boundaries

All external systems should sit behind interfaces from day one:

- OCR provider: Windows Copilot Runtime first, fallback provider for development.
- Local model provider: Phi/Copilot Runtime first, mock/local test provider for CI.
- Cloud model provider: Gemini/Claude behind a routing interface.
- Browser automation provider: Playwright first, Computer Use API as a later adapter.
- Workspace provider: Google Workspace first, Microsoft 365 as a later adapter.

## 4. Data Contracts

### Raw Event

Fields:

- `event_id`
- `event_type`
- `captured_at`
- `source`
- `source_uri`
- `active_window`
- `text`
- `metadata`
- `privacy_class`
- `hash`
- `processing_status`

Storage:

- `.engram/raw/YYYY-MM-DD/[event_id].json`
- append-only
- content-addressed hash for duplicate detection
- no destructive edits

### Wiki Node

Fields:

- `node_id`
- `title`
- `node_type`
- `summary`
- `facts`
- `open_questions`
- `source_events`
- `links`
- `salience`
- `last_touched_at`
- `confidence`

Storage:

- `.engram/wiki/[Slug].md`
- front matter for machine-readable metadata
- Markdown body for human readability and LLM navigation

### Identity Profile

Files:

- `.engram/wiki/user_identity.md`
- `.engram/wiki/priorities.md`
- `.engram/wiki/anti_goals.md`

Rules:

- explicit user-authored or user-confirmed constraints outrank inferred preferences
- intervention logic must read identity constraints before notifying or acting

### Agent Run Log

Storage:

- `.engram/runs/[run_id]/log.md`
- `.engram/runs/[run_id]/state.json`

Rules:

- every action is idempotent
- every run can resume from the latest checkpoint
- automation never executes irreversible actions without an approval gate

## 5. Phased Build Plan

### Phase 1: Repository And Runtime Foundation

Goal: establish a buildable Windows-first project with test harnesses and local storage contracts.

Deliverables:

- Application skeleton for background service, tray/search UI, and shared libraries.
- `.engram` local workspace initializer.
- Configuration model for consent, capture intervals, excluded apps, storage paths, and provider selection.
- Structured logging and local diagnostics.
- Unit test setup and smoke test command.

Acceptance criteria:

- Fresh install can create `.engram` with required folders.
- App can start, stop, and report health without capturing data.
- Tests run locally without requiring cloud credentials.

### Phase 2: Immutable Raw Store

Goal: implement the source-of-truth event ledger.

Deliverables:

- Raw event schema.
- Append-only writer.
- Event hash/deduplication.
- Daily folder partitioning.
- Import replay command for development.

Acceptance criteria:

- File, clipboard, and manual test events persist under `.engram/raw`.
- Existing raw events are never rewritten.
- Replay can reprocess raw events into downstream indexes.

### Phase 3: Local Ingestion MVP

Goal: capture low-risk local signals before continuous screenshots or cloud metadata.

Deliverables:

- Local file watcher for Downloads, Documents, and Desktop.
- Clipboard watcher with opt-in and pause controls.
- Active-window/app-focus tracker.
- OCR provider interface with a development fallback.
- Consent and exclusion settings.

Acceptance criteria:

- Users can enable or disable each capture source independently.
- Excluded apps are never captured.
- Captured data is visible in raw event logs with source attribution.

### Phase 4: Markdown Wiki Memory

Goal: convert raw events into metabolized, queryable Markdown knowledge.

Deliverables:

- Wiki node schema and Markdown front matter.
- Raw-to-wiki metabolizer for people, projects, goals, documents, receipts, and decisions.
- `index.md` generator with wiki links.
- Node merge logic to update existing nodes instead of creating duplicates.
- Source-link back references to raw events.

Acceptance criteria:

- A repeated topic updates one existing wiki node.
- Every wiki fact can be traced back to one or more raw events.
- `index.md` gives a useful navigation map without vector search.

### Phase 5: Local Search And Briefs

Goal: make the memory useful to the user without automation risk.

Deliverables:

- Alt+Space search over wiki files and metadata.
- Morning/evening brief generator for promises, intentions, stale items, and changed facts.
- Tray widget for active context and capture status.
- Local-only query path.

Acceptance criteria:

- User can ask factual questions answered from `.engram/wiki`.
- Briefs cite source wiki nodes and raw events.
- UI exposes pause/resume and capture status clearly.

### Phase 6: Identity Hardening

Goal: make interventions constrained by explicit user identity rules.

Deliverables:

- 15-minute Discovery SOP flow.
- `user_identity.md`, `priorities.md`, and `anti_goals.md` generation.
- Confirmation/edit screen for extracted identity rules.
- Intervention policy evaluator.

Acceptance criteria:

- The system can explain why a notification is allowed or blocked.
- User can edit identity constraints directly.
- No proactive intervention bypasses the identity policy evaluator.

### Phase 7: Salience And Drift Engine

Goal: detect stale, important, or contradictory knowledge.

Deliverables:

- Salience score model.
- Time-decay scheduler.
- Archive mover for stale nodes.
- Conflict detector comparing new raw events against wiki facts and priorities.
- Drift alert model and tray/search UI surfacing.

Acceptance criteria:

- Old untouched nodes decay and can be archived.
- New contradictory events create drift alerts with source evidence.
- Alerts can be dismissed, accepted, or converted into wiki updates.

### Phase 8: Cloud Reasoning And Tier Routing

Goal: add Pro reasoning without compromising local trust.

Deliverables:

- Model router with task complexity classification.
- Local filtering that only sends approved state summaries to cloud providers.
- Cloud request audit log.
- Cost limits, rate limits, and per-user budget accounting.
- Semantic clean cache for non-private common research.

Acceptance criteria:

- Routine ingestion remains local by default.
- Every cloud call has a reason, payload summary, provider, cost estimate, and result.
- Private raw screen/clipboard/email data is never sent without explicit policy approval.

### Phase 9: Google Workspace And Cloud Metadata Ingestion

Goal: add Gmail, Calendar, and Drive context as explicit connectors.

Deliverables:

- OAuth/auth flow.
- Metadata-only ingestion mode.
- Connector-level scopes and revocation.
- GWS raw event types.
- Email/calendar/drive metabolizers.

Acceptance criteria:

- User can connect and disconnect GWS cleanly.
- GWS ingestion creates source-linked raw events and wiki updates.
- Connector scopes are minimal and visible.

### Phase 10: Agentic Research Workflow

Goal: implement high-signal research automation before broader computer control.

Deliverables:

- Research task runner.
- Playwright browser adapter.
- Multi-tab source collection.
- Source quality filter.
- Wiki summary writer.
- Run log and resume support.
- Side-by-side layout helper as a local UX enhancement.

Acceptance criteria:

- A research prompt opens sources, extracts content, and writes a cited wiki summary.
- Failed runs can resume from log state.
- User can inspect all visited sources.

### Phase 11: Computer Use Automation

Goal: allow the system to operate the Windows environment under strict controls.

Deliverables:

- Automation permission model.
- Action planner with preview.
- Approval gate for destructive or external actions.
- UI automation adapter.
- Recovery and rollback guidance for failed runs.

Acceptance criteria:

- Read-only automation works before write/action automation.
- Risky actions require user approval.
- Every automation action is logged with timestamp, target, and rationale.

### Phase 12: Encryption, Sync, And Production Hardening

Goal: make the local and Pro experiences safe enough for real personal data.

Deliverables:

- AES-256 encryption at rest.
- Key management strategy.
- Backup/export/delete flows.
- Performance budget checks for CPU/NPU/background usage.
- Encrypted cloud sync.
- Installer and update pipeline.

Acceptance criteria:

- Local data is encrypted at rest.
- User can export or delete all Engram data.
- Background capture respects resource budgets.
- Sync does not expose plaintext wiki/raw data to the backend.

## 6. MVP Recommendation

The MVP should stop at Phase 7.

MVP definition:

- Local `.engram/raw` event history.
- Local `.engram/wiki` memory.
- Manual and low-risk passive ingestion.
- Search and briefs.
- Identity hardening.
- Salience and drift alerts.

This proves the PRD's core promise: Engram remembers context over time and turns it into queryable, source-linked memory. It avoids shipping high-risk cloud upload and computer-use automation before the trust layer is ready.

## 7. Testing Strategy

### Unit Tests

- Raw event schema validation.
- Append-only writer behavior.
- Wiki node parsing and rendering.
- Link/index generation.
- Salience decay math.
- Drift detector rule cases.
- Identity policy evaluator.

### Integration Tests

- File watcher to raw event to wiki node.
- Clipboard event to raw event with opt-in settings.
- Raw replay to wiki regeneration.
- Search query to cited answer.
- Brief generation from promises and stale nodes.
- Agent run resume from checkpoint.

### Privacy And Safety Tests

- Excluded app capture suppression.
- Connector revocation.
- Cloud payload redaction.
- No-cloud mode enforcement.
- Data export/delete verification.
- Automation approval gates.

### Performance Tests

- Background idle CPU/NPU budget.
- Raw event write throughput.
- Wiki search latency.
- OCR provider latency.
- Long-running service stability.

## 8. Key Engineering Decisions To Lock Early

- Local store format: plain Markdown/JSON files first, with optional SQLite index for search speed.
- Encryption timing: implement before real user data ingestion, not after.
- Consent defaults: all sensitive capture sources disabled until explicitly enabled.
- Cloud upload policy: no raw screenshots, clipboard contents, or email bodies by default.
- Intervention policy: identity constraints must gate every proactive notification.
- Automation policy: preview and approval before any external or irreversible action.
- Provider strategy: every model/OCR/browser/workspace integration behind replaceable interfaces.

## 9. Open Questions

- Should screen capture be full-frame, active-window only, or OCR-text-only for the default mode?
- What exact CPU/NPU budget should define acceptable background sensing?
- Should the MVP include Google Workspace metadata, or wait until the local loop is proven?
- What is the minimum acceptable search quality without vector search?
- Should the wiki support user-authored pages from day one?
- What are the legal/privacy requirements for continuous clipboard and screen sensing in the first target market?
- How should "Energy Units" map to backend cost and user-visible limits?

## 10. Immediate Next Actions

1. Create the application skeleton and `.engram` initializer.
2. Implement raw event schema and append-only local writer.
3. Build a manual import/replay command so memory logic can be tested without passive capture.
4. Implement wiki node generation and `index.md`.
5. Add local search over wiki files.
6. Add identity Discovery SOP before proactive interventions.
7. Add drift detection only after wiki facts are source-linked.

