# Semantic Continuity: Making Engram Alive

## Diagnosis

Engram is ~55-65% behaviorally integrated. All core subsystems exist but operate as isolated microservices. The missing piece is **continuous orchestration** — the bridges that connect chat → memory → retrieval → cognition.

## Current State (What EXISTS)

| Subsystem | File | Status |
|-----------|------|--------|
| WikiMetabolizer | `src/Engram.Store/Wiki/WikiMetabolizer.cs` | ✅ Real — processes file_change, clipboard, email events |
| WikiNodeStore | `src/Engram.Store/Wiki/WikiNodeStore.cs` | ✅ Real — atomic read/write, thread-safe |
| SalienceScorer | `src/Engram.Store/Salience/SalienceScorer.cs` | ✅ Real — power law decay (static only) |
| DriftDetector | `src/Engram.Store/Salience/DriftDetector.cs` | ✅ Real |
| ArchiveManager | `src/Engram.Store/Salience/ArchiveManager.cs` | ✅ Real |
| RawEvent / RawEventWriter | `src/Engram.Store/RawEvent.cs` | ✅ Real — event_id, event_type, text, metadata |
| SearchEngine | `src/Engram.Store/Search/SearchEngine.cs` | ✅ Real — keyword TF-IDF only |
| IdentityStore | `src/Engram.Store/Identity/IdentityStore.cs` | ✅ Real — profile, goals, anti-goals, priorities |
| ResearchAgent | `src/Engram.Store/Agent/ResearchAgent.cs` | ✅ Real |
| ActionExecutor | `src/Engram.Store/Automation/ActionExecutor.cs` | ✅ Real |
| Chat endpoint | `Program.cs:391` | ✅ Real — `/v1/chat/completions` with system prompt |
| System prompt | `Program.cs:341-388` | ⚠️ Shallow — top 5 salient nodes, no query-relevant retrieval |

## What's MISSING (The Bridges)

### Bridge 1: Chat → Memory Extraction
**Problem:** Chat completions return responses but NEVER feed the metabolizer.
**Impact:** Engram talks but doesn't remember what was discussed.

### Bridge 2: Memory → Wiki Metabolism
**Problem:** `WikiMetabolizer.ExtractEntities()` only handles `file_change`, `clipboard`, `email`. No conversation entity extraction.
**Impact:** Conversations are ephemeral — they vanish.

### Bridge 3: Wiki Retrieval → Prompt Assembly
**Problem:** `BuildEngramSystemPrompt()` dumps top 5 salient nodes regardless of query relevance.
**Impact:** Phi behaves generic despite having rich wiki memory.

### Bridge 4: Event Bus
**Problem:** No central event stream. Each subsystem operates in isolation.
**Impact:** Timeline is dead. No automatic flow from capture → processing → wiki → retrieval.

### Bridge 5: Timeline Subscriptions
**Problem:** Events written to disk but no subscriber pattern.
**Impact:** Timeline shows raw events but nothing metabolizes them continuously.

### Bridge 6: Semantic Search
**Problem:** `SearchEngine` is pure keyword TF-IDF. No embeddings, no semantic similarity.
**Impact:** Search misses conceptually related content.

### Bridge 7: Background Metabolism
**Problem:** No continuous loop. All processing is request-driven.
**Impact:** Engram is dormant unless user explicitly interacts.

---

## Execution Plan (7 Tasks)

### TASK 1: ConversationMemoryExtractor
**File:** `src/Engram.Store/Memory/ConversationMemoryExtractor.cs`
**Purpose:** Extract structured memory candidates from chat messages.
**Extraction targets:** PERSON, PROJECT, GOAL, DECISION, PREFERENCE, ANXIETY, TASK
**Approach:** Regex + heuristic extraction (no LLM dependency for extraction itself)
**Tests:** `tests/Engram.Store.Tests/ConversationMemoryExtractorTests.cs`

### TASK 2: ConversationMemoryPipeline
**File:** `src/Engram.Store/Memory/ConversationMemoryPipeline.cs`
**Purpose:** Wire extracted memories → WikiMetabolizer.ProcessEvent()
**Pipeline:** conversation → extract → RawEvent → ProcessEvent → wiki node updates
**Integration:** Hook into chat endpoint after response
**Tests:** `tests/Engram.Store.Tests/ConversationMemoryPipelineTests.cs`

### TASK 3: PromptAssembler (Retrieval-Augmented)
**File:** `src/Engram.Store/Memory/PromptAssembler.cs`
**Purpose:** Query-aware prompt assembly using wiki retrieval
**Pipeline:** user message → query search → retrieve relevant nodes → assemble context → system prompt
**Replace:** Current `BuildEngramSystemPrompt()` with retrieval-aware version
**Tests:** `tests/Engram.Store.Tests/PromptAssemblerTests.cs`

### TASK 4: EventBus
**File:** `src/Engram.Store/Events/IEventBus.cs`, `EventEnvelope.cs`, `InMemoryEventBus.cs`
**Purpose:** Central event stream for all subsystems
**Events:** ChatCompleted, WikiNodeUpdated, CaptureDetected, AutomationExecuted, ResearchCompleted, DriftDetected
**Pattern:** Publish/subscribe with in-memory queue
**Tests:** `tests/Engram.Store.Tests/EventBusTests.cs`

### TASK 5: Timeline Subscriptions
**File:** `src/Engram.Store/Events/TimelineSubscriber.cs`
**Purpose:** Auto-subscribe to event bus, write to timeline
**Integration:** Wire into Program.cs startup
**Tests:** `tests/Engram.Store.Tests/TimelineSubscriberTests.cs`

### TASK 6: Semantic Search (Embeddings)
**File:** `src/Engram.Store/Search/SemanticSearchEngine.cs`, `EmbeddingService.cs`
**Purpose:** Hybrid search — keyword + vector similarity
**Approach:** Local embedding model (nomic-embed-text or bge-small-en) via ONNX Runtime
**Storage:** Embeddings persisted alongside wiki nodes
**Tests:** `tests/Engram.Store.Tests/SemanticSearchEngineTests.cs`

### TASK 7: Background Metabolism Loop
**File:** `src/Engram.Store/Metabolism/BackgroundMetabolismService.cs`
**Purpose:** Continuous processing loop (every N minutes)
**Pipeline:** process raw events → update wiki → re-score salience → detect drift → archive stale
**Integration:** Hosted service in Program.cs
**Tests:** `tests/Engram.Store.Tests/BackgroundMetabolismTests.cs`

---

## Architectural Rules

1. **No LLM dependency for extraction** — use regex/heuristic first, LLM enhancement later
2. **Event bus is in-memory** — no external dependencies (Redis, Kafka)
3. **Embeddings via ONNX Runtime** — local, no cloud dependency
4. **Background metabolism is a hosted service** — clean startup/shutdown
5. **All new code has tests** — production-grade from day one
6. **Never weaken existing tests** — if test fails, investigate root cause

## Success Criteria

After completing all 7 tasks:
- [ ] Chat conversations automatically create wiki nodes
- [ ] System prompt includes query-relevant wiki context
- [ ] Search returns semantically related results
- [ ] Events flow through central bus to all subscribers
- [ ] Timeline auto-populates from event stream
- [ ] Background metabolism runs continuously
- [ ] All existing tests still pass
- [ ] New tests cover all new functionality
