# Requirements: Engram

## Functional Requirements

- [ ] **REQ-001** Create a Windows-first .NET solution skeleton for Engram with service, tray/search shell, shared local store library, CLI/dev tooling, and tests. Phase: 1.
- [ ] **REQ-002** Initialize a local `.engram` workspace with `raw`, `wiki`, `runs`, `config`, `logs`, and `archives` directories. Phase: 1.
- [ ] **REQ-003** Define a typed raw event schema with event id, type, capture timestamp, source, source URI, active window, text, metadata, privacy class, content hash, and processing status. Phase: 1.
- [ ] **REQ-004** Persist raw events under `.engram/raw/YYYY-MM-DD/[event_id].json` using append-only semantics. Phase: 1.
- [ ] **REQ-005** Compute deterministic content hashes and prevent duplicate raw events without rewriting existing files. Phase: 1.
- [ ] **REQ-006** Provide a replay/import command that can read raw events and hand them to later processing pipelines. Phase: 1.
- [ ] **REQ-007** Implement local file watching for Downloads, Documents, and Desktop with source attribution and user-controlled paths. Phase: 3.
- [ ] **REQ-008** Implement opt-in clipboard and active-window/app-focus capture with pause controls and excluded-app enforcement. Phase: 3.
- [ ] **REQ-009** Define OCR provider boundaries for Windows Copilot Runtime and development fallback providers. Phase: 3.
- [ ] **REQ-010** Create Markdown wiki node schema with front matter, node types, source event links, salience, confidence, and backlinks. Phase: 4.
- [ ] **REQ-011** Convert raw events into wiki nodes for people, projects, goals, concepts, documents, receipts, and decisions. Phase: 4.
- [ ] **REQ-012** Generate `index.md` navigation over wiki nodes using Markdown links rather than mandatory vector search. Phase: 4.
- [ ] **REQ-013** Provide Alt+Space semantic search over wiki files and local metadata. Phase: 5.
- [ ] **REQ-014** Generate morning/evening briefs with source links for promises, intentions, stale items, and changed facts. Phase: 5.
- [ ] **REQ-015** Expose tray UI state for active context, capture status, pause/resume, and energy/status indicators. Phase: 5.
- [ ] **REQ-016** Implement the identity Discovery SOP and write user-confirmed identity constraints into `user_identity.md`, `priorities.md`, and `anti_goals.md`. Phase: 6.
- [ ] **REQ-017** Gate proactive interventions with identity constraints and provide a reason for allowed or blocked interventions. Phase: 6.
- [ ] **REQ-018** Implement salience decay, archive movement, and stale-node detection. Phase: 7.
- [ ] **REQ-019** Detect drift when new events contradict wiki facts or priorities, with source-linked alerts. Phase: 7.
- [ ] **REQ-020** Add cloud model routing with local filtering, cost controls, provider audit logs, and no raw private upload by default. Phase: 8.
- [ ] **REQ-021** Add Google Workspace metadata ingestion with OAuth, minimal scopes, revocation, and source-linked raw events. Phase: 9.
- [ ] **REQ-022** Build a Unified Semantic World Model to fuse wiki, operational, workflow, browser, and filesystem entities into a single coherent identity system and living semantic scene. Phase: 10.
- [ ] **REQ-023** Implement Human Trust & Coexistence boundaries, including explainability interfaces, user-configurable privacy maps, memory editing utilities, and ambient cognition restraint gates. Phase: 11.
- [ ] **REQ-024A** Implement Semantic Entropy Resistance to monitor and throttle graph inflation, prune stale narratives, and prevent attention/salience saturation. Phase: 12A.
- [ ] **REQ-024B** Establish Longitudinal Trust Stability features to restrict autonomy inflation, control permission creep, and throttle interventions under fatigue bounds. Phase: 12B.
- [ ] **REQ-024C** Implement Memory Ecology Management, supporting automatic semantic compaction, dormant entity compression, and contradiction expiration rules. Phase: 12C.
- [ ] **REQ-024D** Enforce Operational Fatigue limits to prevent retry obsession, intervention persistence, and narrative fixation. Phase: 12D.
- [ ] **REQ-024E** Provide Multi-Month Runtime Survival infrastructure, including automatic data corruption recovery, causal continuity repairs, and re-index validation tools. Phase: 12E.
- [ ] **REQ-024F** Enforce Cognitive Homeostasis, balancing semantic loads and maintaining emotional neutrality constraints. Phase: 12F.
- [ ] **REQ-024G** Support Longitudinal Human Adaptation, enabling calm, non-intrusive operations and restraint learning patterns. Phase: 12G.
- [ ] **REQ-025** Implement production-grade packaging, including Windows code-signing, automatic updater integration, crash diagnostics telemetry, and installation validation. Phase: 12.
- [ ] **REQ-026** Implement Discovery Skill interview (15-minute AI-guided SOP) that captures anti-goals, comfort triggers, and recurring anxieties into `user.md`. Phase: 6.
- [ ] **REQ-027** Integrate local SLM inference via tiered model strategy: embeddings (always on), task SLM (on demand), reasoning SLM (on demand). Must run on 4GB RAM minimum. Phase: 7.
- [ ] **REQ-028** Implement conversational GUI with chat window, streaming responses, sidebar navigation, backed by API server over Engram.Store services. Phase: 5.
- [ ] **REQ-029** Implement API server layer (ASP.NET Minimal API) exposing search, events, brief, wiki, status, and ask endpoints over Engram.Store services. Phase: 5.
- [ ] **REQ-030** Implement Energy Units system: 3 free Pro-level actions per week for Free tier users, weekly reset, in-app upgrade prompt on exhaustion. Phase: 8.
- [ ] **REQ-031** Implement in-app subscription flow: Free tier by default, Pro tier activation ($20-30/mo), 1 month subscription, no user API keys. Phase: 12.
- [ ] **REQ-032** Integrate LLamaSharp with Vulkan backend for local SLM inference (native .NET, in-process, no external SLM process). Must support AMD, Intel, and NVIDIA GPUs via Vulkan. Phase: 7.
- [ ] **REQ-033** Implement Vulkan GPU detection with fallback chain: discrete GPU → integrated GPU → CPU+SIMD. Must work on laptops with no discrete GPU. Phase: 7.
- [ ] **REQ-034** Bundle Phi-4-mini GGUF Q4_K_M as downloadable model (~2.2GB) on first run, not in installer. Cache at `%LOCALAPPDATA%/Engram/models/`. Phase: 7.
- [ ] **REQ-035** Implement Power Mode toggle (Eco/Turbo) in settings. Eco = local LLamaSharp, Turbo = cloud pipeline. Default to Eco. Phase: 8.
- [ ] **REQ-036** Implement .NET inference router: POST /v1/chat/completions endpoint that routes to LLamaSharp (Eco) or cloud pipeline (Turbo) based on Power Mode and license status. Phase: 8.
- [ ] **REQ-037** Build Tauri (Rust) desktop shell with React frontend, Tailwind CSS, shadcn/ui components, and CopilotKit integration. Tauri spawns .NET sidecar as child process. Phase: 5.
- [ ] **REQ-038** Implement installer variants: Standard (~130MB, model on first run), Offline (~2.4GB, bundled model), Runtime-Dependent (~50MB, requires .NET 8). Phase: 12.

## Non-Functional Requirements

- [ ] **NFR-001** Background sensing must stay within a documented CPU/NPU budget and expose resource diagnostics. Phase: 12.
- [ ] **NFR-002** All agent and automation runs must be idempotent and resumable from persisted run state. Phase: 1, 10, 11.
- [ ] **NFR-003** Tests must run locally without cloud credentials for all local-first phases. Phase: 1.
- [ ] **NFR-004** Privacy-sensitive features must have explicit consent defaults, audit logs, and kill switches. Phase: 3, 8, 9, 11, 12.

## Traceability

| ID | Source | Phase | Status |
|----|--------|-------|--------|
| REQ-001 | Implementation Plan section 3 and 5 | Phase 1 | Pending |
| REQ-002 | PRD section 2.2, Implementation Plan section 5 | Phase 1 | Pending |
| REQ-003 | Implementation Plan section 4 | Phase 1 | Pending |
| REQ-004 | PRD section 2.2 | Phase 1 | Pending |
| REQ-005 | Implementation Plan section 5 | Phase 1 | Pending |
| REQ-006 | Implementation Plan section 10 | Phase 1 | Pending |
| REQ-007 | PRD section 2.1 | Phase 3 | Pending |
| REQ-008 | PRD section 2.1 | Phase 3 | Pending |
| REQ-009 | PRD section 2.1 | Phase 3 | Pending |
| REQ-010 | Implementation Plan section 4 | Phase 4 | Pending |
| REQ-011 | PRD section 3.2 | Phase 4 | Pending |
| REQ-012 | PRD section 3.1 | Phase 4 | Pending |
| REQ-013 | PRD section 9 | Phase 5 | Pending |
| REQ-014 | PRD section 9 | Phase 5 | Pending |
| REQ-015 | PRD section 9 | Phase 5 | Pending |
| REQ-016 | PRD section 4 | Phase 6 | Pending |
| REQ-017 | PRD sections 4 and 9 | Phase 6 | Pending |
| REQ-018 | PRD section 3.2 | Phase 7 | Pending |
| REQ-019 | PRD section 3.2 | Phase 7 | Pending |
| REQ-020 | PRD section 8 | Phase 8 | Pending |
| REQ-021 | PRD section 2.1 | Phase 9 | Pending |
| REQ-022 | PRD section 6.1 | Phase 10 | Pending |
| REQ-023 | PRD section 6 | Phase 11 | Pending |
| REQ-024A | PRD section 10 and Implementation Plan section 5 | Phase 12A | Pending |
| REQ-024B | PRD section 10 and Implementation Plan section 5 | Phase 12B | Pending |
| REQ-024C | PRD section 10 and Implementation Plan section 5 | Phase 12C | Pending |
| REQ-024D | PRD section 10 and Implementation Plan section 5 | Phase 12D | Pending |
| REQ-024E | PRD section 10 and Implementation Plan section 5 | Phase 12E | Pending |
| REQ-024F | PRD section 10 and Implementation Plan section 5 | Phase 12F | Pending |
| REQ-024G | PRD section 10 and Implementation Plan section 5 | Phase 12G | Pending |
| REQ-025 | Product Vision — installer-based distribution | Phase 12 | Pending |
| REQ-026 | Product Vision — Discovery Skill identity hardening | Phase 6 | Pending |
| REQ-027 | Product Vision — local SLM tiered inference | Phase 7 | Pending |
| REQ-028 | Product Vision — ChatGPT-like conversational GUI | Phase 5 | Pending |
| REQ-029 | Product Vision — API server layer | Phase 5 | Pending |
| REQ-030 | Product Vision — Energy Units free trial system | Phase 8 | Pending |
| REQ-031 | Product Vision — in-app subscription flow | Phase 12 | Pending |
| REQ-032 | Product Vision — LLamaSharp Vulkan inference | Phase 7 | Pending |
| REQ-033 | Product Vision — Vulkan GPU detection fallback | Phase 7 | Pending |
| REQ-034 | Product Vision — model download on first run | Phase 7 | Pending |
| REQ-035 | Product Vision — Power Mode Eco/Turbo toggle | Phase 8 | Pending |
| REQ-036 | Product Vision — .NET inference router | Phase 8 | Pending |
| REQ-037 | Product Vision — Tauri + React + CopilotKit shell | Phase 5 | Pending |
| REQ-038 | Product Vision — installer variants (Standard/Offline/RT-dep) | Phase 12 | Pending |
| NFR-001 | PRD section 10 | Phase 12 | Pending |
| NFR-002 | PRD section 10 and Implementation Plan section 4 | Phase 1, Phase 10, Phase 11 | Pending |
| NFR-003 | Implementation Plan section 5 | Phase 1 | Pending |
| NFR-004 | Implementation Plan section 2 | Phase 3, Phase 8, Phase 9, Phase 11, Phase 12 | Pending |
