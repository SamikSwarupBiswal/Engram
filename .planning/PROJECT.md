# Engram

## Vision

Engram is a Windows-first personal semantic operating layer. It turns a user's local digital activity into a durable, source-linked memory layer that can be searched, briefed, and eventually used by agents to help with research and operating-system tasks.

Engram ships as an installer-based desktop application with a ChatGPT-like conversational GUI. On first launch, it runs a Discovery Skill interview to build a personalized user profile. Free tier runs 100% locally on the user's NPU. Pro tier adds cloud-enhanced intelligence via managed credit pooling — no user API keys.

## Product Thesis

The first proof is not cloud automation. The first proof is that Engram can make personal context persistent, queryable, and auditable:

1. Capture events locally with explicit consent.
2. Store every source event immutably.
3. Metabolize raw events into Markdown wiki memory.
4. Make memory searchable and source-linked.
5. Detect drift between new events, stored facts, and stated priorities.

## Onboarding Flow

1. User downloads Engram installer (Windows .exe/.msi)
2. Installer installs: Engram application + local SLM (Phi-4 via Windows Copilot Runtime)
3. First launch → Discovery Skill activates
   - 15-minute AI-guided interview
   - Topics: anti-goals, comfort triggers, recurring anxieties
   - Output: `user.md` (identity profile stored in `.engram/`)
4. Engram enters Free tier by default
5. Conversational GUI available immediately — user can start chatting
6. Pro upgrade available in-app ($20-30/mo, 1 month activates instantly)

## Conversational Interface

Engram provides a ChatGPT-like GUI interface on top of its backend:

- **Chat window**: Natural language queries ("What did we discuss?", "Summarize my week")
- **Streaming responses**: Token-by-token display via SLM (local) or cloud VLM (Pro)
- **Sidebar**: Search, timeline, wiki navigation
- **Backend**: Same Engram.Store services used by CLI — no duplication
- **Architecture**: GUI → API server → Engram.Store (interface-based, decoupled)

## SLM Strategy (Free Tier)

Local inference uses tiered SLMs optimized for low-spec hardware:

| Model | Size | RAM (Q4) | Use Case |
|-------|------|----------|----------|
| all-MiniLM-L6-v2 | 22M | ~80MB | Semantic search, embeddings (always on) |
| Qwen2.5 0.5B | 500M | ~0.5GB | Classification, routing, entity extraction |
| Phi-4-mini | 3.8B | ~2.5GB | Summarization, QA, reasoning (on demand) |

Key: Don't use SLM for what algorithms can do. PII filtering = regex. Dedup = SHA-256. Routing = rules + embeddings. SLM only for summarization, QA, entity extraction.

## Locked Direction

- Stack: .NET/C# Windows-first app.
- App shape: background service plus tray/search surfaces.
- Local store root: `.engram`.
- Source-of-truth event ledger: `.engram/raw/YYYY-MM-DD/[event_id].json`.
- Metabolized memory: `.engram/wiki/*.md`.
- **Tier model: Free tier (local-only, $0) + Pro tier (cloud-enhanced, $20-$30/mo).**
- **Managed credit pooling: users NEVER provide API keys.**
- Canonical artifacts for this checkout:
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
- Artifact folder for this checkout: `Artifacts/`.
- Tier architecture: `docs/TIER-ARCHITECTURE.md`.

## Non-Negotiables

- Sensitive capture sources are opt-in.
- Excluded apps must never be captured.
- Raw events are append-only and traceable.
- Wiki facts must link back to raw event evidence.
- Cloud calls must be audited and policy-gated.
- Proactive interventions must read identity constraints first.
- Computer-use automation must start read-only and require approvals for risky actions.
- **Every phase must pass the quality gate before it is considered deliverable.** See `docs/QUALITY-GATE-POLICY.md`. No phase ships without industry-grade testing relevant to that phase's scope.
- **Free tier must be complete and useful on its own. Pro features must never break free tier.**
- **No raw private data sent to cloud without explicit policy approval.**

## Architecture Commitments

- Use provider interfaces for OCR, local model, cloud model, browser automation, and workspace connectors.
- Prefer JSON and Markdown files as the durable store first; add SQLite indexes only for local query performance.
- Implement encryption before real user-data ingestion.
- Keep GWS, cloud routing, research automation, computer-use automation, and sync on the roadmap, but out of Phase 1 implementation.
- **Every cloud feature sits behind a tier check. Free tier = providers return "not available".**
- **Model routing: Gemini 3 Flash for 90% routine work, Claude 4.5 Sonnet for complex tasks only.**

## Current State

- **Phases 1-8 complete.** 444/444 tests passing.
- Phase 1: Foundation + raw event store
- Phase 2: Immutable raw event store (hardened)
- Phase 3: Local ingestion MVP (file/clipboard/window capture)
- Phase 4: Markdown wiki memory (metabolizer, index, source links)
- Phase 5: Local search and briefs
- Phase 6: Identity hardening (Discovery SOP)
- Phase 7: Salience and drift engine
- Phase 8: Cloud reasoning and tier routing (CloudCallPipeline, rate limiter, providers)
- Production hardened: HashIndex, FileLock, WAL, RateLimiter, CircuitBreaker, InputValidator
- CLI entry point: 8 subcommands (init, status, search, capture, brief, wiki, help, version)
- **Latest commit:** f3b6aa5 — feat(phase-8): complete cloud reasoning pipeline
- **Next: Phase 9 — Google Workspace Metadata Ingestion (Pro Tier)**
- **Phases 1-8 = Free+Pro foundation. Phases 9-12 = Pro Tier features.**
