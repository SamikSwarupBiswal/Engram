# Soak Validation Results

Date: 2026-05-19
Branch: `soak-validation`
Environment: WSL (linux-x64), CPU inference (no Vulkan), Phi-4-mini Q4_K_M

## Baseline Metrics

| Metric | Value |
|--------|-------|
| Backend | CPU (no Vulkan in WSL) |
| Model load time | 10.9s |
| Total startup | 10.9s |
| Tok/s (avg) | 1.2 |
| Tok/s (min/max) | 1.0 / 1.3 |
| Success rate | 20/20 (100%) |
| Latency (avg) | 12.96s |
| Latency (p95) | 51.41s |
| Context size | 4096 tokens |

## Sequential Endurance Soak (200 requests)

### Results

| Metric | Value |
|--------|-------|
| Total requests | 200 |
| Successful | 33 (16.5%) |
| Failed | 167 (83.5%) |
| Timeouts | 0 |
| Cliff point | Request 33 |
| Error | `llama_decode failed: 'NoKvSlot'` |

### Failure Pattern

```
Requests 0-32:  100% success, 1.1 tok/s — perfectly healthy
Request 33:     TRANSITION — SUCCESS → FAILURE
Requests 33-199: 100% failure, 0.0s elapsed, 0 tokens generated
```

**Classification: CATASTROPHIC — binary phase transition, not gradual degradation**

## Root Cause: KV Cache Exhaustion

The KV cache accumulates tokens across requests and is never cleared.

### Evidence

| Run | Starting tokens | Generated before cliff | Total at collapse |
|-----|----------------|----------------------|-------------------|
| Run 1 (soak) | 0 | ~908 tokens | ~908 tokens |
| Run 2 (boundary) | 1513 | ~482 tokens | 1995 tokens |

Both runs collapse when total consumed tokens approach the context size limit (4096).

### Mechanism

1. Each request creates a new `InteractiveExecutor` with the same shared `LLamaContext`
2. The context's KV cache retains tokens from all previous requests
3. Prompt tokens + generated tokens accumulate in the cache
4. When accumulated tokens approach 4096, `llama_decode` fails with `NoKvSlot`
5. The failure is permanent — no self-healing until process restart

### Why Request Count Varies

Run 1: 33 requests before cliff (shorter prompts, ~27 tokens/request average)
Run 2: 24 requests before cliff (started with 1513 tokens already consumed)

The cliff correlates with **total tokens consumed**, not request count.

## Critical Observability Failure

After the runtime poisoned:

```json
{
  "state": "Ready",
  "isReady": true,
  "canAcceptRequests": true
}
```

The health endpoint reported a false positive. The lifecycle manager has no feedback loop from failed inferences back to health state.

### What's Missing

- No consecutive failure counter
- No inference error rate tracking
- No automatic state transition to Error/Degraded after N failures
- `canAcceptRequests` only checks lifecycle state, not actual inference capability

## Classifier Failure

The soak script's auto-classification reported:
```
DRIFT: ACCEPTABLE (<10%)
TIMEOUT RATE: NORMAL (<5%)
TIMEOUT CLUSTERING: NONE
```

While the runtime had an 83.5% failure rate. The classifier was optimized for gradual degradation and missed the binary collapse entirely.

### Fix: Success rate gate

The first classifier rule must be:
1. Check success rate — if <80%, classify as CATASTROPHIC
2. Only then check drift, latency, timeouts

## Fixes Applied (this session)

| Fix | Why |
|-----|-----|
| Added `LLamaSharp.Backend.Cpu` package | Native .so libs missing from Debug build |
| Fixed `FormatChatPrompt` | Used raw text format instead of Phi-4-mini template (`<\|system\|>/<\|user\|>/<\|assistant\|>`) |
| Fixed AntiPrompts | Old AntiPrompts (`"User:", "Assistant:"`) matched prompt text, causing 0-token generation |
| Fixed response cleanup | Updated to strip template artifacts instead of old format strings |

These are prerequisite fixes, not architecture changes.

## What Was NOT Tested

- [ ] Phase 4: Cancellation torture
- [ ] Phase 5: Long-context pressure
- [ ] Phase 6: Chaos testing
- [ ] Vulkan performance (need Windows)
- [ ] Model unload/reload recovery
- [ ] Fresh context per request

## Next Steps

### Priority 1: Fix KV Cache Clearing

Options:
1. Clear KV cache between requests (`llama_kv_cache_seq_clear` or equivalent)
2. Create fresh `LLamaContext` per request (expensive but clean)
3. Increase context size (delays but doesn't fix)

### Priority 2: Health Feedback Loop

- Add consecutive failure counter to inference engine
- Auto-transition to Error state after N consecutive failures
- Expose failure metrics in health response

### Priority 3: Resume Soak Testing

After KV cache fix:
1. Re-run sequential soak (expect no cliff)
2. Cancellation torture (Phase 4)
3. Long-context pressure (Phase 5)
4. Chaos testing (Phase 6)
