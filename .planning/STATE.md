# Engram State

**Status:** Phase 8 complete — CLOUD REASONING (PRO TIER) COMPLETE
**Current Phase:** Phase 8 - Cloud Reasoning and Tier Routing (DONE)
**Next Phase:** Phase 9 - Google Workspace Metadata Ingestion [PRO TIER]
**Last Activity:** 2026-05-13
**Total Tests:** 415/415 passing

## Accumulated Context

### Phase 1 Summary (Complete)
- .NET solution skeleton with Store, CLI, Tests
- .engram workspace initializer (idempotent)
- Raw event schema (11 fields, snake_case JSON)
- Append-only writer with content-addressed deduplication
- Replay enumerator with deterministic ordering
- CLI commands: engram init, engram replay
- 56 tests

### Phase 2 Summary (Complete)
- Atomic writes via .tmp + rename
- Per-event processing sidecar (.meta.json)
- Filtered replay with ReplayQuery (date, source, status)
- Integrity verification on read (hash recomputation)
- CLI filter flags (--from, --to, --source, --status, --limit, --offset, --json)
- 24 new tests (80 total)

### Production Hardening (Complete)
- HashIndex: O(1) dedup via persistent hash index
- FileLock: cross-platform file locking
- WriteAheadLog: crash recovery via WAL
- InputValidator: path traversal, unicode, max sizes
- EngramConfigStore: config persistence
- Streaming enumeration (yield return)
- Pagination (limit/offset)
- 45 new tests (125 total)

### Phase 3 Summary (Complete)
- Provider interfaces: IFileCaptureProvider, IClipboardProvider, IActiveWindowProvider, IOcrProvider
- ExclusionList: thread-safe, default password manager exclusions
- RateLimiter: token bucket flood protection
- Debouncer<T>: event coalescing
- CircuitBreaker: sustained failure protection
- FileWatcher: debounce, self-filter, error recovery
- ClipboardWatcher: polling + content hash + exclusion
- ActiveWindowTracker: polling + process name extraction
- CaptureOrchestrator: coordinates all sources + consent
- 48 new tests (173 total)

### Phase 4 Summary (Complete)
- WikiNode model: 7 entity types (Person, Project, Goal, Concept, Document, Receipt, Decision)
- WikiNodeSerializer: Markdown <-> YAML front matter
- WikiNodeStore: atomic writes, thread-safe persistence
- WikiMetabolizer: raw event -> wiki node (merge, no duplicates)
- IndexGenerator: index.md with [[links]], grouped by type
- Source-linked facts (every fact traces to raw event evidence)
- 43 new tests (216 total)

### Phase 5 Summary (Complete)
- SearchEngine: keyword search, TF-IDF scoring, AND semantics
- BriefGenerator: morning/evening briefs with citations
- CaptureStatus: pause/resume, per-source toggle
- SearchResult model
- CLI commands: engram search, engram brief, engram status
- 44 new tests (260 total)

### Phase 6 Summary (Complete)
- UserProfile, Priority, AntiGoal models
- IdentityStore: thread-safe persistence
- DiscoverySOP: interview flow for identity extraction
- InterventionPolicy: gates ALL proactive behavior
- CLI commands: engram discover, engram identity
- 31 new tests (291 total)

### Phase 7 Summary (Complete)
- SalienceScorer: power law decay
- ArchiveManager: move/restore stale nodes
- DriftDetector: keyword contradiction + status change
- DriftAlertStore: persist, resolve, statistics
- CLI commands: engram salience, engram drift
- 44 new tests (335 total)

### Phase 8 Summary (Complete) [PRO TIER]
- ICloudModelProvider interface (mirrors IOcrProvider pattern)
- TaskComplexity enum (Low/Medium/High)
- ModelRouter: classifies tasks, routes to local/GeminiFlash/ClaudeSonnet
- LocalFilter: strips private data, redacts PII (email/phone/tokens)
- TierGuard: blocks cloud for Free tier, allows for Pro
- PrivacyClass enum (Public/Internal/Private/Sensitive)
- CloudModelRequest/Response models
- CloudAuditEntry: full audit trail (reason, provider, cost, tokens)
- CloudAuditLog: append-only JSONL at .engram/logs/cloud-audit.jsonl
- BudgetManager: daily/monthly/per-call cost limits
- BudgetConfig: configurable limits from EngramConfig
- CleanCache: semantic cache for non-private topics with TTL eviction
- CacheEntry: hit counting, expiration, persistence
- EngramConfig extensions (Tier, CloudEnabled, budget settings)
- TierLevel enum (Free/Pro)
- 80 new tests (415 total)

### Canonical References
- Artifacts/Product Requirements Document_Engram Full Specification.md
- Artifacts/Engram Implementation Plan.md
- docs/QUALITY-GATE-POLICY.md
- docs/TIER-ARCHITECTURE.md

### Decisions Log
- D-001..D-010: Phase 1 foundation decisions
- D-011..D-015: Phase 2 hardening decisions
- D-016..D-022: Phase 3 capture decisions
- D-023..D-027: Phase 4 wiki decisions
- D-028..D-032: Phase 8 cloud reasoning decisions

### Roadmap Evolution
- 2026-05-10: Bootstrapped GSD planning from PRD
- 2026-05-13: Phase 1 executed (TDD, 56 tests)
- 2026-05-13: Phase 2 executed (TDD, 80 tests)
- 2026-05-13: Production hardening (125 tests)
- 2026-05-13: Phase 3 executed (TDD, 173 tests)
- 2026-05-13: Phase 4 executed (TDD, 216 tests)
- 2026-05-13: Phase 5 executed (TDD, 260 tests)
- 2026-05-13: Phase 6 executed (TDD, 291 tests)
- 2026-05-13: Phase 7 executed (TDD, 335 tests)
- 2026-05-13: Phase 8 executed (TDD, 415 tests) [PRO TIER]
