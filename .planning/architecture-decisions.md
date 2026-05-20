# Architecture Decisions

Decisions informed by soak validation data.

## Decision 1: KV Cache Management

**Status: RESOLVED — Clear KV cache after each request**

### Outcome

Experiment 1 confirmed Outcome A: KV cache clearing after each request eliminates the collapse entirely.

| Metric | Without Clearing | With Clearing |
|--------|-----------------|---------------|
| 60 requests | Collapse at ~req 60 | 100% success |
| KV at end | ~2000 tokens | 0 (cleared) |
| Tok/s drift | N/A (dead) | +4.1% (normal) |

### Decision

**Clear KV cache after each request by default.**

This is a lifecycle hygiene fix, not an architecture change. The `SafeLLamaContextHandle.KvCacheClear()` API is stable and has zero measurable overhead.

### Why NOT the other options

| Option | Status | Reason |
|--------|--------|--------|
| Fresh context per request | Implemented, not yet tested | Unnecessary overhead if clearing works |
| Increase context size (8192) | Deferred | Delays problem, doesn't fix; clearing fixes it |
| Context pool with rotation | Not needed | Over-engineering for this failure mode |
| Worker recycling | Not needed for KV exhaustion | May still be useful for other failure modes |

## Decision 2: Worker Recycling

**Status: PENDING**

### Question

Should the runtime enforce `maxTokensPerWorker` and rotate workers before exhaustion?

### Data Needed

- [ ] Is the token budget deterministic across different prompt lengths?
- [ ] Does model unload/reload recover from exhaustion?
- [ ] What's the cost of worker rotation (downtime)?
- [ ] Can we predict exhaustion from telemetry?

## Decision 3: Health Endpoint Redesign

**Status: PENDING**

### Options

| Option | Pros | Cons |
|--------|------|------|
| Canary inference probe | Definitive liveness check | Slow, costs tokens |
| Consecutive failure counter | Fast, cheap | May have false positives |
| Token budget remaining | Predictive | Needs accurate tracking |

### Data Needed

- [ ] How quickly can we detect exhaustion?
- [ ] What's the false positive rate of failure counting?
- [ ] Can we predict exhaustion before it happens?

## Decision 4: Context Isolation Strategy

**Status: PENDING**

### Question

Should each request get its own context, or should we clear the shared context?

### Data Needed

- [ ] Performance impact of context isolation
- [ ] Memory impact of multiple contexts
- [ ] Does LLamaSharp support concurrent contexts?

---

*These decisions will be made after completing the soak validation phases (cancellation torture, long-context pressure, chaos testing).*
