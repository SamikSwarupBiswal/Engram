# Phase 12: Longitudinal Endurance & Entropy Resistance — PLAN.md

## Goal
Establish longitudinal endurance, preventing cognitive decay, semantic bloating, memory graph entropy, and operational fatigue over months of continuous execution.

## Success Criteria (from ROADMAP.md)
1. Semantic graph size and attention/salience budgets are strictly bounded.
2. User-configured permissions, trust scores, and overrides decay or expire over time.
3. Compaction and archival routines continuously merge redundant entities and prune references.
4. Auto-diagnostics and WAL recovery handle data corruptions and causal fractures.
5. Cognitive homeostasis damping manages local resources dynamically.

## Plan 12-01: Semantic Entropy and Memory Ecology Management (12A, 12C)

### Components
1. **SemanticCompactor** — Traverses the Entity Graph, detects clustered/redundant entities, and merges them using LLM summaries.
2. **GraphSalienceDecay** — Implements sliding-window time-decay formulas for salience, removing stale narrative paths.
3. **EngramConfig Extensions** — `MaxGraphNodes`, `SalienceDecayHalfLifeDays`, and `CompactionThreshold`.

### Files
- `src/Engram.Store/Metabolism/SemanticCompactor.cs` [NEW]
- `src/Engram.Store/Metabolism/GraphSalienceDecay.cs` [NEW]
- `src/Engram.Store/EngramConfig.cs` [MODIFY]
- `tests/Engram.Store.Tests/Metabolism/SemanticCompactorTests.cs` [NEW]

### Test Plan
- Verify `SemanticCompactor` merges overlapping entity nodes successfully.
- Verify `GraphSalienceDecay` reduces node salience levels exponentially over simulated time.
- Verify active graph size is strictly capped at `MaxGraphNodes`.

---

## Plan 12-02: Trust Stability, Overrides, and Operational Fatigue (12B, 12D)

### Components
1. **PacingController** — Tracks intervention frequency and clamps dispatches using a token-bucket rate limiter.
2. **OverrideExpiryManager** — Manages TTL (Time-To-Live) for safety overrides and directory whitelists.
3. **FrictionTracker** — Monitors user interaction (cancellations/dismissals) and raises silence thresholds dynamically.

### Files
- `src/Engram.Store/Governance/PacingController.cs` [NEW]
- `src/Engram.Store/Governance/OverrideExpiryManager.cs` [NEW]
- `src/Engram.Store/Governance/FrictionTracker.cs` [NEW]
- `tests/Engram.Store.Tests/Governance/LongitudinalTrustTests.cs` [NEW]

### Test Plan
- Verify interventions are blocked when the rolling fatigue limit is exceeded.
- Verify safety overrides expire automatically after their TTL.
- Verify silence thresholds scale up when consecutive user friction is logged.

---

## Plan 12-03: Multi-Month Survival & Auto-Recovery (12E, 12F, 12G)

### Components
1. **CausalReconciler** — Replays the WAL and repairs causal fractures (missing logs/events) on startup.
2. **BackupManager** — Performs daily ZIP backups of `.engram/` metadata with retention.
3. **HomeostasisController** — Implements cognitive damping (reduces context size/depth) during resource pressure.

### Files
- `src/Engram.Store/Ingestion/CausalReconciler.cs` [NEW]
- `src/Engram.Store/Security/BackupManager.cs` [NEW]
- `src/Engram.Store/Metabolism/HomeostasisController.cs` [NEW]
- `tests/Engram.Store.Tests/Ingestion/CausalReconcilerTests.cs` [NEW]

### Test Plan
- Verify WAL replay successfully recovers from a simulated mid-write crash.
- Verify daily backups are created and older backups are pruned when count > 7.
- Verify global cognitive damping scales down model context budgets during high CPU loads.

---

## Execution Order
1. **Plan 12-01**: Implement compaction and salience decay. Write tests.
2. **Plan 12-02**: Implement pacing clamps, override expiry, and friction tracking. Write tests.
3. **Plan 12-03**: Implement WAL reconciler, backups, and homeostasis damping. Write tests.
4. **Integration**: Expose new metrics in API `/api/health` and verify all tests pass.
