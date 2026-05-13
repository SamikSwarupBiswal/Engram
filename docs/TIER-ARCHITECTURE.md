# Engram Tier Architecture

## Overview

Engram ships in two tiers. All code we build must work for BOTH tiers —
the free tier uses the same codebase, just without cloud features enabled.

## Free Tier (The Local Hub) — $0/mo

Everything runs locally on the user's NPU/CPU. No API keys, no cloud, no payment.

### What Ships (Phases 1-7):
- Local raw event store (.engram/raw/)
- Local wiki memory (.engram/wiki/)
- Local file/clipboard/active-window capture
- Local semantic search (Alt+Space)
- Morning/evening briefs
- Identity hardening (Discovery SOP)
- Salience decay + drift detection

### Intelligence:
- Windows Copilot Runtime (local SLM)
- Phi-4 for local reasoning
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

## Pro Tier — $20-$30/mo

Cloud-enhanced intelligence with managed API keys. No user-provided keys.

### What Ships (Phases 8-12):
- Cloud model routing (Gemini 3 Flash + Claude 4.5)
- Google Workspace metadata ingestion
- Agentic research (browser automation)
- Computer-use automation
- Encrypted cloud sync

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

### Research:
- Multi-tab autonomous browsing via Playwright
- Source collection + quality filtering
- Cited wiki summaries
- Side-by-side layout

### Automation:
- Read-only Windows automation first
- Write/action automation with approval gates
- Every action logged with timestamp + target + rationale

### Memory:
- Everything in Free tier
- + Encrypted cloud sync
- + Multi-device continuity
- + Shared clean cache for common research

## Cost Management (Managed Credit Pooling)

Users NEVER provide API keys. Engram manages a credit pool.

### Model Routing:
| Task | Model | Cost |
|------|-------|------|
| Routine ingestion/summarization | Gemini 3 Flash | ~$0.075/M input |
| Complex research | Claude 4.5 Sonnet | ~$3/M input |
| Computer-use automation | Claude 4.5 Sonnet | ~$3/M input |

### Cost Reduction Strategies:
1. Local filtering: SLM pre-processes screenshots, sends only
   UI state changes to cloud (90% token reduction)
2. Semantic caching: common research topics cached in CleanCache
3. Batch processing: group similar queries
4. Rate limiting: per-user budget caps

### Revenue Model:
- Pro tier: $20-$30/mo subscription
- Energy Units: free users get 3/week Pro trials
- Managed credit pooling: Engram buys API credits in bulk

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
