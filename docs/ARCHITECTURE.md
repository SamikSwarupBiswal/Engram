# Engram Architecture

High-level architecture overview for the Engram personal semantic operating layer.

## Design Principles

1. **Local-first:** All data lives on the user's machine. Cloud is opt-in and audited.
2. **Append-only history:** Raw events are immutable. No destructive edits.
3. **Source-linked memory:** Every wiki fact traces back to raw event evidence.
4. **Replaceable providers:** OCR, models, browser automation, and workspace connectors sit behind interfaces.
5. **Consent-driven:** Sensitive capture sources are disabled by default. Excluded apps are never captured.
6. **Continuously cognitive:** Background metabolism runs every 5 minutes. Engram is not reactive.
7. **Intent-driven chat:** The chat is the intent interface into the semantic operating system, not a generic chatbot.
8. **Epistemically restrained:** Confidence-weighted, counter-evidence-aware, narrative-diverse cognition.
9. **Psychologically sustainable:** Tone regulation, positive evidence, curiosity, user agency protection.
10. **Privacy-first perception:** Maximum semantic usefulness with minimum invasiveness.
11. **Truth-preserving cognition:** Interpretations must be verifiable, correctable, and restrained.

## Layer Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    User Surfaces                         │
│  Engram.App (Tauri + React)  │  Chat (Intent Interface) │
├─────────────────────────────────────────────────────────┤
│                    Orchestration Layer                    │
│  IntentClassifier → TaskRouter → Subsystem Retrieval    │
│  PromptAssembler (RetrievalBudgetManager)                │
├─────────────────────────────────────────────────────────┤
│                    Cognitive Stabilization Layer          │
│  ReflectionConfidenceModel │ IdentityStabilityEngine    │
│  NarrativeBalanceController │ CounterEvidenceDetector   │
│  NarrativeInterpretationEngine │ SemanticHealthMonitor   │
├─────────────────────────────────────────────────────────┤
│                    Behavioral Reality Validation Layer    │
│  PerceptionEventRecorder │ PerceptionReplayEngine       │
│  InterpretationComparator │ InterpretationAccuracyTracker│
│  FalsePatternDetector │ TruthCalibrationStore            │
│  CognitiveRestraintEngine │ TimelineSemanticsEngine     │
├─────────────────────────────────────────────────────────┤
│                    Human-Compatible Cognition Layer       │
│  ToneBalanceEngine │ MomentumDetector │ CuriosityEngine │
│  InterventionConsentModel │ ReflectionExpiryEngine      │
├─────────────────────────────────────────────────────────┤
│                    Intelligence Layer                     │
│  BackgroundMetabolismService (IHostedService, 5min)      │
│  SemanticDeduplicator │ ContradictionDetector            │
│  SalienceScorer │ DriftDetector │ ArchiveManager         │
│  InterventionGenerator │ InterventionStore              │
│  ContradictionHistoryStore │ TensionEvolutionEngine      │
├─────────────────────────────────────────────────────────┤
│                    Perception Layer                       │
│  ActiveWindowService │ FileWatcherService               │
│  EnvironmentModel (IBehavioralModeStrategy) │ PerceptionDashboard │
│  VisualPerceptionPipeline │ OcrService                  │
│  BehavioralModeStrategy │ PerceptionEventRecorder       │
├─────────────────────────────────────────────────────────┤
│                    Memory Layer                          │
│  ConversationMemoryExtractor → WikiMetabolizer           │
│  WikiNodeStore │ SemanticSearchEngine                    │
│  EventBus │ TimelineSubscriber                           │
├─────────────────────────────────────────────────────────┤
│                    Ingestion Layer                        │
│  ClipboardWatcher │ ActiveWindowTracker │ FileWatcher    │
│  OcrService │ GoogleWorkspaceManager                     │
├─────────────────────────────────────────────────────────┤
│                    Inference Layer                        │
│  InferenceRouter │ LocalInferenceEngine │ CloudPipeline  │
│  InferenceLifecycleManager │ GpuDetector                 │
├─────────────────────────────────────────────────────────┤
│                    Storage Layer                         │
│  .engram/raw/ (immutable events)                         │
│  .engram/wiki/ (metabolized memory)                      │
│  .engram/config/ (identity, priorities, anti-goals)      │
│  .engram/config/contradiction_history.json               │
│  .engram/config/interventions.json                       │
│  .engram/config/intervention_consent.json                │
│  .engram/config/perception_config.json                   │
│  .engram/archives/ (decayed nodes)                       │
└─────────────────────────────────────────────────────────┘
```

## Data Flow

```
[User Message]
      ↓
[IntentClassifier] → 7 intent types
      ↓
[TaskRouter] → route to subsystem
      ↓
[SemanticSearchEngine] + [WikiNodeStore] + [IdentityStore]
      ↓
[PromptAssembler] + [RetrievalBudgetManager]
      ↓
[LLM Reasoning] → Response
      ↓
[ConversationMemoryExtractor] → [WikiMetabolizer] → [WikiNodeStore]
      ↓
[EventBus] → [TimelineSubscriber] → [.engram/raw/]

--- Background (every 5 minutes) ---

[BackgroundMetabolismService]
      ↓
[SemanticDeduplicator] → merge duplicates
      ↓
[SalienceScorer] → time decay
      ↓
[ContradictionDetector] → behavioral intelligence
      ↓
[ArchiveManager] → archive stale nodes
      ↓
[InterventionGenerator] → proactive guidance
      ↓
[EventBus] → emit events
```

## Chat Pipeline (Intent Interface)

The chat is NOT a generic chatbot. It's the intent interface into the semantic operating system.

```
User: "What do you know about my startup?"
      ↓
IntentClassifier → MemoryQuery (confidence: 0.85)
      ↓
TaskRouter → HandleMemoryQuery()
      ↓
SemanticSearchEngine.Search("startup") → retrieve relevant wiki nodes
      ↓
PromptAssembler → assemble contextual system prompt with retrieved nodes
      ↓
LLM → reason over memory graph
      ↓
Response + Memory Extraction → WikiMetabolizer → wiki nodes
```

### Intent Types

| Intent | Example | Subsystem |
|--------|---------|-----------|
| MemoryQuery | "What do you know about..." | SemanticSearchEngine |
| TimelineQuery | "What was I doing..." | WikiNodeStore (recent) |
| DriftAnalysis | "Am I making progress?" | ContradictionDetector |
| StateSynthesis | "What matters most?" | IdentityStore + WikiNodeStore |
| ResearchTask | "Find the best..." | ResearchAgent |
| AutomationTask | "Open VSCode..." | ActionExecutor |
| Conversational | "Hello" | PromptAssembler |

## Background Metabolism (The Brain)

The BackgroundMetabolismService runs every 5 minutes as an IHostedService:

```
Cycle:
1. Load all wiki nodes
2. SemanticDeduplicator → merge duplicates (prevent wiki rot)
3. Reload after dedup
4. SalienceScorer → recompute salience (time decay)
5. DriftDetector → detect contradictions
6. ContradictionDetector → behavioral intelligence
   - GoalActivityGap (goal fading while unrelated activity high)
   - PriorityDrift (declared priorities not reflected in behavior)
   - AbandonedCommitment (no follow-through)
   - IdentityBehaviorGap (identity claims not supported by behavior)
7. ArchiveManager → archive stale nodes (salience < 0.1)
8. Generate tension reports
9. InterventionGenerator → proactive guidance
10. EventBus → emit events
```

## Local Store Layout (.engram/)

```
.engram/
├── raw/              # Immutable event history
│   └── YYYY-MM-DD/   # Daily partitions
│       └── [event_id].json
├── wiki/             # Metabolized Markdown memory
│   ├── *.md          # Entity nodes (people, projects, goals)
│   ├── index.md      # Navigation map
│   ├── user_identity.md
│   ├── priorities.md
│   └── anti_goals.md
├── runs/             # Agent run logs
│   └── [run_id]/
│       ├── log.md
│       └── state.json
├── config/           # Local configuration
├── logs/             # Service and diagnostic logs
└── archives/         # Decayed/stale wiki nodes
```

## Project Layout (src/)

| Project | Purpose | Phase |
|---------|---------|-------|
| Engram.Store | Core library (all logic) | 1+ |
| Engram.Store/Events/ | EventBus, TimelineSubscriber | 22 |
| Engram.Store/Memory/ | ConversationMemoryExtractor, Pipeline, PromptAssembler | 22 |
| Engram.Store/Metabolism/ | BackgroundMetabolismService, Deduplicator, ContradictionDetector, RetrievalBudgetManager, InterventionGenerator, InterventionStore, ContradictionHistoryStore, TensionEvolutionEngine, ContradictionResolutionDetector | 23, Sprint 3 |
| Engram.Store/Metabolism/ | ReflectionConfidenceModel, IdentityStabilityEngine, NarrativeBalanceController, CounterEvidenceDetector, NarrativeInterpretationEngine, SemanticHealthMonitor | Sprint 4 |
| Engram.Store/Metabolism/ | ToneBalanceEngine, MomentumDetector, CuriosityEngine, InterventionConsentModel, ReflectionExpiryEngine, CognitiveRestraintEngine | Sprint 5, Sprint 7 |
| Engram.Store/Orchestration/ | IntentClassifier, TaskRouter | 22 |
| Engram.Store/Search/ | TF-IDF, SemanticSearchEngine, BriefGenerator | 5, 22 |
| Engram.Store/Wiki/ | WikiNodeStore, Metabolizer, Serializer | 4 |
| Engram.Store/Identity/ | User profile, discovery, intervention | 6 |
| Engram.Store/Salience/ | Decay scoring, drift detection | 7 |
| Engram.Store/Inference/ | LLamaSharp, GPU detection, model mgmt, KV lifecycle | 11 |
| Engram.Store/Agent/ | Research agent, browser, citations | 14 |
| Engram.Store/Automation/ | Action executor, permission gate | 15 |
| Engram.Store/Security/ | Encryption, export, delete, sync | 16 |
| Engram.Store/Perception/ | Screen capture, OCR, ActiveWindowService, FileWatcherService, EnvironmentModel, PerceptionDashboard, BehavioralModeStrategy, PerceptionEventRecorder, PerceptionReplayEngine, InterpretationComparator, InterpretationAccuracyTracker, FalsePatternDetector, TruthCalibrationStore, TimelineSemanticsEngine | 17, Sprint 6, Sprint 7 |
| Engram.Store/Cloud/ | Cloud pipeline, providers, audit | 8 |
| Engram.Store/Billing/ | Token budget, pricing | 9 |
| Engram.Store/Google/ | Gmail, Calendar, Drive metadata | 13 |
| Engram.Store/Capture/ | Event capture (clipboard, files, windows) | 3 |
| Engram.Cli | Developer CLI | 1 |
| Engram.Api | ASP.NET Minimal API (sidecar) | 1 |
| Engram.App | Tauri + React frontend | 10 |

## Provider Interfaces

All external systems sit behind interfaces:

- **IOcrProvider** — Windows Copilot Runtime first, dev fallback
- **ILocalModelProvider** — Phi/Copilot Runtime, mock for CI
- **ICloudModelProvider** — Gemini/Claude behind routing interface
- **IBrowserAutomationProvider** — Playwright first, Computer Use API later
- **IWorkspaceProvider** — Google Workspace first, M365 later

## Compute Tiering

| Layer | Type | Purpose |
|-------|------|---------|
| Perception | Local SLM | "What is on screen right now?" |
| Reasoning | Cloud VLM | "Why does this matter to the user?" |
| Drift Engine | Hybrid | Compare live behavior to priorities |
| Intervention | Local | Decide notification vs card delivery |

## Security Model

- AES-256 encryption at rest (Phase 12)
- Consent defaults: all sensitive capture disabled
- No raw screenshots/clipboard/email sent to cloud by default
- Cloud calls: audited with reason, provider, payload summary, cost
- Automation: read-only first, approval required for risky actions
- Export/delete: user can purge all Engram data
