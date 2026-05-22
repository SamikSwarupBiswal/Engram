# Roadmap: Engram

## Phases

- [x] **Phase 1: Repository and Runtime Foundation** [FREE] — 2026-05-13
- [x] **Phase 2: Immutable Raw Event Store** [FREE] — 2026-05-13
- [x] **Phase 3: Local Ingestion MVP** [FREE] — 2026-05-13
- [x] **Phase 4: Markdown Wiki Memory** [FREE] — 2026-05-13
- [x] **Phase 5: Desktop Shell + Search + Briefs** [FREE] — 2026-05-17
- [x] **Phase 6: Identity Hardening + Discovery UI** [FREE] — 2026-05-17
- [/] **Phase 7: The Embodied Execution Megaphase** [CRITICAL] — 2026-05-22
  - [ ] **Phase 7A: Action Graph Foundation** — make execution deterministic and inspectable
  - [ ] **Phase 7B: Task Planner** — convert natural language into executable operational graphs
  - [ ] **Phase 7C: Desktop Operator** — give Engram hands
  - [ ] **Phase 7D: Browser Agent Runtime** — give Engram web navigation capability
  - [ ] **Phase 7E: State Verification** — prevent hallucinated success
  - [ ] **Phase 7F: Recovery & Rollback** — survive environmental chaos
  - [ ] **Phase 7G: Execution Memory** — operational continuity
  - [ ] **Phase 7H: Execution Safety** — prevent catastrophic autonomy
  - [ ] **Phase 7I: Live Execution UX** — maintain user trust during operation
  - [ ] **Phase 7J: Operational Cognition** — merge cognition with action
- [ ] **Phase 8: Cloud Reasoning + Token Billing** [PRO]
- [ ] **Phase 9: Google Workspace Metadata** [PRO]
- [ ] **Phase 10: Agentic Research Workflow** [PRO]
- [ ] **Phase 11: Computer-Use Automation** [PRO]
- [ ] **Phase 12: Encryption, Sync, Production Hardening** [PRO]
- [ ] **Phase 13: Visual Perception Pipeline** [PRO]
- [ ] **Phase 14: Quality Hardening** [PRO]
- [ ] **Phase 15: Runtime Survivability** [CRITICAL]
- [ ] **Phase 16: Packaged Product Validation** [CRITICAL]
- [ ] **Phase 17: Engram System Prompt** [CRITICAL]
- [ ] **Phase 18: Semantic Continuity** [CRITICAL]
- [ ] **Phase 19: Cognitive Stabilization** [CRITICAL]
- [ ] **Phase 20: Behavioral Reality Validation** [CRITICAL]
- [ ] **Phase 21: Longitudinal Human Reality Testing** [CRITICAL]

## Current State

- Phases 1-6 implemented and verified
- Active development returned to the expanded Phase 7: The Embodied Execution Megaphase
- 1367/1367 tests passing (C# test suite isolated and hardened)
- 83 API endpoints, all connected to frontend
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
