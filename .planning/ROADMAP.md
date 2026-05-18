# Roadmap: Engram

## Phases

- [x] **Phase 1: Repository and Runtime Foundation** [FREE] — 2026-05-13
- [x] **Phase 2: Immutable Raw Event Store** [FREE] — 2026-05-13
- [x] **Phase 3: Local Ingestion MVP** [FREE] — 2026-05-13
- [x] **Phase 4: Markdown Wiki Memory** [FREE] — 2026-05-13
- [x] **Phase 5: Local Search + Desktop Shell** [FREE] — 2026-05-17
- [x] **Phase 6: Identity Hardening + Discovery UI** [FREE] — 2026-05-17
- [x] **Phase 7: Salience, Drift, Inference Engine** [FREE] — 2026-05-18
- [x] **Phase 8: Cloud Reasoning + Token Billing** [PRO] — 2026-05-18
- [ ] **Phase 9: Google Workspace Metadata Ingestion** [PRO]
- [ ] **Phase 10: Agentic Research Workflow** [PRO]
- [ ] **Phase 11: Computer-Use Automation** [PRO]
- [ ] **Phase 12: Encryption, Sync, Production Hardening** [PRO]

## Current State

- Phases 1-8 complete
- 588/588 tests passing
- Desktop app built and installable (72 MB NSIS installer)
- 32 API endpoints, all connected to frontend
- Token budget system (Free ~60K, Pro 500K tokens/month)
- OpenAI-compatible provider (any API key)
- Inference engine (LLamaSharp + Vulkan)
- Auto-download model on first launch

## Next: Phase 9

Google Workspace Metadata Ingestion [PRO TIER]
- REQ-021: Gmail metadata (sender, subject, timestamps)
- REQ-022: Calendar metadata (event title, time, attendees)
- REQ-023: Drive metadata (file name, type, modified date)
- Local OCR for screenshots
- Privacy-first: metadata only, no email bodies
