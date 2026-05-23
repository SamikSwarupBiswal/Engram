# Phase 12: Longitudinal Endurance & Entropy Resistance - Research

**Researched:** 2026-05-23
**Domain:** Longitudinal AI Agents, Semantic Compaction, Homeostasis, and Causal Database Recovery
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01: Graph Size Ceilings:** Limit the active in-memory and indexed Entity Graph size to a strict upper bound. Prevent unbounded node growth.
- **D-02: Attention/Salience Budgets:** Establish strict decay profiles where stale nodes lose relevance dynamically, preventing context pollution.
- **D-03: Narrative Compression:** Automatically summarize detailed transaction narratives into high-level abstractions once they pass a time threshold (e.g., >30 days old).
- **D-04: Autonomy Decay:** Decay trust scores and autonomy multipliers slowly during periods of user inactivity or dispute, requiring recalibration.
- **D-05: Permission Hardening:** Establish expiration horizons on directory containment zones and safety overrides to prevent permission creep.
- **D-06: Fatigue Throttling:** Clamp the maximum number of interventions in a rolling 24-hour and 7-day window.
- **D-07: Compaction Workers:** Background metabolism runs an offline compaction worker during low-activity sessions to prune redundant links and merge dormant entities.
- **D-08: Contradiction Expiration:** Evict resolved or stale contradictions after they are accepted/dismissed or remain unaddressed for >14 days.
- **D-09: Action Retry Exponential Backoff:** Automation actions that hit transient or environmental errors must apply exponential backoffs (up to a max of 3 attempts), then fail gracefully.
- **D-10: Restraint Learning:** The engine tracks user interaction friction (e.g. prompt dismissals, swift cancellations) and automatically increases silence thresholds.
- **D-11: Causal Continuity Verification:** The EventBus and Write-Ahead Log must self-reconcile on startup, identifying missing sequences and repairing broken causal chains.
- **D-12: Corruption Diagnostics & Auto-Recovery:** Automatically backup the local `.engram/` metadata daily, keeping the last 7 snapshots, and perform integrity checks (checksum validation) on launch.
- **D-13: Activation Damping:** Implement a global cognitive damping multiplier that reduces reasoning overhead during high-workload/low-salience background events.
- **D-14: Emotional Neutrality Audits:** Safety constitution checks must flag and filter out subjective, non-neutral reasoning traces.
- **D-15: Adaptive Restraint Patterns:** The system adjusts its tone and pacing dynamically, becoming quieter and calmer as the user's focus patterns stabilize.

### the agent's Discretion
- Compression ratio thresholds and mathematical formulas for salience decay.
- Choice of JSON/Markdown structures for diagnostic snapshots.
- UI styling adjustments to display cognitive load and homeostasis status.

### Deferred Ideas (OUT OF SCOPE)
- Cross-device sync or multi-device coordination.
- Integration with external cloud vector databases.

</user_constraints>

<architectural_responsibility_map>
## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Graph Compaction & Pruning | API/Backend (Store) | Database/Storage | Offline compaction should run in background services and serialize back to the markdown store. |
| Homeostatic Damping | API/Backend (Store) | — | Tracks active memory/inference load and adjusts reasoning budget inline. |
| Trust & Overrides Expiration | API/Backend (Store) | Database/Storage | Policy checks expire overrides and update local configuration state. |
| Restraint Pacing & Fatigue Clamps | API/Backend (Store) | — | Limits and throttles proactive intervention dispatches inside the pipeline. |
| Backup & Corruption Repair | API/Backend (Store) | Database/Storage | Replays WAL, verifies checksums, and restores snapshots on sidecar startup. |

</architectural_responsibility_map>

<research_summary>
## Summary
Long-horizon agent survivability hinges on **preventing state entropy and cognitive friction**. As an agent runs continuously, its memory graph accumulates redundant links (graph decay), its automation engine gains stale permissions (security creep), and its proactive alerts fatigue the user (interaction overload). 

The standard approach to combatting memory decay is **hierarchical semantic compaction** (drawing from RAPTOR/tree-summarization and sleep-cycle models). During low-load cycles, an offline background worker traverses the Entity Graph, detects clustered nodes, and summarizes them via an LLM, reducing the active context size. Causal continuity is guaranteed by startup WAL reconcilers that self-heal database logs from half-written state transitions.

**Primary recommendation:** Integrate these checks directly into the existing C# `BackgroundMetabolismService` and the `WriteAheadLog`. Run compaction using a sliding-window time decay ($S = S_0 \cdot e^{-\lambda t}$) and restrict proactive prompts via a token-bucket rate limiter.
</research_summary>

<standard_stack>
## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET 8.0 CLR | 8.0 | System execution & sidecar | Provides robust, thread-safe asynchronous channels and file system streams. |
| System.Threading.RateLimiting | 8.0.0 | Throttling and clamping | Standard high-performance rate-limiting token-buckets for C#. |

</standard_stack>

<architecture_patterns>
## Architecture Patterns

### Recommended Project Structure
```
src/Engram.Store/
├── Metabolism/
│   ├── BackgroundMetabolismService.cs   # Triggers daily/hourly compaction
│   ├── SemanticCompactor.cs            # Performs hierarchical summaries
│   └── RestraintEngine.cs              # Implements pacing and silence gates
├── Ingestion/
│   └── WalReconciler.cs                 # Self-heals WAL records on startup
└── Security/
    └── OverrideManager.cs               # Hardens boundary time-to-live (TTL)
```

### Pattern 1: Exponential Backoff Retry Policy
Instead of wrapping logic in fragile loops, use a clean retry policy state pattern.
```csharp
public class RetryPolicy
{
    public static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception) when (++attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay);
            }
        }
    }
}
```

### Anti-Patterns to Avoid
- **Unbounded Memory Retention:** Loading the entire Entity Graph into memory on every metabolism cycle. (Mitigation: Use paginated stream reader).
- **Direct Graph Deletion:** Deleting nodes during compaction rather than archiving or merging. Deletion ruins backlink integrity. (Mitigation: Always write merge-tombstone descriptors).
</architecture_patterns>

<dont_hand_roll>
## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Rate Limiting / Fatigue | Custom timestamp arrays | `System.Threading.RateLimiting.TokenBucketRateLimiter` | Thread-safe, implements proper replenishment math natively. |
| DB Backup/Restore | Copy-pasting directories | Structured zip/archive creation via `System.IO.Compression` | Handles locked files and keeps checksums. |

</dont_hand_roll>

<common_pitfalls>
## Common Pitfalls

### Pitfall 1: Cascade Rollback Failure
**What goes wrong:** A rollback step in the LIFO chain itself fails, leaving the system in a corrupted intermediate state.
**How to avoid:** Ensure every rollback action is wrapped in a strict try-catch block that logs the warning but never blocks subsequent rollback steps.

### Pitfall 2: Compaction Inference Cycles
**What goes wrong:** The compaction worker uses the LLM to summarize nodes, which generates *more* raw event logs, causing a feedback loop.
**How to avoid:** Explicitly flag background metabolism tasks so they bypass event logging pipelines.
</common_pitfalls>

<sources>
## Sources

### Primary (HIGH confidence)
- Microsoft Learn: System.Threading.RateLimiting — Token bucket implementations.
- Microsoft Learn: System.IO.Compression — Stable archive streams.
- LlamaSharp docs — KV cache cleanup interfaces.
</sources>

<metadata>
## Metadata
**Research date:** 2026-05-23
**Valid until:** 2026-06-23
</metadata>

---

*Phase: 12-longitudinal-endurance*
*Research completed: 2026-05-23*
*Ready for planning: yes*
