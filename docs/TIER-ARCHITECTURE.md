# Engram Tier Architecture

## Overview

Engram ships in two tiers. All code we build must work for BOTH tiers —
the free tier uses the same codebase, just without cloud features enabled.

This is a PRODUCTION product, not an MVP. Target audience: users with
decent laptops (8GB+ RAM, modern CPU, optional discrete GPU).

## Tech Stack

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| Frontend Shell | Tauri (Rust) | ~10MB installer, native Windows perf, system tray, auto-update |
| UI Framework | React | Huge ecosystem, works with Tauri, good for chat interfaces |
| Styling | Tailwind CSS + shadcn/ui | Rapid development, accessible components, copy-paste ownership |
| AI Chat UI | CopilotKit | Built-in streaming, markdown rendering, tool calling hooks |
| Backend Sidecar | .NET 8 (ASP.NET Minimal API) | Existing Engram.Store services, interface-based, decoupled |
| Local Inference | LLamaSharp (Vulkan backend) | Native .NET, GPU acceleration on AMD/Intel/NVIDIA, no CUDA dependency |
| Brain (Free) | Phi-4-mini GGUF Q4_K_M | 3.8B params, ~2.2GB, runs on decent laptops |
| Cloud Inference | Gemini 3 Flash + Claude 4.5 Sonnet | Managed credit pooling, no user API keys |

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Tauri Shell (~10-20MB)                                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  React + Tailwind + shadcn/ui + CopilotKit            │  │
│  │  Chat window | Sidebar (search, timeline, wiki)       │  │
│  │  runtimeUrl="http://localhost:5000"                    │  │
│  └────────────────────┬──────────────────────────────────┘  │
│                       │ HTTP/SSE                            │
│  ┌────────────────────┴──────────────────────────────────┐  │
│  │  .NET Sidecar (Engram.Api)                            │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │  Inference Router                               │  │  │
│  │  │  POST /v1/chat/completions                      │  │  │
│  │  │  ├─ Eco Mode  → LLamaSharp (local Phi-4-mini)  │  │  │
│  │  │  └─ Turbo Mode → Gemini/Claude cloud pipeline  │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │  API Endpoints                                  │  │  │
│  │  │  /api/search  /api/brief  /api/wiki             │  │  │
│  │  │  /api/events  /api/status /api/identity         │  │  │
│  │  │  /api/stream (WebSocket for real-time)          │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  │                                                        │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │  LLamaSharp Engine (Vulkan backend)             │  │  │
│  │  │  Phi-4-mini GGUF Q4_K_M loaded in-process      │  │  │
│  │  │  Vulkan auto-detects: iGPU, discrete GPU, CPU   │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Engram.Store (existing services, shared library)     │  │
│  │  SearchIndex | WikiIndex | BriefGenerator             │  │
│  │  CloudCallPipeline | IdentityStore | CaptureOrch      │  │
│  └───────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

## Power Mode Toggle

User-facing setting that controls inference routing:

```
┌─────────────────────────────────────────┐
│  ⚡ Power Mode                          │
│                                         │
│  ○ Eco Mode    Local Phi-4-mini (3B)   │
│    Zero cost, works offline, ~3GB RAM   │
│    Runs on GPU via Vulkan               │
│                                         │
│  ○ Turbo Mode  Cloud API (Pro)          │
│    Gemini 3 Flash / Claude 4.5 Sonnet   │
│    Complex tasks, deep reasoning        │
│    Requires internet + Pro subscription │
└─────────────────────────────────────────┘
```

### Eco Mode (Default, Free Tier):
- LLamaSharp loads Phi-4-mini GGUF Q4_K_M into memory
- Vulkan backend auto-detects best GPU (iGPU or discrete)
- All inference local, zero network calls
- Works offline, works on airplane, works in bunker
- ~3GB RAM footprint, acceptable on 8GB+ laptops
- If no Vulkan GPU detected, falls back to CPU (slower but works)

### Turbo Mode (Pro Tier):
- Unlocked after subscription activation
- .NET sidecar routes to cloud pipeline (existing CloudCallPipeline)
- Gemini 3 Flash for 90% routine work
- Claude 4.5 Sonnet for complex reasoning
- Model routing already built in Phase 8

### Inference Router (.NET Sidecar):

```csharp
[Route("v1/chat/completions")]
[HttpPost]
public async Task<IActionResult> Chat([FromBody] ChatRequest req)
{
    var mode = await _settings.GetPowerMode();

    if (mode == PowerMode.Turbo && _license.IsPro())
    {
        // Cloud pipeline (Phase 8 — already built)
        return await _cloudPipeline.Forward(req);
    }

    // Eco mode — local LLamaSharp with Vulkan
    var response = await _llamaEngine.Complete(req);
    return Ok(response);
}
```

### CopilotKit Configuration (Frontend):

```tsx
<CopilotKit runtimeUrl="http://localhost:5000/api/copilotkit">
  <YourApp />
</CopilotKit>
```

CopilotKit talks to ONE endpoint. The .NET sidecar decides where the
"brain" is located. Frontend doesn't know or care.

## LLamaSharp + Vulkan Details

### Why LLamaSharp (not Ollama):

| Approach | Pros | Cons |
|----------|------|------|
| Ollama | Easy setup, auto model management | Separate process, IPC overhead, two things to manage |
| LLamaSharp | Native .NET, in-process, Vulkan GPU, direct memory control | More integration work |

**LLamaSharp wins** because:
- Runs IN the .NET sidecar process (no IPC)
- Vulkan backend = GPU acceleration on AMD, Intel, AND NVIDIA
- No CUDA dependency (works on any decent laptop with a GPU)
- Direct memory management, no separate process lifecycle
- One process, one memory model

### Vulkan GPU Detection:
1. Check for discrete GPU (NVIDIA/AMD) → use it
2. Fallback to integrated GPU (Intel/AMD APU) → use it
3. Fallback to CPU with SIMD → slower but works everywhere

### Model Format:
- Phi-4-mini GGUF Q4_K_M (~2.2GB)
- 4-bit quantization = good quality/speed tradeoff
- Tool calling support: use GGUF with tool-use chat template
- Fallback: if tool calling fails, use regex-based intent parsing

### Hardware Requirements:

| Spec | Minimum | Recommended |
|------|---------|-------------|
| RAM | 8GB | 16GB |
| CPU | Modern quad-core | Ryzen 5 / i5+ |
| GPU | None (CPU fallback) | GTX 1650+ / RX 5500+ / Intel Arc |
| Storage | 5GB free | 10GB free |
| OS | Windows 10 (64-bit) | Windows 11 |

## Onboarding Flow

1. User downloads Engram installer (~130MB, Windows .exe/.msi)
2. Installer installs:
   - Tauri shell + React frontend
   - .NET 8 self-contained sidecar
   - LLamaSharp + Vulkan native libraries
   - Engram.Store + all backend services
3. First launch → "Enable AI?" prompt
   - User clicks "Enable AI"
   - Downloads Phi-4-mini GGUF Q4_K_M (~2.2GB)
   - Progress bar shown during download
   - Model cached at `%LOCALAPPDATA%/Engram/models/`
4. Discovery Skill activates
   - 15-minute AI-guided interview
   - Topics: anti-goals, comfort triggers, recurring anxieties
   - Output: `user.md` (identity profile stored in `.engram/`)
5. Engram enters Free tier (Eco Mode) by default
6. Conversational GUI available immediately
7. Pro upgrade available in-app ($20-30/mo, 1 month activates instantly)

## Installer Bundle

### Standard Installer (~130MB):

| Component | Size |
|-----------|------|
| Tauri shell (Rust binary) | ~5-8 MB |
| React + Tailwind + shadcn (bundled JS/CSS) | ~2-5 MB |
| CopilotKit (bundled) | ~1-2 MB |
| .NET 8 self-contained runtime | ~60-80 MB |
| Engram.Store + Engram.Api DLLs | ~5-10 MB |
| LLamaSharp + Vulkan native libs | ~15-25 MB |
| Discovery Skill assets + config | ~1.5 MB |
| **Installer Total** | **~100-130 MB** |

### First-Run Download:

| Component | Size |
|-----------|------|
| Phi-4-mini GGUF Q4_K_M model | ~2.2 GB |

### Total Installed Size:

| Component | Size |
|-----------|------|
| Application + runtime | ~130 MB |
| SLM model | ~2.2 GB |
| .engram workspace (empty) | ~5 MB |
| **Total on Disk** | **~2.4 GB** |

### Installer Variants:

| Variant | Size | Use Case |
|---------|------|----------|
| Standard (recommended) | ~130 MB | Download model on first run |
| Offline Installer | ~2.4 GB | Enterprise, restricted networks, USB/SD card |
| .NET Runtime-Dependent | ~50 MB | If user already has .NET 8 installed |

### Comparison to Competitors:

| App | Installer | Installed |
|-----|-----------|-----------|
| **Engram (Standard)** | **~130 MB** | **~2.4 GB** |
| Obsidian | ~80 MB | ~300 MB |
| Notion | ~150 MB | ~500 MB |
| Electron apps (avg) | ~150 MB | ~400 MB |
| Ollama | ~200 MB | ~3+ GB |

## Conversational Interface

Engram provides a ChatGPT-like GUI on top of its backend:

- **Chat window**: Natural language queries ("What did we discuss?", "Summarize my week")
- **Streaming responses**: Token-by-token via LLamaSharp (Eco) or cloud API (Turbo)
- **Sidebar**: Search, timeline, wiki navigation
- **Backend**: Same Engram.Store services used by CLI — no code duplication
- **Architecture**: Tauri → React/CopilotKit → .NET sidecar → Engram.Store

## Free Tier (The Local Hub) — $0/mo

Everything runs locally. No API keys, no cloud, no payment.

### What Ships (Phases 1-7):
- Local raw event store (.engram/raw/)
- Local wiki memory (.engram/wiki/)
- Local file/clipboard/active-window capture
- Local semantic search (Alt+Space)
- Morning/evening briefs
- Identity hardening (Discovery SOP → user.md)
- Salience decay + drift detection
- Conversational GUI (Eco Mode — local LLamaSharp)

### Intelligence:
- LLamaSharp with Vulkan backend (in-process, native .NET)
- Phi-4-mini GGUF Q4_K_M (3.8B params, 4-bit quantized)
- Vulkan auto-detects: discrete GPU → iGPU → CPU fallback
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
- Turbo Mode: cloud VLM via managed credit pooling
- Gemini 3 Flash for 90% of routine work (cheap)
- Claude 4.5 Sonnet for complex research/automation (expensive)
- Model routing based on task complexity (Phase 8 CloudCallPipeline)

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
| Intelligence Model | 100% Local SLM (Phi-4-mini via LLamaSharp/Vulkan) | Hybrid: Eco Mode + Turbo Mode (Gemini/Claude) |
| Primary Logic | Local Perception & Search | Deep Reasoning & Conflict Analysis |
| Sensing Capabilities | Local OCR & File Watching | GWS/365 Metadata Cloud Ingestion |
| Research Power | Search Links (Manual Research) | Multi-tab Synthesis & Structured Reports |
| Automation | None (Read-only observation) | Full "Computer Use" & Executive Action |
| Memory Sync | Single Device (Local) | Encrypted Cloud Sync & Multi-Device Continuity |
| Interventions | Local Drift Alerts & Notifications | Predictive Pattern Analysis & Resolutions |
| Cost Basis | $0 / mo (Runs on User GPU via Vulkan) | Managed Credit Pooling (Managed API) |

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
- Temporarily switches from Eco Mode to Turbo Mode for that action
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
- SLM must run on decent laptops (8GB RAM, modern CPU)
- LLamaSharp with Vulkan for GPU acceleration (no CUDA dependency)
- Discovery Skill interview must complete before first use
- Installer must be under 150MB (model downloaded separately)
- Power Mode toggle must be clear and accessible in settings
