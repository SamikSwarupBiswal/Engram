# Phase 6: Identity Hardening - Context

**Gathered:** 2026-05-13
**Status:** Decisions locked
**Tier:** FREE (local-only, no cloud)

## Phase Boundary

Phase 6 adds the identity layer — explicit user constraints that gate all proactive behavior.

Out of scope:
- Cloud model calls (Phase 8)
- Google Workspace (Phase 9)
- Research automation (Phase 10)
- Computer-use (Phase 11)

## Implementation Decisions

### D-032: Identity File Format
- user_identity.md: Markdown with YAML front matter
- priorities.md: Numbered list of user priorities with confidence
- anti_goals.md: Explicit "do not" rules with severity
- All files in .engram/wiki/ (alongside other wiki nodes)

### D-033: Discovery SOP
- Interactive interview flow (CLI-based)
- 5 categories: goals, anti-goals, comfort triggers, anxieties, preferences
- Each category: 2-3 questions
- Extracts structured data from free-text answers
- Writes to identity files
- User can confirm/edit before saving

### D-034: Intervention Policy
- Every proactive action must pass through InterventionPolicy.Evaluate()
- Returns: Allowed/Blocked with reason
- Checks: anti-goals, identity constraints, time-of-day, context
- No intervention bypasses the policy
- Policy reads identity files on each evaluation (hot-reload)

### D-035: Identity Constraints
- Anti-goals have severity: low, medium, high, critical
- Critical anti-goals block ALL matching interventions
- Low anti-goals just reduce confidence
- User can edit anti_goals.md directly (no UI needed)

*Phase: 06-identity-hardening*
