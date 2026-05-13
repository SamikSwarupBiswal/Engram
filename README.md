# Engram

A Windows-first personal semantic operating layer that turns your digital activity into durable, source-linked memory.

## What Is Engram?

Engram captures your local digital life — files, clipboard, active windows, and eventually email/calendar — and metabolizes it into a local Markdown wiki. It remembers decisions, extracts commitments, detects contradictions, and can operate your OS to perform research or tasks on your behalf.

**Core loop:**
1. Capture events locally (with explicit consent)
2. Store every source event immutably in `.engram/raw/`
3. Metabolize raw events into `.engram/wiki/` (Markdown nodes)
4. Make memory searchable via wiki links (not vector DB)
5. Detect drift between new events, stored facts, and stated priorities

## Tech Stack

- **Language:** C# on .NET 8
- **Platform:** Windows 11+
- **Tests:** xUnit
- **Local store:** `.engram/` directory (JSON + Markdown files)
- **Architecture:** Background service + tray/search UI + shared libraries

## Quick Start

```bash
# Clone
git clone https://github.com/SamikSwarupBiswal/Engram.git
cd Engram

# Build
dotnet build Engram.sln

# Test
dotnet test Engram.sln
```

## Project Structure

```
Engram/
├── Artifacts/              # PRD and implementation plan
├── .planning/              # GSD planning artifacts
├── src/
│   ├── Engram.Store/       # Local store library (.engram/ root)
│   ├── Engram.Cli/         # Developer CLI / command entrypoint
│   ├── Engram.Service/     # Background Windows service (later phases)
│   ├── Engram.Tray/        # System tray UI (later phases)
│   ├── Engram.Search/      # Alt+Space search surface (later phases)
│   ├── Engram.Connectors/  # File/clipboard/OCR/GWS watchers (later phases)
│   ├── Engram.Memory/      # Raw-to-wiki metabolizer (later phases)
│   └── Engram.Agents/      # Research/automation runners (later phases)
├── tests/
│   └── Engram.Store.Tests/ # xUnit tests
├── docs/                   # Extended documentation
│   └── adr/                # Architecture Decision Records
├── scripts/                # Dev helper scripts
└── Engram.sln
```

## Roadmap

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Repository and Runtime Foundation | Planned |
| 2 | Immutable Raw Event Store | Not started |
| 3 | Local Ingestion MVP | Not started |
| 4 | Markdown Wiki Memory | Not started |
| 5 | Local Search and Briefs | Not started |
| 6 | Identity Hardening | Not started |
| 7 | Salience and Drift Engine | Not started |
| 8 | Cloud Reasoning and Tier Routing | Not started |
| 9 | Google Workspace Metadata Ingestion | Not started |
| 10 | Agentic Research Workflow | Not started |
| 11 | Computer-Use Automation | Not started |
| 12 | Encryption, Sync, and Production Hardening | Not started |

See [ROADMAP.md](.planning/ROADMAP.md) for detailed phase descriptions.

## Documentation

- [Product Requirements Document](Artifacts/Product%20Requirements%20Document_Engram%20Full%20Specification.md)
- [Implementation Plan](Artifacts/Engram%20Implementation%20Plan.md)
- [Architecture Overview](docs/ARCHITECTURE.md)
- [Contributing Guide](CONTRIBUTING.md)
- [Architecture Decision Records](docs/adr/)

## License

MIT — see [LICENSE](LICENSE).
