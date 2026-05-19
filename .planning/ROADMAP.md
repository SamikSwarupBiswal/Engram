# Roadmap: Engram

## Phases — ALL COMPLETE

- [x] **Phase 1: Repository and Runtime Foundation** [FREE] — 2026-05-13
- [x] **Phase 2: Immutable Raw Event Store** [FREE] — 2026-05-13
- [x] **Phase 3: Local Ingestion MVP** [FREE] — 2026-05-13
- [x] **Phase 4: Markdown Wiki Memory** [FREE] — 2026-05-13
- [x] **Phase 5: Desktop Shell + Search + Briefs** [FREE] — 2026-05-17
- [x] **Phase 6: Identity Hardening + Discovery UI** [FREE] — 2026-05-17
- [x] **Phase 7: Salience, Drift, Inference Engine** [FREE] — 2026-05-18
- [x] **Phase 8: Cloud Reasoning + Token Billing** [PRO] — 2026-05-18
- [x] **Phase 9: Google Workspace Metadata** [PRO] — 2026-05-18
- [x] **Phase 10: Agentic Research Workflow** [PRO] — 2026-05-18
- [x] **Phase 11: Computer-Use Automation** [PRO] — 2026-05-18
- [x] **Phase 12: Encryption, Sync, Production Hardening** [PRO] — 2026-05-18
- [x] **Phase 13: Visual Perception Pipeline** [PRO] — 2026-05-18
- [x] **Phase 14: Quality Hardening** [PRO] — 2026-05-18
- [x] **Phase 15: Runtime Survivability** [CRITICAL] — 2026-05-19
- [x] **Phase 16: Packaged Product Validation** [CRITICAL] — 2026-05-19
- [x] **Phase 17: Engram System Prompt** [CRITICAL] — 2026-05-19

## Current State

- All 17 phases implemented
- 869/869 tests passing
- 83 API endpoints, all connected to frontend
- 10 frontend views
- Desktop app built (Tauri + React)
- NSIS installer (~77MB) with self-contained .NET
- Runtime survivability FIXED (100/100 soak test)
- Engram system prompt ACTIVE (user identity + wiki context)
- Diagnostics export + post-install validation script

## What's Next

### Priority 1: Production Deployment

1. **Code signing** — Sign the installer to prevent Windows Defender quarantine
2. **Auto-update mechanism** — Tauri has built-in updater support
3. **Crash reporting** — Capture and report sidecar crashes
4. **Telemetry opt-in** — Anonymous usage stats for debugging

### Priority 2: Intelligence Enhancement

1. **Context window management** — Summarize old conversation turns when context fills
2. **Wiki-aware responses** — Model can query wiki during conversation (tool use)
3. **Memory metabolism** — Automatic wiki node creation from conversation
4. **Drift detection in chat** — Detect when user contradicts stored goals

### Priority 3: Multi-Device

1. **Cloud sync** — Encrypted wiki sync across devices
2. **Multi-device identity** — Same user profile on multiple machines
3. **Conflict resolution** — Merge wiki changes from multiple devices

### Priority 4: Advanced Automation

1. **Browser automation** — Playwright-based web research
2. **Email automation** — Draft emails, schedule meetings
3. **File organization** — Auto-sort Downloads, rename files
4. **Calendar intelligence** — Proactive scheduling suggestions

### Priority 5: Platform Expansion

1. **macOS support** — Tauri supports macOS, need LLamaSharp backend
2. **Linux support** — Tauri + LLamaSharp work on Linux
3. **Mobile companion** — iOS/Android app for quick capture
