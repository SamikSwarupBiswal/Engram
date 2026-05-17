# Phase 5 UI: Tauri Frontend Shell

**Status:** In Progress
**Started:** 2026-05-17

## Goal

Build the user-facing desktop shell for Engram using Tauri v2 + React + TypeScript.
This extends the completed Phase 5 library logic (SearchEngine, BriefGenerator,
CaptureStatus) with a graphical interface.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Shell | Tauri v2 (Rust) — ~10MB native Windows app |
| UI Framework | React 19 + TypeScript |
| Styling | Tailwind CSS 3.4 |
| AI Chat | CopilotKit (streaming, markdown, tool calling) |
| Bundler | Vite 6 |

## Components

### Layout
- Titlebar (drag region, version display)
- Sidebar (Chat, Search, Wiki, Timeline, Settings)
- Main content area (view switching)

### Chat Panel
- Message list (user/assistant bubbles)
- Streaming text input (Enter to send, Shift+Enter newline)
- CopilotKit integration for AI streaming
- Power Mode indicator (Eco/Turbo)

### Search View
- Search input with Enter support
- Result list with relevance scores
- Wire to .NET API /api/search

### Wiki View
- Node type grid (People, Projects, Goals, Concepts, Documents, Decisions)
- Wire to .NET API /api/wiki

### Timeline View
- Chronological event list
- Wire to .NET API /api/events

### Settings View
- Power Mode toggle (Eco/Turbo)
- Capture source toggles
- Tier display (Free/Pro)

## Tauri Configuration
- Window: 1200x800 default, 800x600 minimum
- System tray icon with tooltip
- Sidecar config for .NET API process
- Capabilities: window management, shell open

## Files Created

```
src/Engram.App/
├── package.json                    # Dependencies
├── vite.config.ts                  # Vite + path aliases
├── tailwind.config.js              # Dark theme
├── postcss.config.js               # PostCSS
├── tsconfig.json                   # TypeScript config
├── index.html                      # Entry HTML
├── src/
│   ├── main.tsx                    # React entry
│   ├── App.tsx                     # Main layout + all views
│   ├── index.css                   # Tailwind + scrollbar styles
│   ├── lib/utils.ts                # cn() helper
│   └── components/
│       ├── layout/Titlebar.tsx     # Draggable titlebar
│       ├── sidebar/Sidebar.tsx     # Navigation sidebar
│       └── chat/ChatPanel.tsx      # Chat UI
└── src-tauri/
    ├── Cargo.toml                  # Rust dependencies
    ├── tauri.conf.json             # Tauri config
    ├── build.rs                    # Tauri build script
    ├── capabilities/default.json   # Tauri v2 permissions
    ├── icons/                      # App icons
    └── src/
        ├── main.rs                 # Rust entry
        └── lib.rs                  # Tauri commands
```

## Next Steps
1. npm install (in progress)
2. Verify TypeScript compilation
3. Add CopilotKit provider wrapping
4. Wire to mock API endpoints
5. Test Tauri build (cargo build)
