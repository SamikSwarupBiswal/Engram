# Runtime Findings

Empirical observations about the Engram inference runtime behavior.

## Finding 1: Phase-Transition Failure

The runtime does NOT degrade gradually. It exhibits binary phase-transition behavior:

```
Healthy → Healthy → Healthy → Healthy → Dead
```

This means:
- Performance metrics (tok/s, latency) remain stable until collapse
- Collapse is sudden and permanent
- No warning signs in telemetry before failure
- Averaged metrics are misleading — they look fine until the moment of death

### Implication

Traditional reliability metrics (average tok/s, p95 latency, drift percentage) are insufficient. Need **survivability semantics** — the question isn't "how well is it performing?" but "is it still alive?"

## Finding 2: Deterministic Resource Exhaustion

The collapse point correlates with total tokens consumed, not:
- Request count
- Elapsed time
- Memory usage
- Random chance

The KV cache accumulates tokens from all requests. When total consumed tokens approach the context size (4096), the runtime dies.

### Implication

The runtime has a **finite lifespan** measured in tokens, not requests. This is a bounded resource problem, not a reliability problem.

## Finding 3: Shared Context Poisoning

The `LLamaContext` is created once and shared across all requests. The `InteractiveExecutor` is created per-request but operates on the shared context. The KV cache state persists between requests.

This means:
- Each request's prompt and generated tokens accumulate
- No cleanup mechanism exists between requests
- The context is permanently poisoned after exhaustion

### Implication

Need either:
- Context isolation per request
- Explicit KV cache clearing between requests
- A context pool with rotation

## Finding 4: Health Endpoint False Positive

The lifecycle manager tracks state transitions but has no feedback from actual inference results. After the runtime poisoned:

```
State: Ready
isReady: true
canAcceptRequests: true
```

But every inference fails with `NoKvSlot`.

### Implication

Health checks must verify actual inference capability, not just lifecycle state. Need:
- Canary inference probe
- Consecutive failure counter
- Automatic state degradation on repeated failures

## Finding 5: Prompt Template Sensitivity

The model generates 0 tokens with incorrect prompt format. Phi-4-mini requires:
```
<|system|>system message<|end|>
<|user|>user message<|end|>
<|assistant|>
```

Raw text format (`User: ... Assistant:`) produces zero output with no error.

### Implication

Prompt format is a hard requirement, not a preference. Must be validated per model.

## Finding 6: AntiPrompt Matching

AntiPrompts that match prompt text cause immediate termination. The old AntiPrompts included `"Assistant:"` which matched the prompt suffix, causing 0-token generation.

### Implication

AntiPrompts must be carefully chosen to avoid matching the prompt template. Use model-specific end tokens (e.g., `<|end|>`) instead of role labels.

## Classification Framework (updated)

| Category | Pattern | Response |
|----------|---------|----------|
| RECOVERABLE | Isolated slowdown, single timeout | Monitor |
| DEGRADING | Cumulative drift, increasing variance | Investigate |
| CATASTROPHIC | Binary collapse, permanent failure | Fix immediately |

**New rule: Success rate gate is the first check. If success rate < 80%, skip all other analysis and classify as CATASTROPHIC.**

## Architectural Implications

### Worker Recycling

The runtime has a finite lifespan (measured in tokens). This strongly suggests:
- `maxTokensPerWorker` limit
- Graceful worker rotation before exhaustion
- Health monitoring based on token budget remaining

### Context Isolation

Shared context is the root cause of the collapse. Options:
1. Fresh context per request (clean but expensive)
2. KV cache clearing between requests (efficient but needs testing)
3. Context pool with LRU rotation (balanced)

### Observability Redesign

Current observability tracks liveness. Need to add:
- Survivability metrics (tokens remaining before exhaustion)
- Inference success rate (real-time)
- Automatic state transitions based on actual inference results
