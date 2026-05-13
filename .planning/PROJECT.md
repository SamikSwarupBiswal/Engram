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
- Canonical artifacts for this checkout:
  - `Artifacts/Product Requirements Document_Engram Full Specification.md`
  - `Artifacts/Engram Implementation Plan.md`
- Artifact folder for this checkout: `Artifacts/`.

## Non-Negotiables

- Sensitive capture sources are opt-in.
- Excluded apps must never be captured.
- Raw events are append-only and traceable.
- Wiki facts must link back to raw event evidence.
- Cloud calls must be auditable and policy-gated.
- Proactive interventions must read identity constraints first.
- Computer-use automation must start read-only and require approvals for risky actions.
- **Every phase must pass the quality gate before it is considered deliverable.** See `docs/QUALITY-GATE-POLICY.md`. No phase ships without industry-grade testing relevant to that phase's scope.

## Architecture Commitments

- Use provider interfaces for OCR, local model, cloud model, browser automation, and workspace connectors.
- Prefer JSON and Markdown files as the durable store first; add SQLite indexes only for local query performance.
- Implement encryption before real user-data ingestion.
- Keep GWS, cloud routing, research automation, computer-use automation, and sync on the roadmap, but out of Phase 1 implementation.

## Current State

This checkout currently contains product artifacts and GSD planning documents only. No application code exists yet.

## References

- `Artifacts/Product Requirements Document_Engram Full Specification.md`
- `Artifacts/Engram Implementation Plan.md`

## Last Updated

2026-05-10
