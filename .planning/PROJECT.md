# Engram

## Vision

Engram is a Windows-first personal semantic operating layer. It turns a user's local digital activity into a durable, source-linked memory layer that can be searched, briefed, and eventually used by agents to help with research and operating-system tasks.

Engram ships as a downloadable desktop application with a ChatGPT-like conversational GUI. On first launch, it runs a Discovery Skill interview to build a personalized user profile. Free tier runs 100% locally on the user's NPU. Pro tier adds cloud-enhanced intelligence via managed credit pooling — no user API keys.

## Architecture

Engram is a **desktop app**, not a web app. Tauri v2 (Rust) shell auto-spawns a .NET 8 API sidecar as a child process on launch. The React frontend connects to the sidecar at 127.0.0.1:5000 internally. The user never sees the backend.

```
┌─────────────────────────────────────────────────────┐
│  Engram Desktop App (Tauri v2)                      │
│                                                     │
│  ┌─────────────────────────────────────────────┐    │
│  │  React + Tailwind + shadcn/ui               │    │
│  │  ChatGPT-style sidebar, 5 views             │    │
│  │  → connects to 127.0.0.1:5000               │    │
│  └──────────────────┬──────────────────────────┘    │
│                     │ HTTP                          │
│  ┌──────────────────┴──────────────────────────┐    │
│  │  .NET 8 API Sidecar (child process)         │    │
│  │  16 endpoints                               │    │
│  │  Engram.Store services                      │    │
│  └─────────────────────────────────────────────┘    │
│                                                     │
│  Tauri spawns sidecar on app start                  │
│  Tauri kills sidecar on app close                   │
└─────────────────────────────────────────────────────┘
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Shell | Tauri v2 (Rust) — ~10MB, native Windows, system tray |
| UI | React 19 + TypeScript + Tailwind CSS |
| Backend | .NET 8 ASP.NET Minimal API (sidecar) |
| Local Store | .engram/ directory (JSON + Markdown) |
| Tests | xUnit (516 tests) |
| Build | Vite 6, cargo, dotnet publish |

## Onboarding Flow

1. User downloads Engram installer (.exe/.msi)
2. Installs to %LOCALAPPDATA%/Engram/
3. Opens from Start Menu / Desktop shortcut
4. Tauri shell starts → spawns .NET sidecar
5. First launch → Discovery Interview (7-step flow)
   - Name, Goals, Comfort Triggers, Anxieties, Priorities, Anti-Goals
   - Saves to user_identity.md, priorities.md, anti_goals.md
6. Enters Free tier by default
7. Chat/Search/Wiki/Timeline/Settings all available
8. Pro upgrade available in-app ($20-30/mo)

## API Endpoints (16)

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
POST /api/discovery                 Run discovery
POST /api/intervention/check        Evaluate intervention
POST /v1/chat/completions           Chat inference
PUT  /api/identity                  Update profile
```

## Frontend Views (5)

| View | API Connection | Features |
|------|---------------|----------|
| Chat | /v1/chat/completions | localStorage persistence, session management, real API |
| Search | /api/search | Loading/error states, result cards with scores |
| Wiki | /api/wiki | Node type filters, salience display, grid layout |
| Timeline | /api/events | Chronological list, source/type/time display |
| Settings | /api/status, /api/identity, /api/drift | Profile, workspace stats, identity chips, power mode |

## Phases Complete (1-8)

| Phase | What | Tests |
|-------|------|-------|
| 1 | Foundation + raw store | ~125 |
| 2 | Hardened raw store | included above |
| 3 | Local ingestion MVP | ~48 |
| 4 | Wiki memory | ~43 |
| 5 | Search + Desktop shell | ~44 |
| 6 | Identity + Discovery UI | ~46 |
| 7 | Salience + drift | ~44 |
| 8 | Cloud reasoning pipeline | ~29 |
| API tests | Endpoint integration | 18 |
| Edge cases | Validation, concurrency | 39 |
| **Total** | | **516** |

## Current State

- **Phases 1-8 complete.** 516/516 tests passing.
- Desktop app installed on Windows (Start Menu entry)
- Installers: .exe (2.46 MB), .msi (3.69 MB)
- **Next: Phase 9 — Google Workspace Metadata Ingestion**

## Non-Negotiables

- Sensitive capture sources are opt-in (all OFF by default)
- Excluded apps never captured
- Raw events append-only and traceable
- Wiki facts link back to raw event evidence
- Cloud calls audited and policy-gated
- Proactive interventions read identity constraints first
- Every phase passes quality gate
- Free tier complete and useful on its own
- No raw private data sent to cloud
- **Engram is a desktop app, not a web app**
