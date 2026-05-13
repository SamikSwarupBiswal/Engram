# Phase 8: Cloud Reasoning and Tier Routing — PLAN.md

## Goal
Add audited Pro reasoning while preserving local-first privacy defaults.

## Success Criteria (from ROADMAP.md)
1. Routine ingestion remains local by default
2. Every cloud call records reason, provider, payload summary, and cost
3. Private raw data is never sent without explicit policy approval

## Quality Gate (from QUALITY-GATE-POLICY.md)
- Unit: Model routing selects correct tier
- Unit: Local filter reduces token ingress
- Integration: Cloud call -> audit log entry with reason + cost
- Security: Private raw data never sent without policy approval
- Security: Budget limit enforced, no runaway costs
- Performance: Local filtering adds < 50ms latency

## Plan 08-01: Model Routing and Local Filtering

### Components
1. **ICloudModelProvider** — interface for cloud model calls (mirrors IOcrProvider)
2. **TaskComplexity** — enum: Low, Medium, High
3. **CloudModelRequest/Response** — request/response models
4. **ModelRouter** — classifies tasks, routes to local vs cloud provider
5. **LocalFilter** — strips private data, produces sanitized state summaries
6. **TierGuard** — checks tier status, blocks cloud for Free users
7. **PrivacyClass** — enum: Public, Internal, Private, Sensitive
8. **EngramConfig extensions** — Tier, CloudEnabled, budget settings

### Files
- `src/Engram.Store/Cloud/ICloudModelProvider.cs`
- `src/Engram.Store/Cloud/TaskComplexity.cs`
- `src/Engram.Store/Cloud/CloudModelRequest.cs`
- `src/Engram.Store/Cloud/CloudModelResponse.cs`
- `src/Engram.Store/Cloud/ModelRouter.cs`
- `src/Engram.Store/Cloud/LocalFilter.cs`
- `src/Engram.Store/Cloud/TierGuard.cs`
- `src/Engram.Store/Cloud/PrivacyClass.cs`
- `tests/Engram.Store.Tests/ModelRouterTests.cs`
- `tests/Engram.Store.Tests/LocalFilterTests.cs`
- `tests/Engram.Store.Tests/TierGuardTests.cs`

### Test Plan (~35 tests)
- ModelRouter: routes Low→local, Medium→Gemini, High→Claude
- ModelRouter: unknown complexity defaults to local
- LocalFilter: strips raw screen data
- LocalFilter: strips clipboard content
- LocalFilter: strips email bodies
- LocalFilter: preserves public metadata
- LocalFilter: summary size < original payload
- TierGuard: Free tier blocks cloud calls
- TierGuard: Pro tier allows cloud calls
- TierGuard: cloud-disabled config blocks calls
- Integration: full routing with filter + audit

## Plan 08-02: Cloud Audit Log, Budget Controls, and Clean Cache

### Components
1. **CloudAuditEntry** — model: reason, provider, payload summary, cost, result, timestamp
2. **CloudAuditLog** — append-only JSONL at .engram/logs/cloud-audit.jsonl
3. **BudgetManager** — per-user daily/monthly cost caps, rate limiting
4. **BudgetConfig** — daily limit, monthly limit, per-call limit
5. **CleanCache** — semantic cache for non-private common research topics
6. **CacheEntry** — model: key, response, created_at, hit_count

### Files
- `src/Engram.Store/Cloud/CloudAuditEntry.cs`
- `src/Engram.Store/Cloud/CloudAuditLog.cs`
- `src/Engram.Store/Cloud/BudgetManager.cs`
- `src/Engram.Store/Cloud/BudgetConfig.cs`
- `src/Engram.Store/Cloud/CleanCache.cs`
- `src/Engram.Store/Cloud/CacheEntry.cs`
- `tests/Engram.Store.Tests/CloudAuditLogTests.cs`
- `tests/Engram.Store.Tests/BudgetManagerTests.cs`
- `tests/Engram.Store.Tests/CleanCacheTests.cs`

### Test Plan (~30 tests)
- CloudAuditLog: writes entry to JSONL
- CloudAuditLog: entry has all required fields
- CloudAuditLog: append-only (doesn't overwrite)
- CloudAuditLog: concurrent writes are safe
- BudgetManager: allows call within daily limit
- BudgetManager: blocks call exceeding daily limit
- BudgetManager: resets on new day
- BudgetManager: monthly limit enforcement
- BudgetManager: per-call cost validation
- CleanCache: stores and retrieves by key
- CleanCache: hit increments counter
- CleanCache: eviction of old entries
- CleanCache: private data never cached
- Integration: full cloud call → filter → budget → audit → cache

## Execution Order
1. Plan 08-01: stubs → tests (RED) → implementation (GREEN)
2. Plan 08-02: stubs → tests (RED) → implementation (GREEN)
3. Quality gate: all tests, integration, commit

## Estimated Tests
- ~65 new tests
- ~400 total passing at end
