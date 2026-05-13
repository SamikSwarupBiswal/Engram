# Engram

## Vision

Engram is a Windows-first personal semantic operating layer. It turns a user's local digital activity into a durable, source-linked memory layer that can be searched, briefed, and eventually used by agents to help with research and operating-system tasks.

## Product Thesis

The first proof is not cloud automation. The first proof is that Engram can make personal context persistent, queryable, and auditable:

1. Capture events locally with explicit consent.
2. Store every source event immutably.
3. Metabolize raw events into Markdown wiki memory.
4. Make memory searchable and source-linked.
5. Detect drift between new events, stored facts, and stated priorities.

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

- **Phases 1-4 complete.** 216/216 tests passing.
- Phase 1: Foundation + raw event store
- Phase 2: Immutable raw event store (hardened)
- Phase 3: Local ingestion MVP (file/clipboard/window capture)
- Phase 4: Markdown wiki memory (metabolizer, index, source links)
- Production hardened: HashIndex, FileLock, WAL, RateLimiter, CircuitBreaker, InputValidator
- **Next: Phase 5 — Local Search and Briefs (Free Tier)**
- **Phases 5-7 = Free Tier. Phases 8-12 = Pro Tier.**
