# Engram State

**Status:** Phase 8 complete + test integrity audit done
**Current Phase:** Phase 8 - Cloud Reasoning and Tier Routing (DONE)
**Next Phase:** Phase 9 - Google Workspace Metadata Ingestion [PRO TIER]
**Last Activity:** 2026-05-13
**Total Tests:** 415/415 passing
**Commits:** 18a1bfa (latest — strong assertion fix)

## Session Summary (2026-05-13)

### Phase 8 Completed
- Plan 08-01: Model Routing and Local Filtering
  - ICloudModelProvider, TaskComplexity, ModelRouter, LocalFilter, TierGuard
  - PrivacyClass, CloudModelRequest/Response, EngramConfig tier extensions
- Plan 08-02: Cloud Audit Log, Budget Controls, Clean Cache
  - CloudAuditEntry, CloudAuditLog (JSONL), BudgetManager, BudgetConfig
  - CleanCache, CacheEntry
- 80 new tests, 415/415 total passing
- Commit: 2af3167

### Test Integrity Audit Done
- Found: CaptureOrchestratorTests rate limiter assertion was weakened
  - Was: `Assert.True(captured + dropped == 500)` — proves nothing
  - Now: `Assert.True(dropped > 0)` + `Assert.True(captured < 500)` — proves rate limiting fires
  - Strong assertion PASSES — code was correct all along
- Verified: Phase 7 overlap threshold (>=1) was always that value, never changed
- Verified: Phase 8 regex fixes were legitimate code fixes (code wrong, tests right)
- Verified: Phase 8 test data fixes were test-writing errors, not accommodation
- Commit: 18a1bfa

### Memory Limits Increased
- memory_char_limit: 2200 → 5000
- user_char_limit: 1375 → 3000

### What's Next
Phase 9: Google Workspace Metadata Ingestion [PRO TIER]
- OAuth/auth flow
- Metadata-only ingestion mode
- Connector-level scopes and revocation
- GWS raw event types
- Email/calendar/drive metabolizers

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
- 24 new tests (80 total)

### Production Hardening (Complete)
- HashIndex, FileLock, WAL, InputValidator, EngramConfigStore
- RateLimiter, CircuitBreaker, Debouncer, ExclusionList
- Streaming enumeration, pagination
- 45 new tests (125 total)

### Phase 3 Summary (Complete)
- Provider interfaces: IFileCaptureProvider, IClipboardProvider, IActiveWindowProvider, IOcrProvider
- FileWatcher, ClipboardWatcher, ActiveWindowTracker, CaptureOrchestrator
- 48 new tests (173 total)

### Phase 4 Summary (Complete)
- WikiNode model (7 entity types), WikiNodeSerializer, WikiNodeStore
- WikiMetabolizer, IndexGenerator, source-linked facts
- 43 new tests (216 total)

### Phase 5 Summary (Complete)
- SearchEngine (TF-IDF, AND semantics), BriefGenerator, CaptureStatus
- CLI: engram search, engram brief, engram status
- 44 new tests (260 total)

### Phase 6 Summary (Complete)
- UserProfile, IdentityStore, DiscoverySOP, InterventionPolicy
- CLI: engram discover, engram identity
- 31 new tests (291 total)

### Phase 7 Summary (Complete)
- SalienceScorer, ArchiveManager, DriftDetector, DriftAlertStore
- CLI: engram salience, engram drift
- 44 new tests (335 total)

### Phase 8 Summary (Complete) [PRO TIER]
- Cloud/: ICloudModelProvider, ModelRouter, LocalFilter, TierGuard
- Cloud/: CloudAuditLog, BudgetManager, CleanCache
- EngramConfig: TierLevel, CloudEnabled, budget settings
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
