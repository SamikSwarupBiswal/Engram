# ADR-002: Desktop App Architecture — Tauri + .NET Sidecar

## Status

Accepted — 2026-05-17

## Context

Engram must be a downloadable desktop application, not a web app. Users install
it via a Windows installer (.msi/.exe) and open it from Start Menu or Desktop
shortcut. The app must "just work" — no terminal, no manual server startup, no
localhost URLs visible to the user.

## Decision

Engram is a Tauri v2 (Rust) desktop shell that auto-spawns a .NET 8 ASP.NET
Minimal API sidecar as a child process on launch. The React frontend connects
to the sidecar at 127.0.0.1:5000 internally. The user never sees the backend.

## Architecture

```
┌─────────────────────────────────────────────┐
│  Engram Desktop App (Tauri v2)              │
│                                             │
│  ┌─────────────────────────────────────┐    │
│  │  React + Tailwind + shadcn/ui       │    │
│  │  Chat, Search, Wiki, Timeline       │    │
│  │  → connects to 127.0.0.1:5000       │    │
│  └──────────────┬──────────────────────┘    │
│                 │ HTTP (internal)            │
│  ┌──────────────┴──────────────────────┐    │
│  │  .NET 8 API Sidecar (child process) │    │
│  │  /api/search /api/wiki /api/chat    │    │
│  │  Engram.Store services              │    │
│  └─────────────────────────────────────┘    │
│                                             │
│  Tauri spawns sidecar on app start          │
│  Tauri kills sidecar on app close           │
└─────────────────────────────────────────────┘
```

## Lifecycle

1. User downloads Engram installer (.msi or .exe)
2. Installer puts Engram in Program Files + Start Menu
3. User opens Engram from Start Menu / Desktop
4. Tauri shell starts
5. Tauri spawns .NET sidecar (engram-api) on port 5000
6. React frontend loads, connects to localhost:5000
7. User interacts with chat/search/wiki — all data local
8. User closes Engram → sidecar is killed automatically

## Build Pipeline

```powershell
# From src/Engram.App/:
.uild-windows.ps1        # Full build → produces installer
.uild-windows.ps1 -Dev   # Dev mode with hot reload
```

## Consequences

- Users never see localhost, never run terminal commands
- Single installer bundles both frontend and backend
- Sidecar is invisible — runs as child process
- Port 5000 is localhost-only, not exposed to network
- App size: ~130MB (without model), ~2.4GB (with Phi-4-mini bundled)
