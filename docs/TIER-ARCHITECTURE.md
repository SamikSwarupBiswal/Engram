# Engram Tier Architecture

## Overview

Engram ships in two tiers. All code we build must work for BOTH tiers —
the free tier uses the same codebase, just without cloud features enabled.

## Onboarding Flow

1. User downloads Engram installer (Windows .exe/.msi)
2. Installer installs: Engram application + local SLM (Phi-4 via Windows Copilot Runtime)
3. First launch → Discovery Skill activates
   - 15-minute AI-guided interview
   - Topics: anti-goals, comfort triggers, recurring anxieties
   - Output: `user.md` (identity profile stored in `.engram/`)
4. Engram enters Free tier by default
5. Conversational GUI available immediately — user can start chatting
6. Pro upgrade available in-app ($20-30/mo, 1 month activates instantly)

## Conversational Interface

Engram provides a ChatGPT-like GUI on top of its backend:

- **Chat window**: Natural language queries ("What did we discuss?", "Summarize my week")
- **Streaming responses**: Token-by-token via SLM (local) or cloud VLM (Pro)
- **Sidebar**: Search, timeline, wiki navigation
- **Backend**: Same Engram.Store services used by CLI — no code duplication
- **Architecture**: GUI → API server → Engram.Store (interface-based, decoupled)

## Free Tier (The Local Hub) — $0/mo

Everything runs locally on the user's NPU/CPU. No API keys, no cloud, no payment.

### What Ships (Phases 1-7):
- Local raw event store (.engram/raw/)
- Local wiki memory (.engram/wiki/)
- Local file/clipboard/active-window capture
- Local semantic search (Alt+Space)
- Morning/evening briefs
- Identity hardening (Discovery SOP → user.md)
- Salience decay + drift detection
- Conversational GUI (local SLM)

### Intelligence:
- Windows Copilot Runtime (local SLM)
- **Tiered local inference (optimized for low-spec hardware):**
  - Embeddings (always on): all-MiniLM-L6-v2 (~80MB, semantic search)
  - Task SLM (on demand): Qwen2.5 0.5B (~0.5GB, classification/routing)
  - Reasoning SLM (on demand): Phi-4-mini (~2.5GB, summarization/QA)
- No cloud model calls

### Sensing:
- Local OCR (Windows Copilot Runtime)
- File watching (Downloads, Documents, Desktop)
- Clipboard monitoring (opt-in)
- Active window tracking (opt-in)

### Research:
- Search links only (manual research)
- No autonomous browsing

### Automation:
- None (read-only observation)

### Memory:
- Single device, local storage
- No sync

### Revenue Path:
- Energy Units: 3 free Pro trials per week
- Conversion funnel to Pro tier
- In-app upgrade prompt after Energy Units consumed

## Pro Tier — $20-$30/mo

Cloud-enhanced intelligence with managed API keys. No user-provided keys.

### What Ships (Phases 8-12):
- Cloud model routing (Gemini 3 Flash + Claude 4.5)
- Google Workspace metadata ingestion
- Agentic research (browser automation)
- Computer-use automation
- Encrypted cloud sync + multi-device continuity
- Deep reasoning and conflict analysis
- Multi-tab synthesis and structured reports

### Intelligence:
- Hybrid: local SLM + cloud VLM
- Gemini 3 Flash for 90% of routine work (cheap)
- Claude 4.5 Sonnet for complex research/automation (expensive)
- Model routing based on task complexity

### Sensing:
- Everything in Free tier
- + Gmail metadata (subjects, senders, dates)
- + Calendar metadata (events, attendees)
- + Drive metadata (file names, sharing)
- + Microsoft 365 metadata (future)

### Research:
- Multi-tab autonomous browsing via Playwright
- Source collection + quality filtering
- Cited wiki summaries
- Side-by-side layout
- Structured reports with citations

### Automation:
- Read-only Windows automation first
- Write/action automation with approval gates
- Every action logged with timestamp + target + rationale
- Full "Computer Use" capability

### Memory:
- Everything in Free tier
- + Encrypted cloud sync
- + Multi-device continuity
- + Shared clean cache for common research

### Interventions:
- Predictive pattern analysis
- Proactive resolutions (not just alerts)
- Conflict detection across data sources

## Side-by-Side Comparison

| Feature Domain | Free Tier (The Local Hub) | Pro Tier ($20-$30/mo) |
|---|---|---|
| Intelligence Model | 100% Local SLM (Phi-4/Copilot Runtime) | Hybrid SLM + Cloud VLM (Claude/Gemini) |
| Primary Logic | Local Perception & Search | Deep Reasoning & Conflict Analysis |
| Sensing Capabilities | Local OCR & File Watching | GWS/365 Metadata Cloud Ingestion |
| Research Power | Search Links (Manual Research) | Multi-tab Synthesis & Structured Reports |
| Automation | None (Read-only observation) | Full "Computer Use" & Executive Action |
| Memory Sync | Single Device (Local) | Encrypted Cloud Sync & Multi-Device Continuity |
| Interventions | Local Drift Alerts & Notifications | Predictive Pattern Analysis & Resolutions |
| Cost Basis | $0 / mo (Runs on User NPU) | Managed Credit Pooling (Managed API) |

## Cost Management (Managed Credit Pooling)

Users NEVER provide API keys. Engram manages a credit pool.

### Model Routing:
| Task | Model | Cost |
|------|-------|------|
| Routine ingestion/summarization | Gemini 3 Flash | ~$0.075/M input |
| Complex research | Claude 4.5 Sonnet | ~$3/M input |
| Computer-use automation | Claude 4.5 Sonnet | ~$3/M input |

### Cost Reduction Strategies:
1. **Local filtering**: SLM pre-processes screenshots, sends only UI state changes to cloud (85-90% token reduction)
2. **Semantic caching**: common research topics cached in CleanCache globally
3. **Batch processing**: group similar queries
4. **Rate limiting**: per-user budget caps
5. **Model routing**: Gemini for 90% routine, Claude only for complex tasks

### Revenue Model:
- Pro tier: $20-$30/mo subscription
- Energy Units: free users get 3/week Pro trials
- Managed credit pooling: Engram buys API credits in bulk
- No user API keys → lower adoption barrier

## Energy Units System

Free tier users receive 3 Energy Units per week.

### What an Energy Unit Does:
- 1 Energy Unit = 1 Pro-level action (deep research, complex QA, automation preview)
- Enough to demonstrate Pro value without replacing subscription
- Resets every Monday at 00:00 local time

### Conversion Funnel:
- User consumes Energy Units → experiences Pro quality
- After units exhausted → "Upgrade to Pro for unlimited access"
- In-app purchase flow → 1 month Pro activates instantly

## Architecture Requirements

### Provider Interfaces (already built):
- IOcrProvider: Windows Copilot Runtime (local) / mock (dev)
- ILocalModelProvider: Phi-4 (local) / mock (dev)
- ICloudModelProvider: Gemini/Claude (cloud) — Phase 8
- IBrowserAutomationProvider: Playwright — Phase 10
- IWorkspaceProvider: Google Workspace — Phase 9

### Tier Gating:
- Every cloud feature sits behind a tier check
- Free tier: cloud providers return "not available"
- Pro tier: cloud providers route to actual APIs
- No code duplication — same codebase, different provider configs

### Privacy Rules:
- No raw screenshots/clipboard/email sent to cloud by default
- Cloud calls: audited with reason, provider, payload summary, cost
- Private data requires explicit policy approval
- User can disable cloud features and stay on free tier forever

### Installer Requirements:
- Windows .exe/.msi installer
- Bundles: Engram app + local SLM models + Windows Copilot Runtime integration
- SLM models downloaded/cached on first install (~3GB total)
- No internet required for Free tier after install
- Pro tier activation requires internet (subscription validation)

## Phase-to-Tier Mapping

| Phase | Tier | Cloud Required |
|-------|------|----------------|
| 1. Foundation + Raw Store | Free | No |
| 2. Immutable Raw Store | Free | No |
| 3. Local Ingestion MVP | Free | No |
| 4. Wiki Memory | Free | No |
| 5. Local Search + Briefs | Free | No |
| 6. Identity Hardening | Free | No |
| 7. Salience + Drift | Free | No |
| 8. Cloud Reasoning | Pro | Yes |
| 9. Google Workspace | Pro | Yes |
| 10. Agentic Research | Pro | Yes |
| 11. Computer-Use | Pro | Yes |
| 12. Encryption + Sync | Pro | Yes |

## Non-Negotiables

- Free tier MUST be complete and useful on its own
- Pro features MUST NOT break free tier functionality
- Cloud calls MUST be audited and policy-gated
- User MUST be able to disable all cloud features
- No raw private data sent to cloud without explicit approval
- Managed credit pooling — no user API keys ever
- SLM must run on low-spec hardware (4GB RAM minimum)
- Discovery Skill interview must complete before first use
