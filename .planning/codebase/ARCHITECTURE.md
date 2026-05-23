# Architecture

**Analysis Date:** 2026-05-23

## Pattern Overview

**Overall:** Tauri-wrapped Desktop Shell with a .NET 8 API Sidecar running local SLM inference.

**Key Characteristics:**
- **Local-First Sidecar Monolith:** All memory, ingestion, planning, and execution run on the user's machine via a background sidecar process.
- **Intent-Driven UI:** The Chat window acts as the command interface, converting natural language into actions or structured memory queries.
- **Continuous Cognitive Loop:** Not purely reactive; background metabolism runs constantly to maintain memory quality and detect behavioral drift.
- **Decoupled Embodiment:** Cognitive planning and reasoning are decoupled from native Windows/Browser execution via abstractions.

## Layers

**Shell Layer (Tauri / Rust):**
- Purpose: Application entry point, tray configuration, auto-updating, and lifecycle management.
- Contains: Native desktop integration, system tray controllers, sidecar spawn/kill logic.
- Location: `src/Engram.App/src-tauri/`
- Depends on: Operating System WebView2 and Process libraries.
- Used by: User launcher.

**UI Layer (React 19):**
- Purpose: Front-end presentation layer with 10 custom dashboards/views (Chat, Timeline, Wiki, settings, etc.).
- Contains: TypeScript components, Tailwind styles, state management hook interfaces, CopilotKit wrappers.
- Location: `src/Engram.App/src/`
- Depends on: Tauri IPC and HTTP bridge.
- Used by: Tauri Shell WebView2.

**API Layer (Minimal API / ASP.NET Web):**
- Purpose: Exposes HTTP endpoints representing cognitive and automated tasks.
- Contains: Endpoints definition, rate limit middleware, lifecycle handlers, logs exporter.
- Location: `src/Engram.Api/`
- Depends on: `Engram.Store` service layer.
- Used by: UI Layer (internal HTTP requests).

**Service / Store Layer (Engram.Store):**
- Purpose: The core logic monolith.
- Contains: Ingestion, local inference management, memory pipelines, automation runtimes, background metabolism host.
- Location: `src/Engram.Store/`
- Depends on: Native bindings (LLamaSharp, Playwright).
- Used by: API Layer, CLI Layer.

## Data Flow

### 1. Inbound User Message Pipeline (Intent Routing)
1. User enters a message in the Chat UI.
2. React UI fires a request to `/v1/chat/completions`.
3. `IntentClassifier` categorizes the message into one of 7 intents (e.g., `MemoryQuery`, `AutomationTask`).
4. `TaskRouter` dispatches the intent to the target subsystem.
5. If retrieval is required, `PromptAssembler` queries `SemanticSearchEngine`, `WikiNodeStore`, and `IdentityStore`.
6. `RetrievalBudgetManager` limits the query result to a 2000-token payload.
7. Local `LLamaSharp` context processes the prompt and streams the completion back.
8. Post-inference hook clears the KV cache (`KvCacheClear()`) to prevent resource exhaustion, extracts raw events, and publishes to the `EventBus` for the `WikiMetabolizer` to absorb.

### 2. Background Metabolism Service (Every 5 minutes)
1. `BackgroundMetabolismService` wakes up.
2. Runs `SemanticDeduplicator` to merge redundant nodes.
3. Runs `SalienceScorer` to decay weights of old/inactive nodes.
4. Runs `ContradictionDetector` to spot goal-behavior drift (e.g. priority drift, goal activity gaps).
5. Runs `ArchiveManager` to move expired nodes out of the active index.
6. Runs `InterventionGenerator` to issue proactive system alerts if behavioral contradictions exist.

### 3. Embodied Execution (Automation Run loop)
1. `TaskRouter` detects an `AutomationTask` and passes it to the `AgentOrchestrator`.
2. Orchestrator constructs an execution DAG using the `TaskPlanner`.
3. `WorkflowRuntime` runs the DAG:
   - *Observe:* Screen capture + layout parse via `Perception` layer.
   - *Reason:* Evaluate environment and target action.
   - *Adapt:* Adjust workflow or invoke rollback cascade if errors/popups are hit.
4. Execution is driven through `IBrowserDriver` (Playwright) or `IUiEmbodimentProvider` (Win32 COM Automation).
5. Results are logged, state is synced in `OperationalWorldModel`, and checkpoints are serialized to disk.

## Key Abstractions

- `IUiEmbodimentProvider` (`src/Engram.Store/Automation/`): Decouples planning from UI driver platforms (Windows automation vs direct browser interaction).
- `IBehavioralModeStrategy` (`src/Engram.Store/Reality/`): Injectable and replayable behavioral state machine detecting user modes (focus, distraction).
- `ICloudModelProvider` (`src/Engram.Store/Cloud/`): Interface for calling cloud providers under Pro tier constraints.
- `EventBus` (`src/Engram.Store/Events/`): Thread-safe pub/sub broker handling internal communication (e.g. notifying indexers of new wiki nodes).

## Error Handling

**Strategy:** Exception bubbling combined with LIFO (Last In First Out) recovery cascades.
- Store logic throws typed exceptions (e.g., `KvCacheException`, `ContainmentViolationException`).
- Minimal API catches exceptions at route boundaries, reporting errors via standardized response models.
- Execution steps are executed in a transaction-like manner: if an action step fails, a LIFO stack rolls back the environment (e.g., closing spawned processes or closing files).

---

*Architecture analysis: 2026-05-23*
*Update when major patterns change*
