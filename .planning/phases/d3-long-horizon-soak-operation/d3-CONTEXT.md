# Phase D3: Long-Horizon Soak Operation - Context

**Gathered:** 2026-05-23
**Status:** Planned

<domain>
## Phase Boundary

Expose Engram to weeks-long operational soak runs under mock event streams to observe and correct long-term degradation patterns, memory leaks, disk footprint expansion, memory graph entropy, and cognitive debt buildup.

Objectives:
- **Long-Horizon Soak:** Run the .NET sidecar and metabolism services continuously for simulated weeks.
- **Graph Growth & Scaling Limits:** Verify the entity graph, TF-IDF search indices, and timeline files do not expand indefinitely, maintaining flat resource usage.
- **Semantic Entropy & Sludge:** Monitor insight propagation pathways, detecting stale "sludge" (disconnected nodes, expired contradictions, narrative loops).
- **Cognitive Debt Accumulation:** Verify that background tasks queued when busy are successfully executed during idle cycles without starving new requests.
- **Trust Drift:** Track the long-term stability of permission scopes and override values, ensuring trust scores stabilize at healthy baselines.

</domain>

<decisions>
## Implementation Decisions

### D3A: Long-Horizon Simulation & Acceleration
- **D3-01: Accelerated Time-Warp Driver:** Build a developer soak driver that simulates 30 days of active user behavior (emails, file saves, clipboard, browser activities, restarts) in a compressed execution timeframe (e.g. 12 hours).
- **D3-02: Health Check Logs:** Dump daily performance indicators (RAM, CPU, Node count, File size, WAL count, recovery events) to a diagnostics log for regression review.

### D3B: Memory compaction & Ecology Gates
- **D3-03: Abstraction Compaction Limit:** Assert that the background `SemanticCompactor` executes when the graph exceeds a strict threshold (e.g., 2000 nodes), successfully compressing old events without losing parent references.
- **D3-04: Contradiction Expiry Enforcement:** Enforce auto-cleanup of unaddressed contradiction markers older than 14 days, preventing stale alerts from accumulating.

</decisions>

<canonical_refs>
## Canonical References

- `soak-test.py` — The current script executing runtime survivability requests.
- `.planning/soak-validation.md` — Logs and analysis from previous short-term soak sessions.
- `src/Engram.Store/Metabolism/SemanticCompactor.cs` — Code responsible for entity graph summarization.

</canonical_refs>

<code_context>
## Existing Code Insights

- `HomeostasisController.cs` — Manages metabolic resource triage and cognitive debt resolution.
- `BackupManager.cs` — Creates daily compressed metadata ZIP backups.
- `NarrativeDriftAuditor.cs` — Weekly self-model consistency auditor.

</code_context>

<deferred>
## Deferred Ideas
- Cross-machine telemetry collection (strictly local files only).
- Dynamic RAM allocation tuning (rely on standard garbage collection limits and homeostasis context damping).

</deferred>
