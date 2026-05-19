# Architecture Decisions

Decisions informed by soak validation data. **Status: PENDING** — waiting for more data before committing to architecture changes.

## Decision 1: KV Cache Management

**Status: PENDING**

### Options

| Option | Pros | Cons |
|--------|------|------|
| Clear KV cache between requests | Simple, low overhead | May lose context for multi-turn |
| Fresh context per request | Clean isolation | Expensive (model reload?) |
| Increase context size (e.g., 8192) | Delays problem | Doesn't fix, doubles memory |
| Context pool with rotation | Balanced | Complex |

### Data Needed

- [ ] Cost of KV cache clearing (latency impact)
- [ ] Can LLamaSharp clear KV cache without reloading model?
- [ ] Does fresh context per request require model reload?
- [ ] Memory impact of larger context size

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
