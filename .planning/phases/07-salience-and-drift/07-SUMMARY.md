# Phase 7: Salience and Drift Engine — Summary

**Completed:** 2026-05-13
**Plans executed:** 07-01, 07-02
**Quality gate:** PASSED
**Tier:** FREE (local-only, no cloud) — LAST FREE TIER PHASE

## What Was Built

### SalienceScorer (D-036)
- Power law decay: S(t) = S0 * e^(-lambda * t)
- Default lambda: 0.023/day (50% after 30 days)
- Configurable lambda for different decay rates
- ShouldArchive() check with configurable threshold
- GetStaleNodes() for batch analysis
- DaysUntilThreshold() for decay prediction

### ArchiveManager (D-037)
- ArchiveNode(): move stale nodes from wiki/ to archives/
- ArchiveStaleNodes(): batch archival of low-salience nodes
- RestoreFromArchive(): restore with salience reset to 1.0
- ListArchived(): enumerate archived nodes
- GenerateArchiveIndex(): archives/index.md with node listing

### DriftDetector (D-038)
- Keyword contradiction detection (negation patterns)
- Status change detection (completed vs blocked)
- Rule-based — no cloud model required
- Checks both Summary and Facts for matches
- Source event linking

### DriftAlertStore (D-039, D-040)
- Save/Load drift alerts to .engram/config/drift_alerts.json
- Status transitions: Pending -> Dismissed/Accepted/Converted
- Dismiss: mark as false positive
- Accept: confirm drift
- Convert: drift resolved, wiki updated
- GetStats(): total/pending/dismissed/accepted/converted counts

## Quality Gate Results

### Tests: 335/335 PASSED (was 291, +44 new)
- SalienceScorerTests: 12 tests
- ArchiveManagerTests: 10 tests
- DriftDetectorTests: 9 tests
- DriftAlertStoreTests: 9 tests
- Phase7IntegrationTests: 5 tests (removed 1)

### Requirements Satisfied
| ID | Status |
|----|--------|
| REQ-018 | ✓ Salience decay and archive movement |
| REQ-019 | ✓ Drift detection with source-linked alerts |

### Decisions Implemented
| Decision | Implementation |
|----------|---------------|
| D-036 | Power law decay with configurable lambda |
| D-037 | Archive movement with restore capability |
| D-038 | Rule-based drift detection (keyword + status) |
| D-039 | DriftAlert with status transitions |
| D-040 | Dismiss/Accept/Convert resolution flow |

## FREE TIER COMPLETE

All 7 free tier phases are now complete:
- Phase 1: Foundation + raw event store
- Phase 2: Immutable raw event store
- Phase 3: Local ingestion MVP
- Phase 4: Markdown wiki memory
- Phase 5: Local search and briefs
- Phase 6: Identity hardening
- Phase 7: Salience and drift engine

Total: 335/335 tests passing. 0 code exists yet for Pro tier.
