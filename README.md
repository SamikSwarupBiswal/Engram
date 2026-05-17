# Engram

A Windows-first personal semantic operating layer that turns your digital activity into durable, source-linked memory.

## What Is Engram?

Engram captures your local digital life — files, clipboard, active windows, and eventually email/calendar — and metabolizes it into a local Markdown wiki. It remembers decisions, extracts commitments, detects contradictions, and can operate your OS to perform research or tasks on your behalf.

**Engram is a desktop app.** Install via .exe/.msi, open from Start Menu, and it just works.

## Quick Start

### For Users (Install)
1. Download `Engram_1.0.0_x64-setup.exe` from releases
2. Run the installer
3. Open Engram from Start Menu
4. Complete the Discovery Interview (2 minutes)
5. Start chatting

### For Developers (Build)
```bash
# Clone
git clone https://github.com/SamikSwarupBiswal/Engram.git
cd Engram

# Build backend
dotnet build Engram.sln
dotnet test Engram.sln

# Build desktop app (Windows PowerShell)
cd src\Engram.App
npm install
.uild-windows.ps1        # Full build + installer
.uild-windows.ps1 -Dev   # Dev mode with hot reload
```

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  Engram Desktop App (Tauri v2)                      │
│                                                     │
│  React + Tailwind  ←→  .NET 8 API Sidecar          │
│  Chat, Search, Wiki     16 endpoints               │
│  Timeline, Settings     Engram.Store services       │
│                                                     │
│  Tauri auto-spawns sidecar on launch                │
│  Sidecar killed on app close                        │
└─────────────────────────────────────────────────────┘
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Shell | Tauri v2 (Rust) |
| UI | React 19 + TypeScript + Tailwind CSS |
| Backend | .NET 8 ASP.NET Minimal API |
| Tests | xUnit (516 tests) |
| Local Store | .engram/ (JSON + Markdown) |

## Project Structure

```
Engram/
├── Artifacts/              # PRD and implementation plan
├── .planning/              # GSD planning artifacts
├── docs/                   # Architecture, ADRs, quality gate
├── src/
│   ├── Engram.Store/       # Core library (68 source files)
│   ├── Engram.Cli/         # Developer CLI
│   ├── Engram.Api/         # API sidecar (16 endpoints)
│   └── Engram.App/         # Tauri desktop app
│       ├── src/            # React frontend
│       ├── src-tauri/      # Rust Tauri shell
│       ├── build-windows.ps1
│       └── dev-windows.ps1
├── tests/
│   └── Engram.Store.Tests/ # 516 xUnit tests
└── Engram.sln
```

## Roadmap

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Repository and Runtime Foundation | ✅ Complete |
| 2 | Immutable Raw Event Store | ✅ Complete |
| 3 | Local Ingestion MVP | ✅ Complete |
| 4 | Markdown Wiki Memory | ✅ Complete |
| 5 | Local Search + Desktop Shell | ✅ Complete |
| 6 | Identity Hardening + Discovery UI | ✅ Complete |
| 7 | Salience and Drift Engine | ✅ Complete |
| 8 | Cloud Reasoning and Tier Routing | ✅ Complete |
| 9 | Google Workspace Ingestion | Next |
| 10 | Agentic Research Workflow | Planned |
| 11 | Computer-Use Automation | Planned |
| 12 | Encryption, Sync, Hardening | Planned |

## Tests

```bash
dotnet test Engram.sln    # 516/516 passing
```

## License

MIT — see [LICENSE](LICENSE).
