# Engram State

**Status:** Phase 6 complete — Identity Hardening + UI
**Current Phase:** Phase 6 - Identity Hardening (DONE)
**Next Phase:** Phase 9 - Google Workspace Metadata Ingestion [PRO TIER]
**Last Activity:** 2026-05-17
**Total Tests:** 516/516 passing
**Latest Commit:** Phase 6 UI complete
**Git Status:** master

## Architecture

Engram is a desktop app. Not a web app, not a localhost dev server.

```
User clicks Engram icon
  → Tauri shell (Rust, ~10MB)
    → Spawns .NET API sidecar on 127.0.0.1:5000
    → Loads React frontend
    → Frontend connects to sidecar automatically
    → Chat/Search/Wiki/Timeline/Settings all work
    → User closes app → sidecar killed
```

**Installers:**
- `Engram_1.0.0_x64-setup.exe` (NSIS, 2.46 MB)
- `Engram_1.0.0_x64_en-US.msi` (MSI, 3.69 MB)

## Current State

### Desktop App (Phase 5 UI — Complete)
- **Tauri v2** shell with auto-spawn .NET sidecar
- **React 19** + TypeScript + Tailwind CSS dark theme
- **ChatGPT-style sidebar**: New Chat, session history, user profile, More menu
- **5 views**: Chat, Search, Wiki, Timeline, Settings
- **All views wired to API** with loading/error states
- **Chat**: localStorage persistence, real API calls
- **Discovery Interview**: 7-step in-app flow on first launch
- **Settings**: Profile, workspace stats, power mode, identity display, drift alerts

### API Sidecar (16 endpoints)
```
GET  /                              Health check
GET  /api/status                    Workspace stats
GET  /api/search?q=                 Search wiki
GET  /api/wiki                      List wiki nodes
GET  /api/wiki/:id                  Get single node
GET  /api/brief?time=morning|evening  Generate brief
GET  /api/events                    List raw events
GET  /api/identity                  User profile
GET  /api/identity/anti-goals       List anti-goals
GET  /api/identity/priorities       List priorities
GET  /api/discovery/status          Check discovery complete
GET  /api/drift                     Drift alerts
POST /api/discovery                 Run discovery interview
POST /api/intervention/check        Evaluate intervention policy
POST /v1/chat/completions           Chat (mock — needs inference engine)
PUT  /api/identity                  Update user profile
```

### Phase 5 Library (Complete 2026-05-13)
- SearchEngine: TF-IDF keyword search, AND semantics, field weighting
- BriefGenerator: morning/evening briefs with source citations
- CaptureStatus: pause/resume, per-source toggles, counters
- CLI: engram search, brief, status

### Phase 6 Library + UI (Complete 2026-05-17)
- DiscoverySOP: 7-step interview → UserProfile + Priorities + AntiGoals
- InterventionPolicy: gates all proactive actions against anti-goals
- IdentityStore: user_identity.md, priorities.md, anti_goals.md
- Frontend: DiscoveryInterview component, editable Settings identity section
- API: 6 new endpoints (discovery, identity CRUD, intervention check)

### Phase 7 (Complete 2026-05-13)
- SalienceScorer: power-law decay S(t) = S0 * e^(-λt)
- DriftDetector: keyword matching + contradiction alerts
- DriftAlertStore: JSON persistence, accept/dismiss/convert
- ArchiveManager: moves stale nodes to archives/, restore support

### Phase 8 (Complete 2026-05-13)
- CloudCallPipeline: Route→Filter→TierGuard→RateLimit→Budget→Cache→Provider→Audit
- ModelRouter: Low→Local, Medium→Gemini Flash, High→Claude Sonnet
- LocalFilter: strips PII, ~90% token reduction
- TierGuard: Free tier blocks cloud, Pro tier allows
- BudgetManager: $1/day, $25/month, $0.50/call limits
- CloudAuditLog: append-only JSONL audit trail
- CleanCache: LRU cache, 7-day TTL, persisted to disk

## Tests: 516/516

| Category | Count |
|----------|-------|
| Phase 1-2 (Foundation + Hardening) | ~125 |
| Phase 3 (Ingestion) | ~48 |
| Phase 4 (Wiki) | ~43 |
| Phase 5 (Search + Briefs) | ~44 |
| Phase 6 (Identity) | ~31 |
| Phase 7 (Salience + Drift) | ~44 |
| Phase 8 (Cloud) | ~29 |
| API Integration Tests | 18 |
| Edge Case Tests | 39 |
| Phase 6 UI Tests | 15 |
| **Total** | **516** |

## Unresolved

- GeminiFlashProvider/ClaudeSonnetProvider: stubs, not wired to real APIs
- CopilotKit removed (SSE protocol mismatch) — re-add when inference engine ready
- Capture toggle switches in Settings are visual only (not wired to API)
- Alt+Space global hotkey not implemented
- No CI/CD pipeline

## Decisions Log

- D-001..D-010: Phase 1 foundation
- D-011..D-015: Phase 2 hardening
- D-016..D-022: Phase 3 capture
- D-023..D-027: Phase 4 wiki
- D-028..D-031: Phase 5 search
- D-032..D-035: Phase 6 identity
- D-033: Frontend = Tauri + React + Tailwind + shadcn/ui + CopilotKit
- D-034: Local inference = LLamaSharp with Vulkan (not Ollama)
- D-035: Brain = Phi-4-mini GGUF Q4_K_M
- D-036: Power Mode = Eco (local) / Turbo (cloud)
- D-037: .NET sidecar as inference router
- D-038: Installer ~130MB standard, ~2.4GB offline
- D-039: Model at %LOCALAPPDATA%/Engram/models/
- D-040: Vulkan fallback: discrete GPU → iGPU → CPU+SIMD
- D-041: Min hardware: 8GB RAM, quad-core, Win10 64-bit
- D-042: CopilotKit runtimeUrl → .NET sidecar
- D-043: Tauri spawns .NET sidecar as child process (sidecar pattern)
- D-044: Discovery interview on first launch (chat-based, 7 steps)
- D-045: Intervention policy gates all proactive chat actions
