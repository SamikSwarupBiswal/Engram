# Codebase Concerns

**Analysis Date:** 2026-05-23

## Tech Debt

**Telemetry Hardcoding in LocalInferenceEngine:**
- Issue: `ConsecutiveFailures` is hardcoded to `0` inside `LocalInferenceEngine.GetTelemetry()`.
- File: `src/Engram.Store/Inference/LocalInferenceEngine.cs#L622`
- Why: Deferred implementation during the initial telemetry setup.
- Impact: Accurate count of consecutive inference failures is not propagated to the dashboard telemetry directly from the engine (though consecutive *cleanup* failures are tracked independently by `InferenceLifecycleManager`).
- Fix approach: Add a private counter field in `LocalInferenceEngine` that increments on inference exception and resets on success, then expose it in `GetTelemetry()`.

## Known Bugs

**No Major Unresolved Bugs:**
- *Note:* The previously reported critical phase-transition collapse (NoKvSlot) has been successfully resolved in Sprint 4 via the mandatory `KvCacheClear()` after each request.

## Security & Privacy Considerations

**Regex-based Local Privacy Filtering:**
- Risk: `LocalFilter` strips private database items, emails, and clipboard data before cloud routing in Turbo mode, but regex-based extraction might fail to catch custom formatting of sensitive PII.
- File: `src/Engram.Store/Cloud/LocalFilter.cs`
- Current mitigation: Enforces a strict default-deny policy where data is categorized into privacy tiers; only items marked explicitly as Public/Internal metadata are allowed to pass.
- Recommendations: Implement a secondary deterministic PII scanner (e.g. Presidio-based or named-entity-recognition heuristics) to filter outgoing payloads.

## Performance Bottlenecks

**Vulkan Stability Probing Latency:**
- Problem: The initial run stability probe adds significant startup latency.
- File: `src/Engram.Store/Inference/BackendProbe.cs`
- Measurement: 5s to 30s delay on first launch on a clean machine.
- Cause: The app boots in `DetectingBackend` and performs a live canary inference to verify the Vulkan driver is stable.
- Improvement path: Optimize the canary prompt size or execute the probe concurrently after launching a minimal local shell so the user sees a "Ready" UI faster.

## Fragile Areas

**Windows Graphics Capture Fallback:**
- File: `src/Engram.Store/Perception/ScreenCaptureService.cs`
- Why fragile: Windows Graphics Capture (WGC) requires Win 10 1903+. Legacy systems fallback to GDI capture.
- Common failures: Slower capture rates and lack of window handle isolation on legacy versions.
- Safe modification: Encapsulate WGC interop calls with try-catch blocks and verify the system capability using native DLL imports on startup.
- Test coverage: Moderate. Coverage exists for fallback paths, but testing is platform-dependent.

## Scaling Limits

**Local Memory Constraints for Phi-4-mini:**
- Current capacity: Bounded to 4096 context size.
- Limit: Long-running conversation history or deep code research workflows can quickly exceed 4096 context cells.
- Symptoms at limit: Context compression or truncation kicks in, causing the model to lose track of earlier instructions or memories.
- Scaling path: Upgrading the local engine context limit to 8192 (tested under Experiment 3) or routing to cloud Pro tier.

## Dependencies at Risk

**LLamaSharp native runtimes:**
- Risk: Native DLL bindings (`libllama.dll`) vary depending on the host CPU/GPU features (AVX2, AVX512, Vulkan). Mismatched DLL loads cause catastrophic app crashes on startup.
- Impact: Total failure to load the app backend.
- Migration plan: Maintain the robust `BackendProbe` fallback verification to isolate crash loops and revert to safe CPU compilation versions.

---

*Concerns audit: 2026-05-23*
*Update as issues are fixed or new ones discovered*
