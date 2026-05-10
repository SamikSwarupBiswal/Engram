# Phase 1 Walking Skeleton

## Purpose

The Phase 1 walking skeleton proves Engram can be built, initialized, tested, and given a local append-only memory spine before passive capture or cloud functionality exists.

## Chosen Stack

- Language/runtime: C# on .NET.
- Application shape: Windows-first background service later, with Phase 1 starting from shared store library and CLI/dev entrypoint.
- Test strategy: xUnit tests running locally without cloud credentials.
- Local workspace: `.engram`.

## Minimum End-To-End Slice

1. Build the solution.
2. Run tests.
3. Initialize `.engram` at a supplied root.
4. Write one raw event JSON file.
5. Detect the same event as duplicate by hash.
6. Replay persisted raw events.

## Directory Layout Target

```text
Engram.sln
Directory.Build.props
src/
  Engram.Store/
  Engram.Cli/
tests/
  Engram.Store.Tests/
.engram/
  raw/
  wiki/
  runs/
  config/
  logs/
  archives/
```

## Acceptance Gate

Phase 1 is ready to execute when the three plan files exist and cover:

- Solution skeleton.
- Workspace initializer.
- Raw event schema and append-only writer.
- Dedupe hash.
- Replay/import command.
