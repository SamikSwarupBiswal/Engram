# Codebase Structure

**Analysis Date:** 2026-05-23

## Directory Layout

```
Engram/
├── .planning/                  # Project roadmap, state, and codebase maps
├── src/                        # Core source code
│   ├── Engram.Store/          # Core backend logic library
│   │   ├── Agent/             # Research agent and web drivers
│   │   ├── Automation/        # Executable steps, Win32 input, sandboxing
│   │   ├── Billing/           # Token/energy units and subscriptions
│   │   ├── Capture/           # Local clipboard, window, and file capture
│   │   ├── Cloud/             # Cloud model APIs and audit logging
│   │   ├── Events/            # Thread-safe in-memory EventBus
│   │   ├── Google/            # Google Workspace API metadata ingestion
│   │   ├── Governance/        # Safety boundaries and constitutional audit
│   │   ├── Identity/          # Profile discovery and user constraints
│   │   ├── Inference/         # Local LLamaSharp and GPU detection
│   │   ├── Memory/            # Prompt assembly and context retrieval
│   │   ├── Metabolism/        # Background metabolism hosted service
│   │   ├── Orchestration/     # Intent classifier and TaskRouter
│   │   ├── Perception/        # Screen capture and layout hierarchy
│   │   ├── Salience/          # Decay scoring
│   │   ├── Search/            # Semantic search and morning/evening briefs
│   │   ├── Security/          # Encryption, data import/export
│   │   ├── Validation/        # Input sanitization
│   │   └── Wiki/              # WikiNode serialization and store
│   ├── Engram.Cli/            # Developer CLI console runner
│   ├── Engram.Api/            # ASP.NET Core Minimal API sidecar
│   └── Engram.App/            # Tauri shell (Rust) + React UI
│       ├── src/                # React components and dashboard views
│       ├── src-tauri/          # Tauri Rust configuration and entry
│       ├── installer.nsi       # NSIS installer configuration script
│       └── build-*.ps1         # Windows packaging and build pipelines
└── tests/                      # Validation and regression test suites
    ├── Engram.Store.Tests/    # Backend unit and integration tests
    └── Engram.Api.Tests/      # Web API integration tests
```

## Directory Purposes

**src/Engram.Store/**
- Purpose: All application backend domain logic, business rules, and persistence wrappers.
- Contains: C# class files partitioned into feature-focused namespaces.
- Key files: `EngramConfig.cs` (central configuration schema), `WorkspacePaths.cs` (workspace layouts).

**src/Engram.Cli/**
- Purpose: CLI interface for debugging, running tests, or doing standalone model invocations.
- Contains: Console entry points.
- Key files: `Program.cs` (CLI runner setup).

**src/Engram.Api/**
- Purpose: ASP.NET Minimal API sidecar serving HTTP requests from the frontend app.
- Contains: Route mapping files, Swagger/OpenAPI setup, rate limit middleware.
- Key files: `Program.cs` (defines all 97 API routes).

**src/Engram.App/**
- Purpose: Front-end client package and native Tauri build files.
- Contains: React 19 UI, Vite build configurations, Tauri Rust shell handlers.
- Key files: `src-tauri/src/main.rs` (Tauri app entry), `src-tauri/tauri.conf.json` (Tauri capability declarations).

**tests/**
- Purpose: Test files verifying system behavior across sprints and phases.
- Contains: xUnit test projects.
- Key files: `Engram.Store.Tests/` contains validation suites like `Sprint7ValidationSuite.cs`, `PerceptionTests.cs`.

## Key File Locations

**Entry Points:**
- `src/Engram.App/src-tauri/src/main.rs` - Tauri shell startup (spawns backend)
- `src/Engram.Api/Program.cs` - Minimal API server startup
- `src/Engram.Cli/Program.cs` - CLI developer entry

**Configuration:**
- `Directory.Build.props` - Solution-wide C# build properties
- `src/Engram.App/package.json` - Node package versions
- `src/Engram.App/tsconfig.json` - TypeScript configuration
- `src/Engram.App/vite.config.ts` - Vite bundling configuration

**Core Database/State:**
- `src/Engram.Store/Wiki/WikiNodeStore.cs` - Reads and writes Markdown WikiNodes
- `src/Engram.Store/RawEventWriter.cs` - Appends raw event streams to disk

## Naming Conventions

**Files:**
- PascalCase for C# files (`*Service.cs`, `*Provider.cs`, `*Controller.cs`)
- PascalCase for React JSX components (`Sidebar.tsx`, `ChatWindow.tsx`)
- kebab-case for frontend utility modules (`api-client.ts`, `theme-context.ts`)
- `*.test.cs` or `*Tests.cs` for test classes

**Directories:**
- PascalCase for C# namespaces under `Engram.Store/`
- kebab-case for React components folders

## Where to Add New Code

**Adding a Backend Endpoint:**
1. Define services/logic under the appropriate namespace in `src/Engram.Store/`
2. Create unit tests inside `tests/Engram.Store.Tests/` matching the folder namespace
3. Register the HTTP endpoint in `src/Engram.Api/Program.cs`
4. Add API integration test in `tests/Engram.Api.Tests/`

**Adding a UI Component or Dashboard View:**
1. Put component source under `src/Engram.App/src/components/` or `src/Engram.App/src/views/`
2. Connect view via React Router in `src/Engram.App/src/App.tsx`
3. Link endpoints inside the component using the Tauri-configured local port (`127.0.0.1:5000`)

---

*Structure analysis: 2026-05-23*
*Update when directory structure changes*
