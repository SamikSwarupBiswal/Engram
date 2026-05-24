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
- [x] **Phase 10: Unified Semantic World Model** [CRITICAL] — 2026-05-22
  - [x] **Phase 10A: Unified Entity Graph** — Merge wiki, operational, workflow, browser, and filesystem entities into one canonical semantic identity system
  - [x] **Phase 10B: Cross-Modal Identity Resolution** — Resolve references (PDFs, browser tabs, conversations, timelines) to their single project entities
  - [x] **Phase 10C: Temporal World Fusion** — Build a continuous lived timeline merging browsing, cognition, and workflows into a persistent semantic chronology
  - [x] **Phase 10D: Attention & Salience Unification** — Orchestrate global semantic focus, determining what is active, stale, or needs intervention across all domains
  - [x] **Phase 10E: Semantic Scene Construction** — Synthesize the user's active context into living semantic scenes (coding session, burnout spiral, etc.)
  - [x] **Phase 10F: Cross-System Memory Propagation** — Automate seamless memory and insight propagation between workflows, browser, cognition, and timelines
  - [x] **Phase 10G: Global Consistency Engine** — A coherence immune system that prevents duplicate entities, contradictions, and narrative drift
- [x] **Phase 11: Human Trust & Coexistence** [CRITICAL] — Explainability, privacy maps, memory editing, trust pacing, and ambient cognition restraint
- [x] **Phase 12: Longitudinal Endurance & Entropy Resistance** [CRITICAL] — 2026-05-23
  - [x] **Phase 12A: Semantic Entropy Resistance** — Prevent graph inflation, salience sludge, contradiction accumulation, stale narratives, and attention saturation.
  - [x] **Phase 12B: Longitudinal Trust Stability** — Prevent autonomy creep, permission inflation, intervention fatigue, and governance erosion.
  - [x] **Phase 12C: Memory Ecology Management** — Implement automatic compaction, archival, salience pruning, dormant entity compression, and contradiction expiration.
  - [x] **Phase 12D: Operational Fatigue Systems** — Prevent retry obsession, intervention persistence, narrative fixation, and workflow haunting.
  - [x] **Phase 12E: Multi-Month Runtime Survival** — Implement corruption recovery, graph reconciliation, replay validation, causal continuity repair, and operational archaeology integrity.
  - [x] **Phase 12F: Cognitive Homeostasis** — Implement dynamic stabilization, equilibrium enforcement, semantic load balancing, emotional neutrality preservation, and activation damping.
  - [x] **Phase 12G: Longitudinal Human Adaptation** — Enable calmer, non-intrusive operations and learning restraint patterns without accumulating overconfidence.
- [x] **Deployment Era (D1-D6)** [CRITICAL]
  - [x] **Phase D1: Reality Hardening** — Installer resilience, Windows dynamic environments, sleep/wake, driver fallbacks, anti-virus.
  - [x] **Phase D2: Human Coexistence Validation** — Intervention fatigue, trust pacing, annoyance archaeology, workflow frustration, autonomy discomfort, semantic creep.
  - [x] **Phase D3: Long-Horizon Soak Operation** — Virtual 30-day time-warp simulation, auto-expiry of active contradictions, protected islands archival shield, and ecological health telemetry metrics — 2026-05-24
  - [x] **Phase D4: Real Task Execution Validation** — Browser/desktop action resilience, coordination, recovery from interruption, chaotic desktop adaptation.
  - [ ] **Phase D5: Productization** — Onboarding, UX, packaging, privacy, permission transparency.
  - [x] **Phase D6: Real Human Coexistence & Field Validation** — Dynamic autonomy modulation, decay engine, domain ceilings, neutral failure narratives, and psychology telemetry.


## Current State

- Phases 1-12, D1-D4, and D6 implemented and verified
- Active development completed for Phase D5 (Productization)
- 1663 store tests + 88 API tests passing (1751/1751 total tests green)
- 97 API endpoints, all connected to frontend
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
- **Metabolic homeostasis and cognitive debt priority stacks balance resource loads**
- **Causal reconciler heals uncommitted startup WAL fractures**
- **Automatic daily compressed metadata backups preserve history**
- **Governance pacing token-bucket limits and friction-scaled intervention thresholds preserve focus**

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
  → HomeostasisController (metabolic resource triage, cognitive debt resolution)
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

## What's Next: Existential Validation Era (D1-D5)

All remaining roadmap work is focused on robustness, coexistence, endurance, execution validation, and productization to ensure Engram survives real-world Windows environments and users.

### Phase D1: Reality Hardening (Completed)
1. **Installer Resilience** — Handle Windows path variables, administrator/standard privileges, and user profile variations.
2. **Windows Edge Cases** — Multi-monitor configurations, DPI scaling, registry access errors.
3. **OS State Transitions** — Handle system sleep, hibernation, wake cycles, and unexpected power loss.
4. **Environment Divergence** — Browser profiling, anti-virus interference, permissions chaos, and fallback execution strategies.

### Phase D2: Human Coexistence Validation (Completed)
1. **Intervention Fatigue** — Gauge frequency of passive/active intervention dispatches and refine the friction-scaled restraint thresholds.
2. **Annoyance Archaeology** — Monitor Swift cancels and UI dismissals to track user annoyance and trust drift.
3. **Autonomy Discomfort** — Verify pacing constraints to ensure Engram does not context-switch or alert the user aggressively.

### Phase D3: Long-Horizon Soak Operation (Completed)
1. **Long-Horizon Run** — Weeks-long continuous background operational simulation.
2. **Semantic Accumulation** — Analyze entity graph scaling, link density decay, and WAL propagation sludge over time.
3. **Homeostasis Maintenance** — Validate priority stacks and metabolic resource triage under prolonged low-to-high activity cycles.

### Phase D4: Real Task Execution Validation (Completed)
1. **Task Robustness** — Ensure end-to-end task completion (e.g., generating research reports, browser data extraction, file curation) on messy desktops.
2. **Interruption Recovery** — Gracefully pause automation steps when users hijack input or focus and resume safely.

### Phase D5: Productization
1. **Packaging & Security** — Formally sign binaries, configure secure auto-update mechanisms, and enforce localized privacy walls.
2. **Governance UI** — Expose permission dashboards, explainability traces, and correction interfaces to the user clearly.

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
- D-095: Virtual Time Providers (`Func<DateTimeOffset>` clocks) enable temporal simulation testing without physical sleep delays
- D-096: Contradiction Expiry enforces auto-suppression of behavioral contradiction entries older than 14 days
- D-097: Protected Islands Archival Shield prevents system-level archival/decay of critical User Identity, Goal, and Project nodes

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
