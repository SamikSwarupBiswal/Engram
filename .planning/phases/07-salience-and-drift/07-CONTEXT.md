# Phase 7: Salience and Drift Engine - Context

**Gathered:** 2026-05-13
**Status:** Decisions locked
**Tier:** FREE (local-only, no cloud) — LAST FREE TIER PHASE

## Phase Boundary

Phase 7 adds salience decay (stale knowledge fades) and drift detection (contradictions trigger alerts).

Out of scope:
- Cloud model calls (Phase 8)
- Google Workspace (Phase 9)
- Research automation (Phase 10)
- Computer-use (Phase 11)

## Implementation Decisions

### D-036: Salience Decay Formula
- Power law: S(t) = S0 * e^(-lambda * t)
- S0 = initial salience (1.0)
- lambda = decay constant (default: 0.023 per day = 50% after 30 days)
- t = days since last_touched_at
- Configurable lambda via EngramConfig
- Salience computed on read (lazy), not stored (except in front matter for caching)

### D-037: Archive Movement
- Nodes with salience < 0.1 are candidates for archival
- Archive = move .md file from wiki/ to archives/
- Preserve front matter + body
- Create archive index (archives/index.md)
- User can restore from archive
- Archive threshold configurable

### D-038: Drift Detection
- Compare new raw events against existing wiki facts
- Detection methods:
  1. Keyword contradiction: new event negates a wiki fact
  2. Date conflict: new event has contradictory date for same entity
  3. Status change: entity status changed (e.g., project completed vs active)
- No cloud model required — rule-based detection
- DriftAlert created for each detected contradiction

### D-039: DriftAlert Model
- Fields: alert_id, node_id, description, severity, source_event_ids, status
- Status: pending, dismissed, accepted, converted
- Dismissed: user says "not a real contradiction"
- Accepted: user confirms the drift, wiki should be updated
- Converted: drift resolved, wiki node updated
- Alerts persisted in .engram/config/drift_alerts.json

### D-040: Drift Alert Resolution
- User can dismiss (mark as false positive)
- User can accept (mark as real drift)
- User can convert (apply the drift to wiki)
- Converting = update wiki node with new facts from the event

*Phase: 07-salience-and-drift*
