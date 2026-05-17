# Engram State

**Status:** Phase 8 complete — Token budget system + provider config
**Current Phase:** Phase 8 - Cloud Reasoning + Billing (DONE)
**Next Phase:** Phase 9 - Google Workspace Metadata Ingestion [PRO TIER]
**Last Activity:** 2026-05-17
**Total Tests:** 588/588 passing
**Latest Commit:** f9e7d38 (feat: wire token budget + provider config connections)
**Git Status:** master (not pushed)

## Architecture

Engram is a desktop app. Not a web app, not a localhost dev server.

```
User clicks Engram icon
  → Tauri shell (Rust, ~10MB)
    → Spawns .NET API sidecar on 127.0.0.1:5000
    → Loads React frontend
    → Frontend connects to sidecar automatically
    → Chat/Search/Wiki/Timeline/Settings/Archive all work
    → User closes app → sidecar killed
```

## API Endpoints (32 total)

```
GET  /                              Health
GET  /api/status                    Workspace stats
GET  /api/search?q=                 Search wiki
GET  /api/wiki                      List wiki nodes
GET  /api/wiki/:id                  Get single node
GET  /api/brief?time=               Morning/evening brief
GET  /api/events                    Raw event history
GET  /api/identity                  User profile
GET  /api/identity/anti-goals       Anti-goals list
GET  /api/identity/priorities       Priorities list
GET  /api/discovery/status          Discovery complete?
GET  /api/drift                     Drift alerts
GET  /api/drift/stats               Alert statistics
GET  /api/salience                  Salience scores for all nodes
GET  /api/archive                   List archived nodes
GET  /api/archive/candidates        Nodes eligible for archival
GET  /api/model/status              Model + GPU info
GET  /api/power-mode                Current mode (eco/turbo)
GET  /api/tokens                    Token budget status
GET  /api/tokens/pricing            Plans, packs, rates
GET  /api/provider                  Provider config
POST /api/discovery                 Run discovery interview
POST /api/intervention/check        Evaluate intervention policy
POST /api/drift/:id/accept          Accept drift alert
POST /api/drift/:id/dismiss         Dismiss drift alert
POST /api/drift/:id/convert         Convert to wiki update
POST /api/archive/stale             Archive all stale nodes
POST /api/archive/:id/restore       Restore from archive
POST /api/model/download            Download Phi-4-mini
POST /api/model/load                Load model into memory
POST /api/model/unload              Unload model
POST /api/power-mode                Switch eco/turbo
POST /api/tokens/check              Check token budget
POST /api/tokens/pack               Buy token pack
POST /api/tokens/tier               Change tier
POST /api/provider                  Save provider config
POST /v1/chat/completions           Chat (real inference)
PUT  /api/identity                  Update user profile
```

## Billing System (Token Budget)

| Tier | Price | Monthly Tokens | Notes |
|------|-------|---------------|-------|
| Free | $0/mo | ~60,000 | 3 Energy Units/week |
| Pro | $20-30/mo | 500,000 | Managed credits |
| Small Pack | $5 | +100,000 | One-time purchase |
| Large Pack | $20 | +500,000 | One-time purchase |

Token costs per provider:
| Provider | Input Cost | Output Cost |
|----------|-----------|-------------|
| Gemini Flash | 1x | 3x |
| Claude Sonnet | 10x | 30x |
| Local (Ollama) | 0x | 0x |

## Frontend Views (6)

| View | API Connections | Features |
|------|----------------|----------|
| Chat | /v1/chat/completions, /api/tokens/check | Token budget check, localStorage persistence |
| Search | /api/search | Loading/error states, result cards |
| Wiki | /api/wiki | Node type filters, salience, grid layout |
| Timeline | /api/events | Chronological list, source badges |
| Settings | /api/status, /api/identity, /api/drift, /api/tokens, /api/provider | Full dashboard: profile, tokens, drift actions, power mode, provider config |
| Archive | /api/archive, /api/archive/candidates, /api/archive/stale | List, restore, archive-all-stale |

## Tests: 588/588

| Category | Count |
|----------|-------|
| Phase 1-2 (Foundation + Hardening) | ~125 |
| Phase 3 (Ingestion) | ~48 |
| Phase 4 (Wiki) | ~43 |
| Phase 5 (Search + Briefs) | ~44 |
| Phase 6 (Identity) | ~46 |
| Phase 7 (Salience + Drift + Inference) | ~63 |
| Phase 8 (Cloud) | ~29 |
| API Integration Tests | 18 |
| Edge Case Tests | 39 |
| Token Budget Tests | 49 |
| **Total** | **588** |

## Decisions Log

- D-001..D-035: Earlier phases
- D-036: Power Mode = Eco (local) / Turbo (cloud)
- D-037: .NET sidecar as inference router
- D-038: Installer ~130MB standard, ~2.4GB offline
- D-039: Model at %LOCALAPPDATA%/Engram/models/
- D-040: Vulkan fallback: discrete GPU → iGPU → CPU+SIMD
- D-041: Min hardware: 8GB RAM, quad-core, Win10 64-bit
- D-042: CopilotKit runtimeUrl → .NET sidecar
- D-043: Tauri spawns .NET sidecar as child process
- D-044: Discovery interview on first launch (7 steps)
- D-045: Intervention policy gates all proactive chat actions
- D-046: OpenAI-compatible provider (any API key)
- D-047: Localhost APIs always free (bypass tier guard)
- D-048: Token budget: Free ~60K/mo, Pro 500K/mo
- D-049: Token pricing: Gemini 1x/3x, Claude 10x/30x, Local 0x
- D-050: Token packs: small 100K $5, large 500K $20
- D-051: Atomic TryReserve prevents race conditions
- D-052: WikiNodeStore.Delete method added (production feature)
