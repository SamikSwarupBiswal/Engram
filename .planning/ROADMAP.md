# Roadmap: Engram

## Overview

Engram is a Windows-first personal semantic operating layer. It ships as a desktop app (Tauri v2 + React) with a .NET 8 API sidecar that auto-spawns on launch. Users install via .exe/.msi installer and the app just works.

## Phases

- [x] **Phase 1: Repository and Runtime Foundation** [FREE] — **Complete: 2026-05-13**
- [x] **Phase 2: Immutable Raw Event Store** [FREE] — **Complete: 2026-05-13**
- [x] **Phase 3: Local Ingestion MVP** [FREE] — **Complete: 2026-05-13**
- [x] **Phase 4: Markdown Wiki Memory** [FREE] — **Complete: 2026-05-13**
- [x] **Phase 5: Local Search + Desktop Shell** [FREE] — **Complete: 2026-05-17**
- [x] **Phase 6: Identity Hardening + Discovery UI** [FREE] — **Complete: 2026-05-17**
- [x] **Phase 7: Salience, Drift, Inference Engine** [FREE] — **Complete: 2026-05-17**
- [x] **Phase 8: Cloud Reasoning + Token Billing** [PRO] — **Complete: 2026-05-17**
- [ ] **Phase 9: Google Workspace Metadata Ingestion** [PRO]
- [ ] **Phase 10: Agentic Research Workflow** [PRO]
- [ ] **Phase 11: Computer-Use Automation** [PRO]
- [ ] **Phase 12: Encryption, Sync, Production Hardening** [PRO]

## Progress

| Phase | Status | Completed |
|-------|--------|-----------|
| 1-4 | **Complete** | 2026-05-13 |
| 5 | **Complete** | 2026-05-17 |
| 6 | **Complete** | 2026-05-17 |
| 7 | **Complete** | 2026-05-17 |
| 8 | **Complete** | 2026-05-17 |
| 9-12 | Not started | — |

## Current State

- **Phases 1-8 complete.** 588/588 tests passing.
- Desktop app built and installed on Windows
- 32 API endpoints, all connected to frontend
- Token budget system (Free ~60K, Pro 500K tokens/month)
- OpenAI-compatible provider (any API key)
- Inference engine (LLamaSharp + Vulkan)
- **Next: Phase 9 — Google Workspace Metadata Ingestion**
