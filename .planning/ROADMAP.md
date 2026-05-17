# Roadmap: Engram

## Overview

Engram is a Windows-first personal semantic operating layer. It ships as a desktop app (Tauri v2 + React) with a .NET 8 API sidecar that auto-spawns on launch. Users install via .exe/.msi installer and the app just works.

## Quality Gate

**Every phase must pass the quality gate defined in `docs/QUALITY-GATE-POLICY.md`.** The gate includes: unit test coverage, integration validation, performance budgets, security checks, build verification, and manual smoke testing.

## Phases

**Tier Mapping:**
- Phases 1-7: FREE TIER (local-only, $0/mo)
- Phases 8-12: PRO TIER (cloud-enhanced, $20-$30/mo)

- [x] **Phase 1: Repository and Runtime Foundation** [FREE] — .NET solution skeleton, `.engram` initializer, append-only raw event ledger. **Complete: 2026-05-13**
- [x] **Phase 2: Immutable Raw Event Store** [FREE] — Atomic writes, WAL, hash index, file locking, integrity verification. **Complete: 2026-05-13**
- [x] **Phase 3: Local Ingestion MVP** [FREE] — File watcher, clipboard, active window, circuit breaker, rate limiter, exclusion list. **Complete: 2026-05-13**
- [x] **Phase 4: Markdown Wiki Memory** [FREE] — Wiki nodes, serializer, metabolizer, index generator, source-linked facts. **Complete: 2026-05-13**
- [x] **Phase 5: Local Search and Briefs + Desktop Shell** [FREE] — SearchEngine, BriefGenerator, CaptureStatus + Tauri desktop app, React frontend, .NET API sidecar, ChatGPT-style sidebar, all views wired to API. **Complete: 2026-05-17**
- [x] **Phase 6: Identity Hardening** [FREE] — DiscoverySOP, InterventionPolicy, IdentityStore + in-app Discovery interview, editable identity settings, intervention API. **Complete: 2026-05-17**
- [x] **Phase 7: Salience and Drift Engine** [FREE] — SalienceScorer, DriftDetector, DriftAlertStore, ArchiveManager. **Complete: 2026-05-13**
- [x] **Phase 8: Cloud Reasoning and Tier Routing** [PRO] — CloudCallPipeline, ModelRouter, LocalFilter, TierGuard, BudgetManager, CloudAuditLog, CleanCache. **Complete: 2026-05-13**
- [ ] **Phase 9: Google Workspace Metadata Ingestion** [PRO] — OAuth, Gmail/Calendar/Drive metadata connectors.
- [ ] **Phase 10: Agentic Research Workflow** [PRO] — Playwright research runs, cited wiki summaries.
- [ ] **Phase 11: Computer-Use Automation** [PRO] — Windows automation with approval gates.
- [ ] **Phase 12: Encryption, Sync, and Production Hardening** [PRO] — AES-256, installer, performance gates.

## Progress

| Phase | Plans | Status | Completed |
|-------|-------|--------|-----------|
| 1. Repository and Runtime Foundation | 3/3 | **Complete** | 2026-05-13 |
| 2. Immutable Raw Event Store | 1/1 | **Complete** | 2026-05-13 |
| 3. Local Ingestion MVP | 2/2 | **Complete** | 2026-05-13 |
| 4. Markdown Wiki Memory | 3/3 | **Complete** | 2026-05-13 |
| 5. Local Search + Desktop Shell | 3/3 + UI | **Complete** | 2026-05-17 |
| 6. Identity Hardening + UI | 2/2 + UI | **Complete** | 2026-05-17 |
| 7. Salience and Drift Engine | 2/2 | **Complete** | 2026-05-13 |
| 8. Cloud Reasoning and Tier Routing | 2/2 | **Complete** | 2026-05-13 |
| 9. Google Workspace Ingestion | 0/TBD | Not started | — |
| 10. Agentic Research Workflow | 0/TBD | Not started | — |
| 11. Computer-Use Automation | 0/TBD | Not started | — |
| 12. Encryption, Sync, Hardening | 0/TBD | Not started | — |

## Current State

- **Phases 1-8 complete.** 516/516 tests passing.
- Desktop app built and installed on Windows (Tauri v2 + React + .NET sidecar)
- Installers: `Engram_1.0.0_x64-setup.exe` (2.46 MB), `Engram_1.0.0_x64_en-US.msi` (3.69 MB)
- **Next: Phase 9 — Google Workspace Metadata Ingestion (Pro Tier)**
