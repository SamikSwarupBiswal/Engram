# Phase 12: Longitudinal Endurance & Entropy Resistance - Context

**Gathered:** 2026-05-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish Longitudinal Endurance & Entropy Resistance capabilities to allow Engram to run continuously for months without cognitive decay, governance drift, semantic bloating, or operational fatigue. This phase transitions Engram from a powerful cognitive tool into stable civil digital infrastructure.

</domain>

<decisions>
## Implementation Decisions

### 12A: Semantic Entropy Resistance
- **D-01: Graph Size Ceilings:** Limit the active in-memory and indexed Entity Graph size to a strict upper bound. Prevent unbounded node growth.
- **D-02: Attention/Salience Budgets:** Establish strict decay profiles where stale nodes lose relevance dynamically, preventing context pollution.
- **D-03: Narrative Compression:** Automatically summarize detailed transaction narratives into high-level abstractions once they pass a time threshold (e.g., >30 days old).

### 12B: Longitudinal Trust Stability
- **D-04: Autonomy Decay:** Decay trust scores and autonomy multipliers slowly during periods of user inactivity or dispute, requiring recalibration.
- **D-05: Permission Hardening:** Establish expiration horizons on directory containment zones and safety overrides to prevent permission creep.
- **D-06: Fatigue Throttling:** Clamp the maximum number of interventions in a rolling 24-hour and 7-day window.

### 12C: Memory Ecology Management
- **D-07: Compaction Workers:** Background metabolism runs an offline compaction worker during low-activity sessions to prune redundant links and merge dormant entities.
- **D-08: Contradiction Expiration:** Evict resolved or stale contradictions after they are accepted/dismissed or remain unaddressed for >14 days.

### 12D: Operational Fatigue Systems
- **D-09: Action Retry Exponential Backoff:** Automation actions that hit transient or environmental errors must apply exponential backoffs (up to a max of 3 attempts), then fail gracefully.
- **D-10: Restraint Learning:** The engine tracks user interaction friction (e.g. prompt dismissals, swift cancellations) and automatically increases silence thresholds.

### 12E: Multi-Month Runtime Survival
- **D-11: Causal Continuity Verification:** The EventBus and Write-Ahead Log must self-reconcile on startup, identifying missing sequences and repairing broken causal chains.
- **D-12: Corruption Diagnostics & Auto-Recovery:** Automatically backup the local `.engram/` metadata daily, keeping the last 7 snapshots, and perform integrity checks (checksum validation) on launch.

### 12F: Cognitive Homeostasis
- **D-13: Activation Damping:** Implement a global cognitive damping multiplier that reduces reasoning overhead during high-workload/low-salience background events.
- **D-14: Emotional Neutrality Audits:** Safety constitution checks must flag and filter out subjective, non-neutral reasoning traces.

### 12G: Longitudinal Human Adaptation
- **D-15: Adaptive Restraint Patterns:** The system adjusts its tone and pacing dynamically, becoming quieter and calmer as the user's focus patterns stabilize.

### the agent's Discretion
- Compression ratio thresholds and mathematical formulas for salience decay.
- Choice of JSON/Markdown structures for diagnostic snapshots.
- UI styling adjustments to display cognitive load and homeostasis status.

</decisions>

<canonical_refs>
## Canonical References

### Current Architecture & Context
- `.planning/PROJECT.md` — Core architecture overview (shell sidecar, background metabolism).
- `.planning/STATE.md` §Background Metabolism — Details of deduplication and salience scoring.
- `.planning/runtime-findings.md` — Runtime survivability lessons (KV cache, collapse bounds).
- `.planning/soak-validation.md` — Verification parameters for continuous runtime.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `InferenceLifecycleManager.cs` — The single source of truth for runtime state and degradation triggers. Can be extended to track homeostasis and long-term degradation.
- `BackgroundMetabolismService.cs` — Hosted service running every 5 minutes. Ideal integration point for entropy resistance (D-01 to D-03) and memory ecology compaction (D-07, D-08).
- `ContradictionDetector.cs` — Logic for goal-behavior gap analysis.
- `TruthCalibrationStore.cs` — Holds human corrections.

### established Patterns
- LIFO (Last In First Out) recovery stacks in `ActionRuntime` can be utilized to gracefully exit aborted task runs and prevent workflow haunting.

</code_context>

<deferred>
## Deferred Ideas
- Cross-device sync or multi-device coordination (Strictly out of scope due to local-first principles).
- Integration with external cloud vector databases (Anti-pattern).

</deferred>

---

*Phase: 12-longitudinal-endurance*
*Context gathered: 2026-05-23*
