# Engram State

**Status:** Phase 8 complete — Cloud Reasoning Pipeline operational
**Current Phase:** Phase 8 - Cloud Reasoning and Tier Routing (DONE)
**Next Phase:** Phase 9 - Google Workspace Metadata Ingestion [PRO TIER]
**Last Activity:** 2026-05-13
**Total Tests:** 444/444 passing
**Latest Commit:** f3b6aa5 (feat: complete cloud reasoning pipeline and integration tests)
**Git Status:** master, 5 commits ahead of origin/master (not pushed)

## Phase 8 Final State

### Cloud Reasoning Pipeline (Complete)
- **CloudCallPipeline.cs** — Orchestration: Route → Filter → TierGuard → RateLimit → Budget → Cache → Provider → Audit
- **CloudRateLimiter.cs** — Per-minute and per-hour rate limiting
- **MockCloudModelProvider.cs** — Test/dev provider
- **GeminiFlashProvider.cs** — Stub for Gemini 3 Flash
- **ClaudeSonnetProvider.cs** — Stub for Claude 4.5 Sonnet
- **Phase 8 plans 08-01 and 08-02: COMPLETE**

### Test Results
- 444 total tests, 444 passed, 0 failed
- 29 new Phase 8 tests (17 integration + 12 unit)
- All 6 quality gates pass:
  1. Model routing selects correct tier ✓
  2. Local filter reduces token ingress ✓
  3. Cloud call → audit log with reason+cost ✓
  4. Private data never sent to cloud ✓
  5. Budget limit enforced (no runaway costs) ✓
  6. Local filtering < 50ms latency (0.36ms avg) ✓

### Test Fixes Applied (Phase 8)
- CloudRateLimiter constructor validation: maxPerMinute cannot exceed maxPerHour
- Budget test: uses EstimateCost (pre-call ~$0.06) not mock cost; test per-call limit blocking
- PII filter test: verify PII absent from audit log, not from mock response
- Latency test: warm-up regex JIT, assert per-call average <50ms (0.36ms avg)

### CLI Entry Point Created
- `src/Engram.Cli/Program.cs` — ~180 lines, 8 subcommands
- Commands: init, status, search, capture, brief, wiki, help, version
- Wired to Engram.Store services (RawEventWriter, ReplayEnumerator, WikiIndex, SearchIndex, CaptureOrchestrator)

### Phase Numbering Resolved
- Implementation Plan renumbered to 1-indexed (Phase 0→1, ..., Phase 11→12)
- All docs, code, tests now use consistent 1-indexed system

## Unresolved

- **CS0649 warnings**: Unassigned fields in CaptureOrchestrator
- **CS0414 warnings**: Unused _disposed fields in WriteAheadLog, ProcessingSidecar, DriftAlertStore, IdentityStore
- **xUnit1031 warnings**: Blocking task operations in some tests
- **GeminiFlashProvider/ClaudeSonnetProvider**: Stubs, not wired to real APIs yet

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
- Cloud/: CloudCallPipeline, CloudRateLimiter, MockCloudModelProvider
- Cloud/: GeminiFlashProvider, ClaudeSonnetProvider (stubs)
- Prior phases: ICloudModelProvider, ModelRouter, LocalFilter, TierGuard
- Prior phases: CloudAuditLog, BudgetManager, CleanCache
- EngramConfig: TierLevel, CloudEnabled, budget settings
- CLI: engram init/status/search/capture/brief/wiki/help/version
- 29 new tests (444 total)

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
- D-033: Frontend stack = Tauri + React + Tailwind + shadcn/ui + CopilotKit
- D-034: Local inference = LLamaSharp with Vulkan (not Ollama, not LocalAI)
- D-035: Brain = Phi-4-mini GGUF Q4_K_M (~2.2GB, downloaded on first run)
- D-036: Power Mode toggle = Eco (local) / Turbo (cloud), default Eco
- D-037: .NET sidecar as inference router (single endpoint for CopilotKit)
- D-038: Installer = ~130MB standard, ~2.4GB offline, ~50MB runtime-dependent
- D-039: Model cached at %LOCALAPPDATA%/Engram/models/, not bundled in installer
- D-040: Vulkan fallback chain: discrete GPU → iGPU → CPU+SIMD
- D-041: Hardware minimum: 8GB RAM, modern quad-core, Windows 10 64-bit
- D-042: CopilotKit runtimeUrl points to .NET sidecar localhost endpoint
- D-043: Tauri spawns .NET sidecar as child process (sidecar pattern)
