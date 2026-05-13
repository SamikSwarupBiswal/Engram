# Phase 5: Local Search and Briefs - Context

**Gathered:** 2026-05-13
**Status:** Decisions locked
**Tier:** FREE (local-only, no cloud)

## Phase Boundary

Phase 5 makes Engram memory useful to the user. Adds local search, briefs, and capture status.

Out of scope:
- Cloud model calls (Phase 8)
- Google Workspace (Phase 9)
- Research automation (Phase 10)
- Computer-use (Phase 11)

## Implementation Decisions

### D-028: Search Engine Design
- Keyword-based search over wiki nodes (not vector search)
- TF-IDF inspired scoring: term frequency in node, inverse document frequency
- Search across: title, summary, facts, open questions
- Results ranked by relevance score (0.0 to 1.0)
- Results include: matching node, matching facts, relevance score, source links
- Case-insensitive, accent-insensitive matching
- Supports multi-word queries (AND semantics)

### D-029: Brief Generator Design
- Morning brief: promises, intentions, stale items, recent changes
- Evening brief: what was accomplished, what's pending, drift alerts
- Briefs cite source wiki nodes and raw events
- Brief stored as .engram/wiki/brief_morning.md and brief_evening.md
- Configurable brief schedule (not implemented in Phase 5)

### D-030: Capture Status
- Track: which sources are active, events captured count, last event time
- Pause/resume: per-source or global
- Status persisted in .engram/config/capture_state.json
- CLI: engram status shows current state

### D-031: Search Index
- In-memory index built from wiki nodes on first query
- Rebuilt when wiki changes (lazy invalidation)
- Index fields: title, summary, fact text, node type
- No external dependencies (no SQLite, no Lucene)

*Phase: 05-local-search-and-briefs*
