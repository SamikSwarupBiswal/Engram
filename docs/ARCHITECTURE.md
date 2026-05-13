# Engram Architecture

High-level architecture overview for the Engram personal semantic operating layer.

## Design Principles

1. **Local-first:** All data lives on the user's machine. Cloud is opt-in and audited.
2. **Append-only history:** Raw events are immutable. No destructive edits.
3. **Source-linked memory:** Every wiki fact traces back to raw event evidence.
4. **Replaceable providers:** OCR, models, browser automation, and workspace connectors sit behind interfaces.
5. **Consent-driven:** Sensitive capture sources are disabled by default. Excluded apps are never captured.

## Layer Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    User Surfaces                         │
│  Engram.Tray (system tray)  │  Engram.Search (Alt+Space)│
├─────────────────────────────────────────────────────────┤
│                    Intelligence Layer                     │
│  Engram.Memory (wiki, metabolizer, drift, salience)      │
│  Engram.Agents (research, synthesis, automation)         │
├─────────────────────────────────────────────────────────┤
│                    Ingestion Layer                        │
│  Engram.Connectors (file, clipboard, OCR, GWS, browser) │
├─────────────────────────────────────────────────────────┤
│                    Service Layer                         │
│  Engram.Service (background process, scheduling, routing)│
├─────────────────────────────────────────────────────────┤
│                    Storage Layer                         │
│  Engram.Store (.engram/ local workspace, raw events)     │
├─────────────────────────────────────────────────────────┤
│                    Cloud Layer (Pro Tier)                 │
│  Engram.CloudGateway (model routing, cost controls)      │
│  Engram.CleanCache (shared non-private research cache)   │
│  Engram.Sync (encrypted multi-device continuity)         │
└─────────────────────────────────────────────────────────┘
```

## Data Flow

```
[Capture Sources] → [Raw Events] → [Wiki Memory] → [Search/Briefs]
       │                  │              │                │
   File watcher      .engram/raw/    .engram/wiki/    Alt+Space
   Clipboard         YYYY-MM-DD/     *.md nodes       Morning brief
   Active window     [event_id].json index.md         Evening brief
   OCR/Screenshot                   user_identity.md
   GWS metadata                     priorities.md
```

## Local Store Layout (.engram/)

```
.engram/
├── raw/              # Immutable event history
│   └── YYYY-MM-DD/   # Daily partitions
│       └── [event_id].json
├── wiki/             # Metabolized Markdown memory
│   ├── *.md          # Entity nodes (people, projects, goals)
│   ├── index.md      # Navigation map
│   ├── user_identity.md
│   ├── priorities.md
│   └── anti_goals.md
├── runs/             # Agent run logs
│   └── [run_id]/
│       ├── log.md
│       └── state.json
├── config/           # Local configuration
├── logs/             # Service and diagnostic logs
└── archives/         # Decayed/stale wiki nodes
```

## Project Layout (src/)

| Project | Purpose | Phase |
|---------|---------|-------|
| Engram.Store | Local store library, raw event writer, workspace init | 1 |
| Engram.Cli | Developer CLI, init/replay commands | 1 |
| Engram.Service | Background Windows process, scheduling, routing | 3+ |
| Engram.Tray | System tray UI (context, credits, pause) | 5 |
| Engram.Search | Alt+Space semantic search surface | 5 |
| Engram.Connectors | File watcher, clipboard, active-window, OCR, GWS | 3, 9 |
| Engram.Memory | Raw-to-wiki metabolizer, salience, drift detector | 4, 6, 7 |
| Engram.Agents | Research runner, synthesis, computer-use automation | 10, 11 |
| Engram.CloudGateway | Model routing, cost controls, managed credit pooling | 8 |
| Engram.CleanCache | Shared non-private research cache | 8 |
| Engram.Sync | Encrypted multi-device sync | 12 |

## Provider Interfaces

All external systems sit behind interfaces:

- **IOcrProvider** — Windows Copilot Runtime first, dev fallback
- **ILocalModelProvider** — Phi/Copilot Runtime, mock for CI
- **ICloudModelProvider** — Gemini/Claude behind routing interface
- **IBrowserAutomationProvider** — Playwright first, Computer Use API later
- **IWorkspaceProvider** — Google Workspace first, M365 later

## Compute Tiering

| Layer | Type | Purpose |
|-------|------|---------|
| Perception | Local SLM | "What is on screen right now?" |
| Reasoning | Cloud VLM | "Why does this matter to the user?" |
| Drift Engine | Hybrid | Compare live behavior to priorities |
| Intervention | Local | Decide notification vs card delivery |

## Security Model

- AES-256 encryption at rest (Phase 12)
- Consent defaults: all sensitive capture disabled
- No raw screenshots/clipboard/email sent to cloud by default
- Cloud calls: audited with reason, provider, payload summary, cost
- Automation: read-only first, approval required for risky actions
- Export/delete: user can purge all Engram data
