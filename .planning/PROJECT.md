# Engram

## Vision

Engram is a Windows-first personal semantic operating layer. It turns a user's local digital activity into a durable, source-linked memory layer that can be searched, briefed, and eventually used by agents to help with research and operating-system tasks.

Engram ships as a downloadable desktop application with a ChatGPT-like conversational GUI. On first launch, it runs a Discovery Skill interview to build a personalized user profile. Free tier runs 100% locally on the user's NPU. Pro tier adds cloud-enhanced intelligence via managed credit pooling — no user API keys.

**THE CHAT IS NOT A CHATBOT.** It's the intent interface into the semantic operating system. Every message is classified by intent, routed to the appropriate subsystem, and responded to with contextual memory retrieval.

## Architecture

Engram is a **desktop app**, not a web app. Tauri v2 (Rust) shell auto-spawns a .NET 8 API sidecar as a child process on launch. The React frontend connects to the sidecar at 127.0.0.1:5000 internally. The user never sees the backend.

```
┌─────────────────────────────────────────────────────┐
│  Engram Desktop App (Tauri v2)                      │
│                                                     │
│  ┌─────────────────────────────────────────────┐    │
│  │  React + Tailwind + shadcn/ui               │    │
│  │  ChatGPT-style sidebar, 10 views            │    │
│  │  → connects to 127.0.0.1:5000               │    │
│  └──────────────────┬──────────────────────────┘    │
│                     │ HTTP                          │
│  ┌──────────────────┴──────────────────────────┐    │
│  │  .NET 8 API Sidecar (child process)         │    │
│  │  83 endpoints                               │    │
│  │  Engram.Store services                      │    │
│  │  BackgroundMetabolismService (5min cycle)    │    │
│  └─────────────────────────────────────────────┘    │
│                                                     │
│  Tauri spawns sidecar on app start                  │
│  Tauri kills sidecar on app close                   │
└─────────────────────────────────────────────────────┘
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Shell | Tauri v2 (Rust) — ~10MB, native Windows, system tray |
| UI | React 19 + TypeScript + Tailwind CSS |
| Backend | .NET 8 ASP.NET Minimal API (sidecar) |
| Local Store | .engram/ directory (JSON + Markdown) |
| Tests | xUnit (1721 tests total: 1633 Store, 88 API) |
| Build | Vite 6, cargo, dotnet publish |

## Onboarding Flow

1. User downloads Engram installer (.exe/.msi)
2. Installs to %LOCALAPPDATA%/Engram/
3. Opens from Start Menu / Desktop shortcut
4. Tauri shell starts → spawns .NET sidecar
5. Model auto-downloads on first launch (~2.3GB)
6. Discovery interview runs to build user profile
7. Chat becomes the intent interface into the semantic operating system

## Chat Pipeline

```
User Message
  → IntentClassifier (7 types)
  → TaskRouter (central nervous system)
  → SemanticSearchEngine + WikiNodeStore + IdentityStore
  → PromptAssembler + RetrievalBudgetManager
  → LLM Reasoning
  → Response + Memory Extraction → WikiMetabolizer → EventBus
```

## Background Metabolism

```
BackgroundMetabolismService (every 5 minutes)
  → SemanticDeduplicator (prevent wiki rot)
  → SalienceScorer (time decay)
  → ContradictionDetector (behavioral intelligence)
  → ArchiveManager (archive stale nodes)
  → InterventionGenerator (proactive guidance)
  → EventBus (emit events)
```

## Key Features

1. **Intent Classification** — 7 intent types detected from user messages
2. **Task Routing** — Central nervous system routes intents to appropriate subsystems
3. **Memory Pipeline** — Chat conversations automatically create wiki nodes
4. **Retrieval-Augmented Prompts** — Query-aware prompt assembly with budget management
5. **Event Bus** — Central event stream connecting all subsystems
6. **Background Metabolism** — Continuous cognitive loop running every 5 minutes
7. **Semantic Deduplication** — Prevents wiki rot by merging duplicate entities
8. **Behavioral Intelligence** — Detects contradictions between goals and behavior
9. **Intervention Engine** — Proactive guidance when contradictions detected
10. **Retrieval Hygiene** — Prevents prompt entropy explosion with budget management
11. **Operational World Model** — Live execution state tracking with event-driven updates
12. **Workflow Checkpointing** — Pause, save, and resume long-running workflows across restarts
13. **Procedural Memory** — Persistent learned patterns for reliable task re-execution
14. **Multi-Agent Orchestration** — Specialized agents coordinated via shared world model
15. **Execution Sandboxing** — Filesystem safety and dry-run simulation mode
16. **Human Collaboration Gates** — Pause execution and surface clarification requests via API
17. **UI Embodiment Abstraction** — Decouples cognitive planning from execution drivers via IUiEmbodimentProvider
18. **Execution Trust Tiers** — Restricts action scope using a multi-level safety grid
19. **Coexistence Safety Controls** — Limits rates, prevents directory violations, and respects foreground sovereignty

## Current State

- Phases 1-12, D1, D2, and D3 implemented and verified
- Active development completed for Phase D3 (Long-Horizon Soak Operation)
- 1633 store tests + 88 API tests passing (1721/1721 total tests green)
- 97 API endpoints (all connected to frontend)
- 10 frontend views
- Desktop app built (Tauri + React)
- NSIS installer (~77MB)
- Runtime survivability fixed (100/100 soak test)
- Chat is the intent interface into the semantic operating system
- Background metabolism runs every 5 minutes
- Behavioral intelligence detects contradictions
- **Operational world model tracks live execution context with event-driven state updates**
- **Long-running workflows can be paused, checkpointed, and restored across process restarts**
- **Procedural memory persists and replays learned execution patterns**
- **Multi-agent orchestration safely routes tasks to specialized agents**
- **Safety controls enforce rate limiting, containment zone whitelisting, and foreground sovereignty yields**
- **Windows dynamic COM UI Automation dynamically operates without platform compilation lock-in**
- **Safety constitution state machine enforces boundaries based on violation levels and writes SHA-256 tamper-evident logs**
- **Explainability narration turns causal reasoning traces into plain-text user explanations**
- **Memory sovereignty enforces deletion envelopes and handles user reality corrections**
- **Metabolic priority stack manages triage under load and runs deferred tasks (cognitive debt) when idle**
- **Token-bucket pacing controls intervention rates, scaling silence limits as user friction increases**
- **Causal startup reconciler repairs WAL sequence fractures to recover uncommitted writes**
- **Progressive containment states (D2) queue deferred mutations and back off when user activity is detected**
- **Yield-to-Focus multitasking gating (D2) prevents interruptions during deep work periods**
- **Virtual temporal simulation (D3) runs multi-week soak tests using configurable time providers**
- **Contradiction auto-expiry (D3) auto-suppresses contradictions older than 14 days**
- **Protected islands archival shield (D3) exempts core user identities and goal nodes from pruning**
- **Ecological health telemetry (D3) tracks metrics like annoyance, autonomy drift, and cognitive debt backlog**

## What's Next

1. **Existential Validation Era (D1-D5)** — Shift focus entirely from capability expansion to operational consolidation, hardening, coexistence validation, soak operations, task execution validation, and productization. Active phase: **D4 (Real Task Execution Validation)**.

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
