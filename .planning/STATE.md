# Engram State

**Status:** All 12 phases implemented, 747 tests, installer sidecar issue remains
**Last Activity:** 2026-05-18
**Tests:** 747/747 passing
**Latest Commit:** dbf942a
**Git:** master (pushed)

## What Engram Is

Engram is a Windows-first personal semantic operating layer. It captures everything you do on your computer, metabolizes it into a structured wiki of knowledge, and lets you search, recall, and reason over your entire digital life.

Desktop app (Tauri v2 + React) with .NET 8 API sidecar. Install via .exe installer.

## Architecture

```
User double-clicks Engram
  → Tauri shell (Rust, ~10MB)
    → Spawns .NET API sidecar on 127.0.0.1:5000
    → Loads React frontend
    → Frontend connects to sidecar automatically
    → All 6 views work (Chat, Search, Wiki, Timeline, Settings, Archive)
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
| Encryption | AES-256-GCM |
| Search | DuckDuckGo HTML |
| OAuth | Google Workspace |

## Project Structure

```
Engram/
├── src/
│   ├── Engram.Store/          Core library (all logic)
│   │   ├── Agent/             Research agent, browser, citations
│   │   ├── Automation/        Action executor, permission gate
│   │   ├── Billing/           Token budget, pricing
│   │   ├── Capture/           Event capture (clipboard, files, windows)
│   │   ├── Cloud/             Cloud pipeline, providers, audit
│   │   ├── Google/            Gmail, Calendar, Drive metadata
│   │   ├── Identity/          User profile, discovery, intervention
│   │   ├── Inference/         LLamaSharp, GPU detection, model mgmt
│   │   ├── Salience/          Decay scoring, drift detection
│   │   ├── Search/            TF-IDF search, brief generator
│   │   ├── Security/          Encryption, export, delete, sync
│   │   ├── Validation/        Input validation, sanitization
│   │   └── Wiki/              Wiki node store, serializer
│   ├── Engram.Cli/            Developer CLI
│   ├── Engram.Api/            ASP.NET Minimal API (sidecar)
│   └── Engram.App/            Tauri + React frontend
│       ├── src/                React components (10 views)
│       ├── src-tauri/          Rust shell + sidecar config
│       ├── installer.nsi       NSIS installer script
│       └── build-*.ps1         Build scripts
├── tests/
│   └── Engram.Store.Tests/    747 tests
└── .planning/                 All planning docs
```

## API Endpoints (64)

```
GET  /                              Health
GET  /api/search                    Search wiki
GET  /api/wiki                      List wiki nodes
GET  /api/wiki/:id                  Get single node
GET  /api/brief                     Morning/evening brief
GET  /api/events                    Raw event history
GET  /api/status                    Workspace stats
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
GET  /api/power-mode                Current mode
GET  /api/tokens                    Token budget status
GET  /api/tokens/pricing            Plans, packs, rates
GET  /api/provider                  Provider config
GET  /api/security/status           Encryption configured?
GET  /api/automation/log            Action history
GET  /api/research/:id              Research run details
GET  /api/research                  List research runs
GET  /api/gws/status                Google connection status
GET  /api/gws/url                   OAuth URL
GET  /api/gws/emails                Gmail metadata
GET  /api/gws/events                Calendar metadata
GET  /api/gws/files                 Drive metadata
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
POST /api/security/setup            Setup encryption
POST /api/security/unlock           Unlock encryption
POST /api/security/change-password  Change password
POST /api/security/export           Export all data
POST /api/security/import           Import data
POST /api/security/delete           Delete all data
POST /api/automation/plan           Create action plan
POST /api/automation/approve-all    Approve all pending
POST /api/automation/deny-all       Deny all pending
POST /api/automation/execute        Run approved actions
POST /api/automation/rollback       Rollback last N
POST /api/research/start            Start research
POST /api/research/:id/resume       Resume research
POST /api/research/:id/cancel       Cancel research
POST /api/gws/connect               OAuth token exchange
POST /api/gws/disconnect            Revoke access
POST /api/gws/sync                  Sync all metadata
POST /v1/chat/completions           Chat (real inference)
PUT  /api/identity                  Update profile
```

## Frontend Views (10)

| View | API Connections | Status |
|------|----------------|--------|
| Chat | /v1/chat/completions, /api/tokens/check, /api/model/* | Connected |
| Search | /api/search | Connected |
| Wiki | /api/wiki, /api/salience | Connected |
| Timeline | /api/events | Connected |
| Settings | /api/status, /api/identity, /api/drift, /api/tokens, /api/provider, /api/security, /api/brief, /api/gws | Connected |
| Archive | /api/archive, /api/archive/candidates | Connected |
| Research | /api/research/* | Connected |
| Automation | /api/automation/* | Connected |
| ModelDownloadBar | /api/model/status, /api/model/download, /api/model/load | Connected |
| DiscoveryInterview | /api/discovery/status, /api/discovery | Connected |

## Tests: 747/747

| Category | Count |
|----------|-------|
| Foundation + Hardening | ~125 |
| Ingestion | ~48 |
| Wiki | ~43 |
| Search + Briefs | ~44 |
| Identity + Discovery | ~46 |
| Salience + Drift | ~30 |
| Cloud Pipeline | ~29 |
| Token Budget | ~49 |
| Google Workspace | ~46 |
| Research Agent | ~38 |
| Automation | ~37 |
| Security | ~38 |
| API Integration | ~18 |
| Edge Cases | ~39 |
| Inference | ~19 |
| **Total** | **747** |

## Billing (Token Budget)

| Tier | Price | Monthly Tokens |
|------|-------|---------------|
| Free | $0 | ~60,000 |
| Pro | $20-30 | 500,000 |
| Small Pack | $5 | +100,000 |
| Large Pack | $20 | +500,000 |

Token costs: Gemini 1x/3x, Claude 10x/30x, Local 0x
Localhost APIs always free (bypass tier guard)

## Known Issues

1. **Installer sidecar crash** — Tauri app crashes on launch because .NET sidecar can't find DLLs. Root cause: NSIS installs DLLs but Tauri's shell command doesn't resolve them. Fix in progress.

2. **Model not loading on WSL** — LLamaSharp native DLLs are Windows-only. Works on real Windows.

3. **Flaky Debouncer test** — Pre-existing timing-sensitive test fails intermittently.

## Decisions (52+)

D-001..D-035: Earlier phases
D-036: Power Mode = Eco/Turbo
D-037: .NET sidecar as inference router
D-038: Installer variants
D-039: Model at %LOCALAPPDATA%/Engram/models/
D-040: Vulkan fallback chain
D-041: Min hardware requirements
D-042: CopilotKit runtimeUrl
D-043: Tauri spawns .NET sidecar
D-044: Discovery interview on first launch
D-045: Intervention policy gates proactive actions
D-046: OpenAI-compatible provider
D-047: Localhost APIs always free
D-048: Token budget system
D-049: Token pricing model
D-050: Token packs
D-051: Atomic TryReserve
D-052: WikiNodeStore.Delete
D-053: AES-256-GCM encryption
D-054: PBKDF2 key derivation (100k iterations)
D-055: Secure wipe before delete
D-056: Hash-based sync dedup
D-057: DuckDuckGo for research (no API key)
D-058: Permission gate auto-approves safe actions
