# Engram Quality Gate Policy

## Rule

**Every phase MUST pass a quality gate before it is considered deliverable.**
No phase ships without industry-grade testing relevant to that phase's scope.

This is a non-negotiable project standard. The quality gate sits between
execute-phase and ship. It is not optional, not skippable, and not deferrable.

## Gate Lifecycle

```
execute-phase -> quality-gate -> verify-work -> ship
                     |
                     v
              [Test Suite Required]
              [Performance Budget]
              [Security Scan]
              [Integration Validation]
              [Manual Smoke Test]
```

## Quality Gate Steps (Every Phase)

### 1. Unit Test Coverage
- All new public APIs have unit tests
- Edge cases and error paths are tested
- Tests are deterministic and isolated (no cloud creds, no network)
- Minimum: every new method/class has at least one test
- Target: critical paths have 3+ test scenarios (happy, edge, error)

### 2. Integration Validation
- End-to-end flow for the phase's primary feature works
- Multi-component interactions are tested (not just unit mocks)
- File system tests use temp directories, never user data

### 3. Performance Budget (where applicable)
- Response times within acceptable bounds
- Memory usage within documented limits
- Background processes stay within CPU/NPU budget
- No memory leaks in long-running components

### 4. Security Checks
- No hardcoded secrets or credentials
- Consent defaults are correct (sensitive features OFF by default)
- Input validation on all user-facing entry points
- File paths are sanitized (no path traversal)
- No raw private data exposed to logs or external calls

### 5. Build & Distribution Check
- Solution builds clean (zero warnings on Release)
- All tests pass on clean clone
- No missing dependencies or broken references
- CI pipeline passes

### 6. Manual Smoke Test
- The primary user story works end-to-end by hand
- Error messages are clear and actionable
- No silent failures or swallowed exceptions

## Phase-Specific Testing Requirements

### Phase 1: Foundation + Raw Store
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Raw event serialization (all 11 fields, snake_case JSON) | Critical |
| Unit | Content hash is deterministic across runs | Critical |
| Unit | Duplicate detection returns existing result without rewrite | Critical |
| Unit | Workspace init creates all 6 folders | Critical |
| Unit | Workspace init is idempotent (run twice, no error) | Critical |
| Integration | Write event -> detect duplicate -> replay enumerates | Critical |
| Integration | CLI init -> write event -> replay from CLI | High |
| Performance | 1000 events written without degradation | Medium |
| Security | No path traversal in event_id or workspace root | High |
| Smoke | Fresh clone -> build -> test -> init -> write -> replay | Critical |

### Phase 2: Hardened Raw Store
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Atomic write (partial failure doesn't corrupt) | Critical |
| Unit | Processing status tracked without mutation | Critical |
| Integration | Replay with date/source/status filters | Critical |
| Integration | Crash recovery (kill mid-write, restart) | High |
| Performance | 10,000 events, replay completes in < 5s | Medium |

### Phase 3: Local Ingestion MVP
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Each capture source can be enabled/disabled independently | Critical |
| Unit | Excluded apps list is enforced | Critical |
| Integration | File watcher -> raw event with correct source attribution | Critical |
| Integration | Clipboard capture respects opt-in | Critical |
| Security | Excluded app content never appears in raw store | Critical |
| Security | Consent defaults: all sensitive capture OFF | Critical |
| Smoke | Enable file watcher -> drop file -> raw event appears | Critical |

### Phase 4: Markdown Wiki Memory
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Wiki node front matter parsing/generation | Critical |
| Unit | Node merge (same topic updates, not duplicates) | Critical |
| Unit | Source event links are preserved | Critical |
| Integration | Raw event -> wiki node -> index.md updated | Critical |
| Integration | Replay from raw -> wiki regeneration is idempotent | High |
| Smoke | Multiple events -> wiki has correct nodes + index | Critical |

### Phase 5: Local Search and Briefs
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Search query returns relevant wiki nodes | Critical |
| Unit | Brief includes promises, intentions, stale items | Critical |
| Integration | Search -> cited result with source links | Critical |
| Performance | Search latency < 200ms for 100 wiki nodes | High |
| Smoke | Alt+Space -> type query -> get answer with sources | Critical |

### Phase 6: Identity Hardening
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Discovery SOP generates valid identity files | Critical |
| Unit | Intervention policy allows/blocks correctly | Critical |
| Integration | Identity constraint blocks a matching intervention | Critical |
| Security | No intervention bypasses identity policy evaluator | Critical |
| Smoke | Set anti-goal -> trigger related intervention -> blocked | Critical |

### Phase 7: Salience and Drift Engine
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Salience decay follows power law formula | Critical |
| Unit | Contradictory event triggers drift alert | Critical |
| Integration | Stale node -> decay -> archive movement | Critical |
| Integration | Drift alert -> dismiss/accept/convert to wiki update | High |
| Performance | 500 nodes, decay scan completes in < 2s | Medium |

### Phase 8: Cloud Reasoning
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Model routing selects correct tier | Critical |
| Unit | Local filter reduces token ingress | High |
| Integration | Cloud call -> audit log entry with reason + cost | Critical |
| Security | Private raw data never sent without policy approval | Critical |
| Security | Budget limit enforced, no runaway costs | Critical |
| Performance | Local filtering adds < 50ms latency | Medium |

### Phase 9: Google Workspace
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | OAuth scopes are minimal and correct | Critical |
| Integration | Connect -> ingest metadata -> raw events created | Critical |
| Integration | Disconnect -> revoke -> no further ingestion | Critical |
| Security | Minimal scopes, no excess permissions | Critical |
| Security | Revocation is clean, no stale tokens | Critical |

### Phase 10: Agentic Research
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Research run model serialization | High |
| Integration | Research prompt -> source collection -> cited wiki summary | Critical |
| Integration | Failed run -> resume from persisted state | Critical |
| Performance | 5-tab research completes in < 60s | Medium |
| Smoke | Ask research question -> get cited summary with sources | Critical |

### Phase 11: Computer-Use Automation
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Permission model blocks risky actions without approval | Critical |
| Integration | Read-only automation works before write automation | Critical |
| Integration | Approval gate blocks destructive action | Critical |
| Security | Every action logged with timestamp + target + rationale | Critical |
| Security | No irreversible action without user approval | Critical |
| Smoke | Preview -> approve -> execute -> verify log | Critical |

### Phase 12: Encryption & Production
| Test Type | What to Verify | Priority |
|-----------|---------------|----------|
| Unit | Encrypt -> decrypt round-trip | Critical |
| Integration | Export all data -> verify completeness | Critical |
| Integration | Delete all data -> verify purge | Critical |
| Security | AES-256 encryption at rest verified | Critical |
| Security | Sync never exposes plaintext to backend | Critical |
| Performance | Background sensing within CPU/NPU budget | Critical |
| Smoke | Install -> capture -> encrypt -> sync -> export -> delete | Critical |

## Gate Enforcement

The quality gate is enforced by the GSD verify-work workflow with the
following additions:

1. **Test results are documented** in `UAT.md` for each phase
2. **Performance numbers are recorded** where budgets exist
3. **Security checklist is checked** for every phase
4. **No phase is marked "Done" in ROADMAP.md until gate passes**
5. **STATE.md reflects gate status** (pending/passed/failed)

## Failed Gate

If a gate fails:
1. Document the failure in UAT.md
2. Create a fix plan
3. Re-execute the failing components
4. Re-run the gate
5. Only mark phase complete when gate passes

## Source

This policy is a non-negotiable project rule documented in:
- `docs/QUALITY-GATE-POLICY.md` (this file)
- `.planning/PROJECT.md` (non-negotiables section)
