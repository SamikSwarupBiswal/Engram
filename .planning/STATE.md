# Engram State

**Status:** Phases 1-8 complete
**Current:** Phase 8 — Cloud Reasoning + Token Billing (DONE)
**Next:** Phase 9 — Google Workspace Metadata Ingestion [PRO]
**Last Activity:** 2026-05-18
**Tests:** 588/588 passing
**Latest Commit:** 99d36d2
**Git:** master (pushed)

## What Engram Is

Engram is a Windows-first personal semantic operating layer. It captures everything you do on your computer, metabolizes it into a structured wiki of knowledge, and lets you search, recall, and reason over your entire digital life.

It ships as a desktop app (Tauri v2 + React) with a .NET 8 API sidecar. Users install via .exe installer and the app just works.

## Architecture

```
User double-clicks Engram
  → Tauri shell (Rust, ~10MB)
    → Spawns .NET API sidecar on 127.0.0.1:5000
    → Loads React frontend
    → Frontend connects to sidecar automatically
    → Chat/Search/Wiki/Timeline/Settings/Archive all work
    → Model auto-downloads on first launch
    → User closes app → sidecar killed
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Desktop Shell | Tauri v2 (Rust) |
| Frontend | React 19 + TypeScript + Tailwind CSS |
| Backend | .NET 8 Minimal API |
| Inference | LLamaSharp + Vulkan (local) |
| Model | Phi-4-mini GGUF Q4_K_M (~2.3GB) |
| Storage | Markdown files (.engram/) |
| Cloud | OpenAI-compatible API (any provider) |

## Project Structure

```
Engram/
├── src/
│   ├── Engram.Store/          Core library (all logic)
│   │   ├── Capture/           Event capture (clipboard, files, windows)
│   │   ├── Cloud/             Cloud pipeline, providers, audit
│   │   ├── Identity/          User profile, discovery, intervention
│   │   ├── Inference/         LLamaSharp, GPU detection, model mgmt
│   │   ├── Billing/           Token budget, pricing, tiers
│   │   ├── Salience/          Decay scoring, drift detection
│   │   ├── Search/            TF-IDF search, brief generator
│   │   ├── Validation/        Input validation, sanitization
│   │   └── Wiki/              Wiki node store, serializer
│   ├── Engram.Cli/            Developer CLI
│   ├── Engram.Api/            ASP.NET Minimal API (sidecar)
│   └── Engram.App/            Tauri + React frontend
│       ├── src/                React components
│       ├── src-tauri/          Rust shell + sidecar config
│       └── installer.nsi      NSIS installer script
├── tests/
│   └── Engram.Store.Tests/    588 tests
└── .planning/                 All planning docs
```

## API Endpoints (32)

```
GET  /                              Health
GET  /api/status                    Workspace stats
GET  /api/search?q=                 Search wiki
GET  /api/wiki                      List wiki nodes
GET  /api/wiki/:id                  Get single node
GET  /api/brief?time=               Morning/evening brief
GET  /api/events                    Raw event history
GET  /api/identity                  User profile
GET  /api/identity/anti-goals       Anti-goals
GET  /api/identity/priorities       Priorities
GET  /api/discovery/status          Discovery complete?
GET  /api/drift                     Drift alerts
GET  /api/drift/stats               Alert statistics
GET  /api/salience                  Salience scores
GET  /api/archive                   Archived nodes
GET  /api/archive/candidates        Nodes eligible for archival
GET  /api/model/status              Model + GPU info
GET  /api/power-mode                Current mode (eco/turbo)
GET  /api/tokens                    Token budget status
GET  /api/tokens/pricing            Plans, packs, rates
GET  /api/provider                  Provider config
POST /api/discovery                 Run discovery interview
POST /api/intervention/check        Evaluate intervention
POST /api/drift/:id/accept          Accept drift alert
POST /api/drift/:id/dismiss         Dismiss drift alert
POST /api/drift/:id/convert         Convert to wiki update
POST /api/archive/stale             Archive stale nodes
POST /api/archive/:id/restore       Restore from archive
POST /api/model/download            Download model
POST /api/model/load                Load model
POST /api/model/unload              Unload model
POST /api/power-mode                Switch eco/turbo
POST /api/tokens/check              Check token budget
POST /api/tokens/pack               Buy token pack
POST /api/tokens/tier               Change tier
POST /api/provider                  Save provider config
POST /v1/chat/completions           Chat (real inference)
PUT  /api/identity                  Update profile
```

## Frontend Views (6)

| View | API Connections | Features |
|------|----------------|----------|
| Chat | /v1/chat/completions, /api/tokens/check | Token check, localStorage, auto-download bar |
| Search | /api/search | Loading/error states, result cards |
| Wiki | /api/wiki | Node type filters, salience, grid |
| Timeline | /api/events | Chronological list, source badges |
| Settings | /api/status, /api/identity, /api/drift, /api/tokens, /api/provider | Profile, tokens, drift actions, power mode, provider config |
| Archive | /api/archive, /api/archive/candidates | List, restore, archive-all |

## Billing (Token Budget)

| Tier | Price | Monthly Tokens |
|------|-------|---------------|
| Free | $0 | ~60,000 |
| Pro | $20-30 | 500,000 |
| Small Pack | $5 | +100,000 |
| Large Pack | $20 | +500,000 |

Token costs: Gemini 1x/3x, Claude 10x/30x, Local 0x
Localhost APIs always free (bypass tier guard)

## Tests: 588/588

| Category | Count |
|----------|-------|
| Foundation + Hardening | ~125 |
| Ingestion | ~48 |
| Wiki | ~43 |
| Search + Briefs | ~44 |
| Identity + Discovery | ~46 |
| Salience + Drift + Inference | ~63 |
| Cloud Pipeline | ~29 |
| API Integration | 18 |
| Edge Cases | 39 |
| Token Budget | 49 |
| **Total** | **588** |

## Decisions (52)

D-001..D-035: Earlier phases
D-036: Power Mode = Eco/Turbo
D-037: .NET sidecar as inference router
D-038: Installer variants (Standard/Offline/Runtime-Dependent)
D-039: Model at %LOCALAPPDATA%/Engram/models/
D-040: Vulkan fallback: discrete GPU → iGPU → CPU+SIMD
D-041: Min hardware: 8GB RAM, quad-core, Win10 64-bit
D-042: CopilotKit runtimeUrl → .NET sidecar
D-043: Tauri spawns .NET sidecar as child process
D-044: Discovery interview on first launch
D-045: Intervention policy gates proactive actions
D-046: OpenAI-compatible provider (any API key)
D-047: Localhost APIs always free
D-048: Token budget: Free ~60K, Pro 500K
D-049: Token pricing: Gemini 1x/3x, Claude 10x/30x
D-050: Token packs: small 100K $5, large 500K $20
D-051: Atomic TryReserve prevents race conditions
D-052: WikiNodeStore.Delete method added
