# Roadmap: Engram

## Phases

- [x] **Phase 1: Repository and Runtime Foundation** [FREE] — 2026-05-13
- [x] **Phase 2: Immutable Raw Event Store** [FREE] — 2026-05-13
- [x] **Phase 3: Local Ingestion MVP** [FREE] — 2026-05-13
- [x] **Phase 4: Markdown Wiki Memory** [FREE] — 2026-05-13
- [x] **Phase 5: Desktop Shell + Search + Briefs** [FREE] — 2026-05-17
- [x] **Phase 6: Identity Hardening + Discovery UI** [FREE] — 2026-05-17
- [x] **Phase 7: The Embodied Execution Megaphase** [CRITICAL] — 2026-05-22
  - [x] **Phase 7A: Action Graph Foundation** — make execution deterministic and inspectable
  - [x] **Phase 7B: Task Planner** — convert natural language into executable operational graphs
  - [x] **Phase 7C: Desktop Operator** — give Engram hands
  - [x] **Phase 7D: Browser Agent Runtime** — give Engram web navigation capability
  - [x] **Phase 7E: State Verification** — prevent hallucinated success
  - [x] **Phase 7F: Recovery & Rollback** — survive environmental chaos
  - [x] **Phase 7G: Execution Memory** — operational continuity
  - [x] **Phase 7H: Execution Safety** — prevent catastrophic autonomy
  - [x] **Phase 7I: Live Execution UX** — maintain user trust during operation
  - [x] **Phase 7J: Operational Cognition** — merge cognition with action
- [x] **Phase 8: Executional World Model & Autonomous Workflows** [CRITICAL] — 2026-05-22
- [x] **Phase 9: Execution Reliability & Embodied Operation** [CRITICAL] — 2026-05-22
- [ ] **Phase 10: Unified Semantic World Model** [CRITICAL]
  - [ ] **Phase 10A: Unified Entity Graph** — Merge wiki, operational, workflow, browser, and filesystem entities into one canonical semantic identity system
  - [ ] **Phase 10B: Cross-Modal Identity Resolution** — Resolve references (PDFs, browser tabs, conversations, timelines) to their single project entities
  - [ ] **Phase 10C: Temporal World Fusion** — Build a continuous lived timeline merging browsing, cognition, and workflows into a persistent semantic chronology
  - [ ] **Phase 10D: Attention & Salience Unification** — Orchestrate global semantic focus, determining what is active, stale, or needs intervention across all domains
  - [ ] **Phase 10E: Semantic Scene Construction** — Synthesize the user's active context into living semantic scenes (coding session, burnout spiral, etc.)
  - [ ] **Phase 10F: Cross-System Memory Propagation** — Automate seamless memory and insight propagation between workflows, browser, cognition, and timelines
  - [ ] **Phase 10G: Global Consistency Engine** — A coherence immune system that prevents duplicate entities, contradictions, and narrative drift
- [ ] **Phase 11: Human Trust & Coexistence** [CRITICAL] — Explainability, privacy maps, memory editing, trust pacing, and ambient cognition restraint
- [ ] **Phase 12: Longitudinal Endurance** [CRITICAL] — Month-scale runtime, semantic compaction, graph entropy control, memory pollution resistance, and operational fatigue defense

## Current State

- Phases 1-9 implemented and verified
- Active development completed for Phase 9 (Execution Reliability & Embodied Operation)
- 1544 store tests + 88 API tests passing (1632/1632 total tests green)
- 97 API endpoints (Phase 8 added 8 endpoints), all connected to frontend
- 10 frontend views
- Desktop app built (Tauri + React)
- NSIS installer (~77MB) with self-contained .NET
- Runtime survivability FIXED (100/100 soak test)
- Engram system prompt ACTIVE (user identity + wiki context)
- Diagnostics export + post-install validation script
- **Chat is now the intent interface into the semantic operating system**
- **Background metabolism runs every 5 minutes**
- **Behavioral intelligence detects contradictions between goals and behavior**
- **Perception replay system makes interpretations verifiable**
- **Cognitive restraint engine prevents intervention fatigue**
- **Human truth calibration keeps interpretations grounded in reality**
- **Operational world model tracks live execution state with event-driven updates**
- **Long-running workflows with pause/resume checkpointing**
- **Procedural memory learns and recalls execution patterns**
- **Multi-agent orchestration routes tasks to specialized agents**
- **Strict UI embodiment abstraction (IUiEmbodimentProvider) and Windows dynamic COM UI automation**
- **Execution trust tiers and category-bounded permissions to prevent unauthorized/destructive actions**
- **Coexistence safety controls (rate limiting, foreground sovereignty yielding, directory containment zones)**

## Architecture State

```
User Message
  → IntentClassifier (7 types)
  → TaskRouter (central nervous system)
  → SemanticSearchEngine + WikiNodeStore + IdentityStore
  → PromptAssembler + RetrievalBudgetManager
  → LLM Reasoning
  → Response + Memory Extraction → WikiMetabolizer → EventBus

Background (every 5 minutes):
  → SemanticDeduplicator (prevent wiki rot)
  → SalienceScorer (time decay)
  → ContradictionDetector (behavioral intelligence)
  → ArchiveManager (archive stale nodes)
  → InterventionGenerator (proactive guidance)
  → EventBus (emit events)

Autonomous Execution (Phase 8):
  → OperationalWorldModel (live execution state)
  → WorkflowRuntime (long-running workflows with checkpoint/restore)
  → ExecutionReasoningEngine (observe → reason → adapt loop)
  → ProceduralMemoryEngine (learned execution patterns)
  → CollaborationEngine (human-in-the-loop gates)
  → OperationalAttentionOrchestrator (context focus management)
  → ToolAbstractionLayer (semantic high-level capabilities)
  → EnvironmentalResilienceEngine (popup/offline/sleep handling)
  → SandboxManager (filesystem safety + dry-run simulation)
  → AgentOrchestrator (multi-agent task routing)
```

## What's Next

### Priority 1: Production Deployment

1. **Code signing** — Sign the installer to prevent Windows Defender quarantine
2. **Auto-update mechanism** — Tauri has built-in updater support
3. **Crash reporting** — Capture and report sidecar crashes
4. **Telemetry opt-in** — Anonymous usage stats for debugging

### Priority 2: Semantic Coherence — ADDRESSED

Phase 7 (Behavioral Reality Validation) directly addressed this:
- Perception replay makes interpretations verifiable
- False pattern detection prevents over-interpretation
- Human truth calibration grounds interpretations in reality
- Cognitive restraint prevents intervention fatigue
- Timeline semantics provide life continuity

### Priority 3: Intervention Refinement — NEXT

1. **Intervention accuracy** — Track which interventions users act on
2. **Intervention timing** — Don't interrupt focus, deliver at natural breaks
3. **Intervention personalization** — Learn what types of interventions work
4. **Intervention fatigue** — Don't overwhelm with too many alerts (CognitiveRestraintEngine addresses this)

### Priority 4: Memory Quality

1. **Fact verification** — Cross-reference facts across sources
2. **Source confidence** — Weight facts by source reliability
3. **Memory consolidation** — Merge related facts over time
4. **Memory forgetting** — Intentional forgetting of irrelevant details

### Priority 5: Platform Expansion (DEFERRED)

1. **macOS support** — Tauri supports macOS, need LLamaSharp backend
2. **Linux support** — Tauri + LLamaSharp work on Linux
3. **Mobile companion** — iOS/Android app for quick capture

## Key Decisions

- D-064: Intent classification via regex/heuristic (no LLM dependency)
- D-065: TaskRouter as central nervous system
- D-066: ConversationMemoryPipeline as fire-and-forget
- D-067: EventBus as in-memory pub/sub (no external dependencies)
- D-068: BackgroundMetabolismService as IHostedService (5min cycle)
- D-069: SemanticDeduplicator with 0.7 similarity threshold
- D-070: ContradictionDetector for behavioral intelligence
- D-071: RetrievalBudgetManager with 2000 token budget
- D-072: InterventionGenerator with configurable threshold
- D-073: IBehavioralModeStrategy for injectable mode detection
- D-074: PerceptionSnapshot as immutable record (input + interpretation + sequence)
- D-075: CognitiveRestraintEngine with 9 restraint gates
- D-076: TruthCalibrationStore for persistent human corrections
- D-077: FalsePatternDetector for anti-overinterpretation
- D-078: TimelineSemanticsEngine for sessions/arcs/momentum/regressions
- D-079: NarrativeDriftAuditor for weekly self-model reality check
- D-080: InterventionFatigueTracker for user response measurement
- D-081: AmbiguityToleranceEngine for 'I don't know' infrastructure
- D-082: SemanticCompressor analyze-only mode (report, don't modify)
- D-083: Action Graph DAG representation for deterministic, cycle-free task steps
- D-084: Win32 SendInput simulator with strict bounds and failsafe mechanisms
- D-085: IBrowserDriver abstraction using Playwright for web environment operations
- D-086: LIFO recovery and rollback cascade for step-wise environment cleanup
- D-087: Host-level ExecutionSafetyManager for process and URL domain blacklisting
- D-088: OperationalWorldModel as central live state with event-driven change propagation
- D-089: WorkflowCheckpoint serialized to JSON for durable pause/restore across process restarts
- D-090: ActionRuntime.Pause() cancels active token to exit execution loop cleanly (no blocking wait)
- D-091: ProceduralMemoryEngine persisted to .engram/automation/procedural_memory.json
- D-092: SandboxManager path whitelist enforced at dispatch time, not at file operation time
- D-093: AgentOrchestrator uses per-agent mutex to prevent overlapping task dispatch
- D-094: CollaborationEngine creates pending query entries surfaced via Minimal API for human resolution

## Anti-Patterns (DO NOT BUILD)

- ❌ MCP integration
- ❌ Multi-agent systems
- ❌ Browser-use hype
- ❌ Cloud sync
- ❌ Voice assistants
- ❌ Mobile apps
- ❌ Giant vector databases
- ❌ Autonomous internet agents

**Reason:** The bottleneck is semantic coherence, not features. Adding more features before the existing system is coherent will create more fragmentation.
