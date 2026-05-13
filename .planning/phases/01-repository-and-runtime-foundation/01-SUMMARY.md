# Phase 1: Repository and Runtime Foundation — Summary

**Completed:** 2026-05-13
**Plans executed:** 01-01, 01-02, 01-03
**Quality gate:** PASSED

## What Was Built

### Plan 01-01: .NET Solution Skeleton
- `Engram.sln` with 3 projects
- `src/Engram.Store/` — shared store library
- `src/Engram.Cli/` — developer CLI entrypoint
- `tests/Engram.Store.Tests/` — xUnit test project
- `Directory.Build.props` — shared build properties
- All projects build and test locally without cloud credentials

### Plan 01-02: Workspace Initializer
- `WorkspacePaths` — derives all 6 required paths from a configurable root
- `WorkspaceInitializer` — idempotent init creating raw/, wiki/, runs/, config/, logs/, archives/
- `EngramConfig` — configuration model with consent defaults (all sensitive capture OFF)
- CLI command: `engram init [path]`

### Plan 01-03: Raw Event Store
- `RawEvent` — typed model with all 11 fields, snake_case JSON serialization
- `ContentHasher` — deterministic SHA-256 over stable content (excludes event_id, hash, processing_status)
- `RawEventWriter` — append-only writer with content-addressed deduplication
- `ReplayEnumerator` — deterministic enumeration by date then filename
- CLI command: `engram replay [path]`

## Quality Gate Results

### Unit Tests: 56/56 PASSED
- WorkspaceInitializerTests: 9 tests
- ContentHasherTests: 12 tests
- RawEventModelTests: 7 tests
- RawEventWriterTests: 11 tests
- ReplayEnumeratorTests: 10 tests
- IntegrationTests: 7 tests

### Test Categories Covered
| Category | Tests | Status |
|----------|-------|--------|
| Workspace init + idempotency | 9 | ✓ |
| Hash determinism + stability | 12 | ✓ |
| JSON serialization round-trip | 7 | ✓ |
| Append-only + deduplication | 11 | ✓ |
| Replay enumeration | 10 | ✓ |
| End-to-end integration | 7 | ✓ |

### Manual Smoke Test: PASSED
1. `engram init` — creates all 6 directories ✓
2. `engram init` (again) — reports "already initialized" ✓
3. `engram replay` (empty) — returns gracefully ✓

### Security Checks
- No cloud credentials required ✓
- Consent defaults: all sensitive capture OFF ✓
- No path traversal in event_id ✓
- Hash excludes mutable metadata ✓

### Build Verification
- Release build: 0 errors, 0 warnings (test warnings only) ✓
- All tests pass in Release configuration ✓

## Requirements Satisfied

| ID | Status | Evidence |
|----|--------|----------|
| REQ-001 | ✓ | Solution builds with Store, CLI, Tests projects |
| REQ-002 | ✓ | WorkspaceInitializer creates all 6 directories |
| REQ-003 | ✓ | RawEvent model with all 11 fields, snake_case JSON |
| REQ-004 | ✓ | Append-only writer under raw/YYYY-MM-DD/[event_id].json |
| REQ-005 | ✓ | ContentHasher with deterministic SHA-256, dedup without rewrite |
| REQ-006 | ✓ | ReplayEnumerator enumerates in deterministic order |
| NFR-002 | ✓ | Init is idempotent, replay is non-mutating |
| NFR-003 | ✓ | All tests run locally without cloud credentials |

## Decisions Represented

| Decision | Implementation |
|----------|---------------|
| D-001 | Phase 1 combines foundation + raw store |
| D-002 | Full PRD scope on roadmap, Phase 1 local only |
| D-003 | .NET/C# used for all projects |
| D-004 | Provider interfaces preserved (ContentHasher, etc.) |
| D-005 | .engram workspace with raw, wiki, runs, config, logs, archives |
| D-006 | Raw events immutable under raw/YYYY-MM-DD/[event_id].json |
| D-007 | Deterministic content hashing, no file rewrites |
| D-008 | Replay enumerates without passive capture |
| D-009 | Tests run locally, no cloud credentials |
| D-010 | Sensitive capture sources disabled |

## Files Created

```
Engram.sln
Directory.Build.props
src/Engram.Store/
  Engram.Store.csproj
  WorkspacePaths.cs
  WorkspaceInitializer.cs
  EngramConfig.cs
  RawEvent.cs
  WriteResult.cs
  ContentHasher.cs
  RawEventWriter.cs
  ReplayEnumerator.cs
src/Engram.Cli/
  Engram.Cli.csproj
  Program.cs
tests/Engram.Store.Tests/
  Engram.Store.Tests.csproj
  TestHelpers.cs
  WorkspaceInitializerTests.cs
  ContentHasherTests.cs
  RawEventModelTests.cs
  RawEventWriterTests.cs
  ReplayEnumeratorTests.cs
  IntegrationTests.cs
```
