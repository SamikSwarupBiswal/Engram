# Engram State

**Status:** Existential Validation Era (D1-D5) — Completed Phase: D4 (Real Task Execution Validation), Next Phase: D5 (Productization)
**Last Activity:** 2026-05-24
**Branch:** `master`
**Tests:** 1640 store tests + 88 API tests passing (1728/1728 total tests green)
**Latest Commit:** (completed — Phase D4 implementation)

## What Engram Is

Engram is a persistent semantic operating layer for Windows designed to manage a user's digital life by bridging intent and action. It operates on the principle of Longitudinal Continuity — remembering decisions, extracting commitments, and operating the OS to perform research or tasks on the user's behalf.

Desktop app (Tauri v2 + React) with .NET 8 API sidecar. Install via .exe installer (~77MB). Model auto-downloads on first launch (~2.3GB).

**THE CHAT IS NOT A CHATBOT.** It's the intent interface into the semantic operating system. Every message is classified by intent, routed to the appropriate subsystem, and responded to with contextual memory retrieval.

## Architecture

```
User double-clicks Engram
  → Tauri shell (Rust, ~10MB)
    → Spawns .NET API sidecar on 127.0.0.1:5000
    → Loads React frontend
    → Frontend connects to sidecar automatically
    → All 10 views work
    → Model auto-downloads on first launch
    → Inference lifecycle: Starting → DetectingBackend → BackendReady → LoadingModel → Ready
    → Chat uses INTENT-CLASSIFIED, RETRIEVAL-AUGMENTED system prompt
    → BackgroundMetabolismService runs every 5 minutes
    → Every inference: KV cache cleared after completion
    → User closes app → sidecar killed
```

### Chat Pipeline (Intent Interface)

```
User Message
↓
IntentClassifier (7 types: MemoryQuery, TimelineQuery, DriftAnalysis, StateSynthesis, ResearchTask, AutomationTask, Conversational)
↓
TaskRouter (central nervous system)
↓
(subsystem retrieval)
  → SemanticSearchEngine (salience + recency + relevance scoring)
  → WikiNodeStore (knowledge graph)
  → IdentityStore (goals, priorities, anti-goals)
  → DriftDetector (behavioral contradictions)
↓
PromptAssembler (RetrievalBudgetManager: 2000 token budget)
↓
LLM Reasoning (Phi-4-mini)
↓
Response + Memory Extraction → WikiMetabolizer → EventBus
```

### Background Metabolism (The Brain)

```
BackgroundMetabolismService (every 5 minutes)
  → SemanticDeduplicator (prevent wiki rot)
  → SalienceScorer (time decay)
  → DriftDetector (contradiction detection)
  → ContradictionDetector (behavioral intelligence)
    → GoalActivityGap (goal fading while unrelated activity high)
    → PriorityDrift (declared priorities not reflected in behavior)
    → AbandonedCommitment (no follow-through)
    → IdentityBehaviorGap (identity claims not supported by behavior)
  → ArchiveManager (archive stale nodes)
  → InterventionGenerator (proactive guidance)
  → EventBus (emit events)
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Desktop Shell | Tauri v2 (Rust) |
| Frontend | React 19 + TypeScript + Tailwind CSS |
| Backend | .NET 8 Minimal API |
| Inference | LLamaSharp + Vulkan (local) |
| Model | Phi-4-mini GGUF Q4_K_M (~2.3GB) |
| Storage | Markdown files (.engram/) |
| Cloud | OpenAI-compatible API (any provider) |
| Encryption | AES-256-GCM |
| Search | TF-IDF + salience/recency/relevance scoring |
| OAuth | Google Workspace |

## Project Structure

```
Engram/
├── src/
│   ├── Engram.Store/          Core library (all logic)
│   │   ├── Agent/             Research agent, browser, citations
│   │   ├── Automation/        Action executor, permission gate, safety systems, trust tiers, rate limits, Windows dynamic COM
│   │   ├── Billing/           Token budget, pricing
│   │   ├── Capture/           Event capture (clipboard, files, windows)
│   │   ├── Cloud/             Cloud pipeline, providers, audit
│   │   ├── Events/            EventBus, EventEnvelope, TimelineSubscriber
│   │   ├── Google/            Gmail, Calendar, Drive metadata
│   │   ├── Identity/          User profile, discovery, intervention
│   │   ├── Inference/         LLamaSharp, GPU detection, model mgmt, KV lifecycle
│   │   ├── Memory/            ConversationMemoryExtractor, Pipeline, PromptAssembler
│   │   ├── Metabolism/        BackgroundMetabolismService, Deduplicator, ContradictionDetector, RetrievalBudgetManager, InterventionGenerator
│   │   ├── Orchestration/     IntentClassifier, TaskRouter
│   │   ├── Perception/        Screen capture, OCR, layout snap
│   │   ├── Salience/          Decay scoring, drift detection
│   │   ├── Search/            TF-IDF search, semantic search, brief generator
│   │   ├── Security/          Encryption, export, delete, sync
│   │   ├── Validation/        Input validation, sanitization
│   │   └── Wiki/              Wiki node store, serializer, metabolizer
│   ├── Engram.Cli/            Developer CLI
│   ├── Engram.Api/            ASP.NET Minimal API (sidecar)
│   └── Engram.App/            Tauri + React frontend
│       ├── src/                React components (10 views)
│       ├── src-tauri/          Rust shell + sidecar config
│       ├── installer.nsi       NSIS installer script
│       ├── build-*.ps1         Build scripts
│       └── validate-install.ps1  Post-install validation
├── tests/
│   └── Engram.Store.Tests/    1544 tests
└── .planning/                 All planning docs
```

## API Endpoints (92)

```
GET  /                              Health
GET  /api/health                    Lifecycle health (single source of truth)
GET  /api/health/logs               Lifecycle logs
GET  /api/health/transparency       Get transparency and active degradations profile
POST /api/health/retry              Retry after error
GET  /api/search                    Search wiki
GET  /api/wiki                      List wiki nodes
GET  /api/wiki/:id                  Get single node
GET  /api/brief                     Morning/evening brief
GET  /api/events                    Raw event history
GET  /api/status                    Workspace stats
GET  /api/identity                  User profile
GET  /api/identity/anti-goals       Anti-goals
GET  /api/identity/priorities       Priorities
GET  /api/discovery/status          Discovery complete?
GET  /api/drift                     Drift alerts
GET  /api/drift/stats               Alert statistics
GET  /api/salience                  Salience scores
GET  /api/archive                   Archived nodes
GET  /api/archive/candidates        Nodes eligible for archival
GET  /api/model/status              Model + GPU info
GET  /api/power-mode                Current mode
GET  /api/tokens                    Token budget status
GET  /api/tokens/pricing            Plans, packs, rates
GET  /api/provider                  Provider config
GET  /api/security/status           Encryption configured?
GET  /api/automation/log            Action history
GET  /api/research/:id              Research run details
GET  /api/research                  List research runs
GET  /api/gws/status                Google connection status
GET  /api/gws/url                   OAuth URL
GET  /api/gws/emails                Gmail metadata
GET  /api/gws/events                Calendar metadata
GET  /api/gws/files                 Drive metadata
GET  /api/diagnostics/export        Full runtime diagnostics snapshot
POST /api/discovery                 Run discovery interview
POST /api/intervention/check        Evaluate intervention
POST /api/drift/:id/accept          Accept drift alert
POST /api/drift/:id/dismiss         Dismiss drift alert
POST /api/drift/:id/convert         Convert to wiki update
POST /api/archive/stale             Archive stale nodes
POST /api/archive/:id/restore       Restore from archive
POST /api/model/download            Download model
POST /api/model/load                Load model
POST /api/model/unload              Unload model
POST /api/power-mode                Switch eco/turbo
POST /api/tokens/check              Check token budget
POST /api/tokens/pack               Buy token pack
POST /api/tokens/tier               Change tier
POST /api/provider                  Save provider config
POST /api/security/setup            Setup encryption
POST /api/security/unlock           Unlock encryption
POST /api/security/change-password  Change password
POST /api/security/export           Export all data
POST /api/security/import           Import data
POST /api/security/delete           Delete all data
POST /api/automation/plan           Create action plan
POST /api/automation/approve-all    Approve all pending
POST /api/automation/deny-all       Deny all pending
POST /api/automation/execute        Run approved actions
POST /api/automation/rollback       Rollback last N
POST /api/research/start            Start research
POST /api/research/:id/resume       Resume research
POST /api/research/:id/cancel       Cancel research
POST /api/gws/connect               OAuth token exchange
POST /api/gws/disconnect            Revoke access
POST /api/gws/sync                  Sync all metadata
POST /v1/chat/completions           Chat (intent-classified, retrieval-augmented)
PUT  /api/identity                  Update profile
GET  /api/governance/activity        Get activity feed
GET  /api/governance/traces          Get causal reason traces
GET  /api/governance/trust           Get trust scores and multipliers
POST /api/governance/forget          Purge node and reconcile propagation
POST /api/governance/dispute         Dispute and rollback narrative
POST /api/governance/settings        Update boundaries and configs
POST /api/governance/recover         Manual override to restore Frozen state
GET  /api/governance/audit           Get safety constitution audit log
```

## Frontend Views (10)

| View | API Connections | Status |
|------|----------------|--------|
| Chat | /v1/chat/completions, /api/tokens/check, /api/model/* | Connected |
| Search | /api/search | Connected |
| Wiki | /api/wiki, /api/salience | Connected |
| Timeline | /api/events | Connected |
| Settings | /api/status, /api/identity, /api/drift, /api/tokens, /api/provider, /api/security, /api/brief, /api/gws, /api/diagnostics/export | Connected |
| Archive | /api/archive, /api/archive/candidates | Connected |
| Research | /api/research/* | Connected |
| Automation | /api/automation/* | Connected |
| ModelDownloadBar | /api/model/status, /api/model/download, /api/model/load | Connected |
| DiscoveryInterview | /api/discovery/status, /api/discovery | Connected |

- Phases 1-12, D1, D2, D3, and D4 implemented and verified (Cognitive Architecture Era complete)
- Phase 10 (Unified Semantic World Model) completed, featuring a Unified Entity Graph, Cross-Modal Identity Resolution, Temporal World Fusion, Attention & Salience Unification, Semantic Scene Construction, Cross-System Memory Propagation, and Global Consistency Engine.
- Phase 11 (Human Trust & Coexistence) completed, featuring an Explainability narrative layer, Memory Sovereignty with deletion envelopes and propagation reconciliation, trust calibration, cognitive boundaries, ambient restraint, transparency/dispute resolution, and a safety constitution with an isolated state machine and immutable audit log.
- Phase 12 (Cognitive Homeostasis & Longitudinal Endurance) completed, featuring metabolic resource-awareness, dynamic cognitive priority stack triage, startup WAL causal reconciler self-healing, metadata daily ZIP backups, token-bucket pacing, and friction-scaled alert silence thresholds.
- Phase D1 (Reality Hardening) completed, focusing on installer robustness, multi-monitor/DPI edge cases, wake/sleep OS cycles, and Playwright driver fallbacks.
- Phase D2 (Human Coexistence Validation) completed, featuring graded progressive containment states, deferred mutation queueing, action outcome semantic OCR/state verifiers, friction quiet windows, Yield-to-Focus multitasking gating with cognitive debt, capability warmups, and trust regression.
- Phase D3 (Longitudinal Existential Validation) completed, featuring virtual temporal time-warp simulation, clock providers, 14-day contradiction auto-expiry, protected islands archival shield, and ecological health telemetry metrics.
- Phase D4 (Real Task Execution Validation) completed, featuring cooperative control transfer (yield-first, abort-second), intent confidence hysteresis, silent pause defaults, layered verification cascade, context binder, and coexistence telemetry.
- 1640/1640 Store tests passing (11 new Phase D2 tests, 4 new Phase D3 tests, 7 new Phase D4 tests, full regression green) + 88/88 API integration tests passing (1728/1728 total tests green)

## Tests: 1640/1640 (Engram.Store.Tests)

| Category | Count |
|----------| ------|
| Foundation + Hardening | ~125 |
| Ingestion | ~48 |
| Wiki | ~43 |
| Search + Briefs | ~44 |
| Identity + Discovery | ~46 |
| Salience + Drift | ~30 |
| Cloud Pipeline | ~29 |
| Token Budget | ~49 |
| Google Workspace | ~46 |
| Research Agent | ~38 |
| Automation | ~37 |
| Security | ~38 |
| API Integration | ~18 |
| Edge Cases | ~39 |
| Inference | ~19 |
| Intent Classifier | 35 |
| Task Router | 21 |
| Conversation Memory | 52 |
| Prompt Assembler | 19 |
| Event Bus | 19 |
| Timeline Subscriber | 9 |
| Semantic Search | 17 |
| Background Metabolism | 19 |
| Semantic Deduplicator | 14 |
| Contradiction Detector | 10 |
| Retrieval Budget | 16 |
| Intervention Generator | 10 |
| **Sprint 4: Cognitive Stabilization** | **32** |
| **Sprint 5: Human-Compatible Cognition** | **23** |
| **Sprint 6: Semantic Perception** | **22** |
| **Sprint 7: Behavioral Reality Validation** | **62** |
| **Sprint 8: Longitudinal Reality Testing** | **38** |
| **Phase 7: The Embodied Execution Megaphase** | **146** |
| **Phase 8: Executional World Model** | **47** |
| **Phase 9: Execution Reliability & Embodied Safety** | **20** |
| **Phase 10: Unified Semantic World Model** | **35** |
| **Phase 11: Human Trust & Coexistence** | **8** |
| **Phase 12: Cognitive Homeostasis & Longitudinal Endurance** | **10** |
| **Phase D2: Human Coexistence Validation** | **11** |
| **Phase D3: Longitudinal Existential Validation** | **4** |
| **Total** | **1633** |

## Billing (Token Budget)

| Tier | Price | Monthly Tokens |
|------|-------|---------------|
| Free | $0 | ~60,000 |
| Pro | $20-30 | 500,000 |
| Small Pack | $5 | +100,000 |
| Large Pack | $20 | +500,000 |

Token costs: Gemini 1x/3x, Claude 10x/30x, Local 0x
Localhost APIs always free (bypass tier guard)

## Runtime Survivability — RESOLVED

**Original Issue:** "Desktop app unable to connect to LLM"
**Root Cause:** KV cache exhaustion causing catastrophic phase-transition collapse at request ~33
**Fix:** Mandatory KV cache clearing after every inference request with verification

### Survivability Metrics (from validation)
```
Soak test: 100/100 requests, 0 failures
KV reset: 0 misses out of 100
Cleanup success rate: 100%
Consecutive failures: 0
Runtime operational: true
Post-soak state: Ready
```

## Semantic Continuity — COMPLETE (Phase 22)

The chat endpoint was transformed from a generic chatbot into the intent interface into the semantic operating system.

### Intent Classification
7 intent types detected from user messages:
- **MemoryQuery** — "What do you know about..." → retrieves from wiki
- **TimelineQuery** — "What was I doing..." → queries activity history
- **DriftAnalysis** — "Am I making progress?" → compares goals vs behavior
- **StateSynthesis** — "What matters most?" → synthesizes current state
- **ResearchTask** — "Find the best..." → research on topic
- **AutomationTask** — "Open VSCode..." → executes actions
- **Conversational** — general chat → standard prompt assembly

### Memory Pipeline
Every chat conversation automatically:
1. Extracts entities (Person, Project, Goal, Decision, Preference, Anxiety, Task)
2. Converts to RawEvents
3. Feeds to WikiMetabolizer
4. Creates/updates wiki nodes
5. Publishes events to EventBus
6. Invalidates search index

### Event Bus
Central event stream connecting all subsystems:
- Typed subscriptions (e.g., "chat.completed")
- Wildcard subscriptions (e.g., "wiki.*")
- Global subscriptions (all events)
- Thread-safe, non-blocking publish

## Cognitive Stabilization — COMPLETE (Phase 23)

The semantic organism is now continuously cognitive, not reactive.

### BackgroundMetabolismService
IHostedService running every 5 minutes:
1. SemanticDeduplicator — prevents wiki rot
2. SalienceScorer — time decay for all nodes
3. DriftDetector — contradiction detection
4. ContradictionDetector — behavioral intelligence
5. ArchiveManager — archives stale nodes
6. InterventionGenerator — proactive guidance
7. EventBus — emits events

### Behavioral Intelligence (The Moat)
ContradictionDetector compares declared intent vs observed behavior:
- **GoalActivityGap** — Goal fading while unrelated activity high
- **PriorityDrift** — Declared priorities not reflected in behavior
- **AbandonedCommitment** — Commitment with no follow-through
- **IdentityBehaviorGap** — Identity claims not supported by behavior

### Retrieval Hygiene
RetrievalBudgetManager prevents prompt entropy explosion:
- 2000 token budget for retrieved context
- Scores by salience (0.4), recency (0.3), relevance (0.3)
- Max 10 nodes, 3 facts per node
- Compresses context when budget exceeded

### Intervention Engine
InterventionGenerator creates proactive guidance:
- "You said this deadline mattered, but no related activity has occurred in 5 days."
- "You repeatedly mention wanting deep work, but your timeline shows constant context switching."
- Pattern detection: synthesizes multiple contradictions into higher-level alerts

## Behavioral Reality Validation — COMPLETE (Phase 24, Sprint 7)

The organism now proves it understands reality correctly instead of hallucinating patterns from noise.

### Key Architectural Change
EnvironmentModel now takes `IBehavioralModeStrategy` — behavioral mode detection is injectable, testable, and replayable. This makes the entire perception layer verifiable.

### Perception Replay System
- **PerceptionEventRecorder** — captures perception events as immutable snapshots
- **PerceptionReplayEngine** — deterministic replay through any strategy
- **InterpretationComparator** — A/B comparison, systematic error detection

### Truth-Preserving Infrastructure
- **InterpretationAccuracyTracker** — records what Engram concluded vs what was true
- **FalsePatternDetector** — anti-overinterpretation (research≠procrastination, etc.)
- **TruthCalibrationStore** — persistent human corrections

### Cognitive Restraint
9 gates controlling when Engram should speak:
1. Confidence threshold
2. Silence threshold
3. Flow state protection (don't interrupt deep work)
4. Accuracy gate (stay silent on unreliable modes)
5. Over-interpretation gate
6. Frequently corrected gate
7. Category ignored gate
8. Intervention fatigue gate
9. Consecutive suppression release

### Timeline Semantics
Transforms event history into life continuity:
- Sessions (contiguous activity periods)
- Arcs (multi-session efforts)
- Momentum (building vs fading)
- Regressions (previously active modes becoming rare)

## Longitudinal Human Reality Testing — COMPLETE (Phase 25, Sprint 8)

Infrastructure for real-world validation over weeks of actual human behavior.

### Narrative Drift Auditor
Weekly self-model reality check:
- Goal alignment (active goals vs total goals)
- Priority alignment (priorities with recent activity)
- Freshness (average days since last touch)
- Coherence (nodes with links vs total)
- Trend tracking (improving/stable/degrading)

### Intervention Fatigue Tracker
Measures user response to interventions:
- Dismissal rate, ignore rate, action rate
- Fatigue score (weighted: dismissals 0.4, ignores 0.3, low action 0.3)
- Per-category breakdown
- ShouldReduceFrequency() gate

### Memory Pollution Detector
Detects graph degradation:
- Stale nodes (>30 days), orphaned nodes (no links)
- Overrepresented types, retrieval loops
- Prune candidates

### Semantic Compressor
Analyze-only compression recommendations:
- Prune, merge, archive, abstraction candidates
- Does NOT modify graph — only reports

### Ambiguity Tolerance Engine
Teaches Engram to say "I don't know":
- 4 ambiguity signals (low confidence, close competition, multiple candidates, negative-default bias)
- 5 ambiguity levels → 5 actions
- IsOverConfident() — detects systems that NEVER say "I don't know"

## Known Issues

1. **Windows Defender** — May quarantine unsigned binaries. Need code signing for production.
2. **Vulkan on clean machines** — BackendProbe + VerdictStore handle graceful CPU fallback.
3. **Frontend system prompt awareness** — The frontend doesn't show what context the model has about the user.

## Decisions (68+)

D-001..D-063: Earlier phases
D-088: OperationalWorldModel as central live state with event-driven change propagation
D-089: WorkflowCheckpoint serialized to JSON for durable pause/restore across process restarts
D-090: ActionRuntime.Pause() cancels active token to exit execution loop cleanly (no blocking wait)
D-091: ProceduralMemoryEngine persisted to .engram/automation/procedural_memory.json
D-092: SandboxManager path whitelist enforced at dispatch time, not at file operation time
D-093: AgentOrchestrator uses per-agent mutex to prevent overlapping task dispatch
D-094: CollaborationEngine creates pending query entries surfaced via Minimal API for human resolution
