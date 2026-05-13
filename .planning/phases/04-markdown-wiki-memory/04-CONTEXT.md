# Phase 4: Markdown Wiki Memory - Context

**Gathered:** 2026-05-13
**Status:** Decisions locked

## Implementation Decisions

### D-023: YAML Front Matter Format
- Fields: node_id, title, node_type, summary, salience, confidence, last_touched_at, created_at, version
- Parsed via simple YAML parser (no external dependency)
- Version field for schema evolution

### D-024: Merge Strategy (Duplicate Detection)
- Primary: normalized title match (lowercase, slug-based)
- Secondary: node_type + key field match
- No fuzzy matching — exact matches only in Phase 4
- Merge = update existing node, add new facts, update salience

### D-025: Source Links Format
- Inline: `[fact text](source:event_id "YYYY-MM-DD HH:mm")`
- Every fact must have at least one source link
- Multiple sources per fact allowed

### D-026: Index Structure
- Grouped by node_type with counts
- Each entry: title + one-line summary + [[slug]]
- Stale nodes (>30 days) marked with ⚠️
- Regenerated on each metabolizer run

### D-027: Node Types
- Person, Project, Goal, Concept, Document, Receipt, Decision
- Each type has different key fields for merge detection
- Person: name, Project: title, Goal: description, Concept: topic

*Phase: 04-markdown-wiki-memory*
